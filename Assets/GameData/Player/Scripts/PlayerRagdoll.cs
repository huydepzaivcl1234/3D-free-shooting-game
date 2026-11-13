using System.Collections.Generic;
using UnityEngine;
using MiningSimulator.Ores;

namespace GameData.Player.Script
{
    [DisallowMultipleComponent,RequireComponent(typeof(CharacterHealth))]
    public sealed class PlayerRagdoll : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody pelvis;
        [SerializeField] private Rigidbody[] bones=System.Array.Empty<Rigidbody>();
        [SerializeField] private Collider[] boneColliders=System.Array.Empty<Collider>();
        [Tooltip("A small backwards nudge lets a standing corpse lose its balance naturally.")]
        [SerializeField,Min(0)] private float collapseSpeed=.65f;
        private Rigidbody rootBody;
        private Collider[] rootColliders;
        private bool[] rootColliderEnabled;
        private Vector3[] localPositions,localScales;
        private Transform[] boneParents;
        private Transform physicsRoot;
        private Quaternion[] localRotations;
        private Pose[] deathPose;
        private Transform skinPoseRoot;
        private Transform[] skinPoseBones;
        private readonly List<(SkinnedMeshRenderer renderer, Transform root, Transform[] bones)> skinBindings = new();
        private bool rootKinematic,rootGravity,animatorEnabled;
        public bool IsRagdoll{get;private set;}
        public Vector3 FollowPosition=>pelvis?pelvis.position:transform.position;
        public Rigidbody Pelvis=>pelvis;
        public string PoseRig => animator && animator.avatar ? animator.avatar.name : "";
        public System.Collections.Generic.IReadOnlyList<Rigidbody> Bodies=>bones;

        public void Configure(Animator source,Rigidbody[] bodies,Collider[] colliders)
        {animator=source;bones=bodies;boneColliders=colliders;pelvis=bodies.Length>0?bodies[0]:null;}
        private void Awake()
        {
            rootBody=GetComponent<Rigidbody>();if(!animator)animator=GetComponent<Animator>();
            rootColliders=GetComponents<Collider>();rootColliderEnabled=new bool[rootColliders.Length];
            for(int i=0;i<rootColliders.Length;i++)rootColliderEnabled[i]=rootColliders[i].enabled;
            if(rootBody){rootKinematic=rootBody.isKinematic;rootGravity=rootBody.useGravity;}
            animatorEnabled=animator&&animator.enabled;
            deathPose = new Pose[bones.Length];
            localPositions=new Vector3[bones.Length];localRotations=new Quaternion[bones.Length];localScales=new Vector3[bones.Length];boneParents=new Transform[bones.Length];
            for(int i=0;i<bones.Length;i++)if(bones[i]){boneParents[i]=bones[i].transform.parent;localScales[i]=bones[i].transform.localScale;localPositions[i]=bones[i].transform.localPosition;localRotations[i]=bones[i].transform.localRotation;bones[i].isKinematic=true;}
            foreach(var collider in boneColliders)if(collider)collider.enabled=false;
            // Self collisions in an arbitrary animated death pose can inject explosive separation forces.
            for(int i=0;i<boneColliders.Length;i++)for(int j=i+1;j<boneColliders.Length;j++)if(boneColliders[i]&&boneColliders[j])Physics.IgnoreCollision(boneColliders[i],boneColliders[j],true);
        }
        public void Activate()
        {
            if(IsRagdoll||bones.Length==0||!pelvis)return;
            Vector3 velocity=Vector3.ClampMagnitude(rootBody?rootBody.linearVelocity:Vector3.zero,25);
            Vector3 angular=Vector3.ClampMagnitude(rootBody?rootBody.angularVelocity:Vector3.zero,3);
            for (int i = 0; i < bones.Length; i++)
                if (bones[i]) deathPose[i] = new Pose(bones[i].transform.position, bones[i].transform.rotation);
            BindRagdollSkin();
            if(animator)animator.enabled=false;
            foreach(var collider in rootColliders)if(collider)collider.enabled=false;
            if(rootBody){rootBody.isKinematic=true;rootBody.useGravity=false;}
            IsRagdoll=true;
            // Nested interpolated Rigidbodies can write a parent's motion into a child twice.
            // Simulate the physics bones as siblings until respawn.
            if(!physicsRoot){physicsRoot=new GameObject("Player physics bones").transform;physicsRoot.SetParent(transform,false);}
            physicsRoot.localScale=new Vector3(1/transform.lossyScale.x,1/transform.lossyScale.y,1/transform.lossyScale.z);
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (!bone) continue;
                bone.interpolation = RigidbodyInterpolation.None;
                bone.transform.SetParent(physicsRoot, true);
                var pose = deathPose[i];
                bone.transform.SetPositionAndRotation(pose.position, pose.rotation);
                bone.position = pose.position;
                bone.rotation = pose.rotation;
            }

            // A dead player must not be crushed between dozens of moving enemy colliders.
            // Local exclusions preserve ground collision without changing the global layer matrix.
            int crowdMask=LayerMask.GetMask("Enemy","Ragdoll","LocalPlayerBody");
            foreach(var collider in boneColliders)if(collider){collider.excludeLayers |= crowdMask;collider.enabled=true;}
            for(int i=0;i<boneColliders.Length;i++)for(int j=i+1;j<boneColliders.Length;j++)if(boneColliders[i]&&boneColliders[j])Physics.IgnoreCollision(boneColliders[i],boneColliders[j],true);
            RagdollPhysics.Release(bones, velocity, angular);
            if(velocity.sqrMagnitude<1)pelvis.AddForce(-transform.forward*collapseSpeed,ForceMode.VelocityChange);
        }
        private void BindRagdollSkin()
        {
            // Keep the skin's bone lengths even when the physics solver separates connected bodies.
            skinPoseRoot = new GameObject("Ragdoll skin pose").transform;
            skinPoseRoot.SetParent(transform, false);
            var copies = new Dictionary<Transform, Transform>();

            Transform CopyBone(Transform source)
            {
                if (!source || !source.IsChildOf(transform)) return source;
                if (source == transform) return skinPoseRoot;
                if (copies.TryGetValue(source, out var copy)) return copy;

                var parent = CopyBone(source.parent);
                copy = new GameObject(source.name).transform;
                copy.SetParent(parent, false);
                copy.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
                copy.localScale = source.localScale;
                copies.Add(source, copy);
                return copy;
            }

            skinPoseBones = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                if (bones[i]) skinPoseBones[i] = CopyBone(bones[i].transform);

            foreach (var renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var original = renderer.bones;
                var mapped = new Transform[original.Length];
                for (int i = 0; i < original.Length; i++)
                    mapped[i] = CopyBone(original[i]);
                skinBindings.Add((renderer, renderer.rootBone, original));
                renderer.rootBone = CopyBone(renderer.rootBone);
                renderer.bones = mapped;
            }
        }

        private void LateUpdate()
        {
            if (!IsRagdoll || !skinPoseRoot) return;

            // Independent bones such as IK feet need world position as well as rotation.
            for (int i = 0; i < bones.Length; i++)
            {
                var body = bones[i];
                var bone = skinPoseBones[i];
                if (!body || !bone) continue;
                if (body == pelvis || !bone.IsChildOf(skinPoseBones[0])) bone.position = body.position;
                bone.rotation = body.rotation;
            }
        }

        private void UnbindRagdollSkin()
        {
            foreach (var binding in skinBindings)
            {
                if (!binding.renderer) continue;
                binding.renderer.rootBone = binding.root;
                binding.renderer.bones = binding.bones;
            }
            skinBindings.Clear();
            if (skinPoseRoot) Destroy(skinPoseRoot.gameObject);
            skinPoseRoot = null;
            skinPoseBones = null;
        }

        public void AddVelocity(Vector3 velocity)
        {
            if(!IsRagdoll)return;
            foreach(var bone in bones)if(bone&&!bone.isKinematic){bone.linearVelocity=Vector3.ClampMagnitude(bone.linearVelocity+velocity,25);bone.WakeUp();}
        }
        public void RestoreBodyPose(string path,Vector3 position,Quaternion rotation,Vector3 velocity,Vector3 angular)
        {
            string name=path.Substring(path.LastIndexOf('/')+1);
            foreach(var body in bones)if(body&&body.name==name){body.transform.SetPositionAndRotation(position,rotation);body.position=position;body.rotation=rotation;body.linearVelocity=Vector3.ClampMagnitude(velocity,25);body.angularVelocity=Vector3.ClampMagnitude(angular,body.maxAngularVelocity);break;}
        }
        public void CompletePoseRestore()
        {
            // Older checkpoints may already contain separated joints. Rejoin them parent-first.
            foreach(var body in bones)if(body){var joint=body.GetComponent<CharacterJoint>();if(joint&&joint.connectedBody){Vector3 delta=joint.connectedBody.transform.TransformPoint(joint.connectedAnchor)-body.transform.TransformPoint(joint.anchor);body.transform.position+=delta;body.position=body.transform.position;}body.WakeUp();}
            Physics.SyncTransforms();
            if(WorldNavigationGrid.Instance&&WorldNavigationGrid.Instance.TryGetGroundPoint(FollowPosition,out var ground)){
                float lowest=float.PositiveInfinity;foreach(var collider in boneColliders)if(collider&&collider.enabled)lowest=Mathf.Min(lowest,collider.bounds.min.y);
                float lift=ground.y+.015f-lowest;
                if(lift>0&&float.IsFinite(lift)){foreach(var body in bones)if(body){var position=body.position+Vector3.up*lift;body.transform.position=position;body.position=position;}Physics.SyncTransforms();}
            }
        }
        public void RestoreAnimatedPose()
        {
            if(!IsRagdoll)return;
            foreach(var collider in boneColliders)if(collider)collider.enabled=false;
            foreach(var bone in bones)if(bone){if(!bone.isKinematic){bone.linearVelocity=Vector3.zero;bone.angularVelocity=Vector3.zero;}bone.isKinematic=true;}
            for(int i=0;i<bones.Length;i++)if(bones[i]){bones[i].transform.SetParent(boneParents[i],false);bones[i].transform.localPosition=localPositions[i];bones[i].transform.localRotation=localRotations[i];bones[i].transform.localScale=localScales[i];}
            if(rootBody){rootBody.isKinematic=rootKinematic;rootBody.useGravity=rootGravity;}
            for(int i=0;i<rootColliders.Length;i++)if(rootColliders[i])rootColliders[i].enabled=rootColliderEnabled[i];
            IsRagdoll=false;
            UnbindRagdollSkin();
            GetComponent<PlayerStats>()?.ApplyBodySize();
            if(animator){animator.enabled=animatorEnabled;if(animatorEnabled){animator.Rebind();animator.Update(0);}}
            Physics.SyncTransforms();
        }
    }
}
