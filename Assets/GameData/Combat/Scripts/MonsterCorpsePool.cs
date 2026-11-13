using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace MiningSimulator.Ores
{
    public sealed class MonsterCorpsePool:MonoBehaviour
    {
        static readonly Dictionary<Scene,MonsterCorpsePool> scenes=new();
        readonly List<Corpse> active=new(),free=new();
        public int ActiveCount=>active.Count;
        public int CachedCount=>free.Count;
        public int CreatedCount{get;private set;}
        public int ReusedCount{get;private set;}
        public int EvictedCount{get;private set;}
        sealed class Corpse {
            public Object species;public GameObject visual;public Rigidbody[] bodies;public Collider[] shapes;
            public Transform[] transforms, bodyParents;public string[] paths;public float expires;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>scenes.Clear();
        void Awake(){int layer=LayerMask.NameToLayer("Ragdoll");if(layer<0)return;foreach(var name in new[]{"Ragdoll","Enemy","LocalPlayerBody"}){int other=LayerMask.NameToLayer(name);if(other>=0)Physics.IgnoreLayerCollision(layer,other,true);}}
        void OnDestroy(){scenes.Remove(gameObject.scene);}
        public static bool Spawn(MushroomMonster owner,Animator animator,Vector3 velocity)
        {
            var data=RagdollSettings.Current;if(!data||!data.monsterRagdolls||!animator||(!owner.GetComponent<BreadMonsterRagdoll>()&&!owner.GetComponent<PotionMonsterAbility>()))return false;
            var scene=owner.gameObject.scene;
            if(!scenes.TryGetValue(scene,out var pool)||!pool){var go=new GameObject("Ragdoll corpse pool");SceneManager.MoveGameObjectToScene(go,scene);pool=go.AddComponent<MonsterCorpsePool>();scenes[scene]=pool;}
            pool.Rent(owner,animator.transform,velocity,data);
            foreach(var renderer in animator.GetComponentsInChildren<Renderer>())renderer.enabled=false;
            animator.enabled=false;
            return true;
        }
        void Rent(MushroomMonster owner,Transform source,Vector3 velocity,RagdollSettings data)
        {
            while(active.Count>=Mathf.Max(1,data.maximumActiveCorpses)){Release(0,data);EvictedCount++;}
            Corpse corpse=null;
            for(int i=free.Count-1;i>=0;i--)if(free[i].species==owner.SpeciesData){corpse=free[i];free.RemoveAt(i);ReusedCount++;break;}
            if(corpse==null){corpse=Build(source,owner.SpeciesData,data.maximumBodiesPerCorpse);CreatedCount++;}
            for (int i = 0; i < corpse.bodies.Length; i++)
                corpse.bodies[i].transform.SetParent(corpse.bodyParents[i], false);
            corpse.visual.transform.SetPositionAndRotation(source.position,source.rotation);corpse.visual.transform.localScale=source.lossyScale;
            for(int i=0;i<corpse.transforms.Length;i++){
                var from=string.IsNullOrEmpty(corpse.paths[i])?source:source.Find(corpse.paths[i]);if(!from)continue;
                if(i!=0){corpse.transforms[i].localPosition=from.localPosition;corpse.transforms[i].localRotation=from.localRotation;corpse.transforms[i].localScale=from.localScale;}
            }
            corpse.visual.SetActive(true);
            foreach (var body in corpse.bodies)
                if (body.transform != corpse.visual.transform) body.transform.SetParent(corpse.visual.transform, true);
            foreach(var shape in corpse.shapes)shape.enabled=true;
            RagdollPhysics.Release(corpse.bodies, velocity, Vector3.zero);
            if (velocity.sqrMagnitude < 1f)
            {
                Vector3 fallDirection = -owner.transform.forward;
                if (owner.SpeciesData is PotionMonsterData)
                    RagdollPhysics.TipStandingCorpse(corpse.bodies, fallDirection + owner.transform.right * .4f, 5f);
                else
                    RagdollPhysics.TipStandingCorpse(corpse.bodies, fallDirection);
            }
            corpse.expires=Time.time+data.corpseSeconds;active.Add(corpse);
        }
        Corpse Build(Transform source,Object species,int bodyLimit)
        {
            // Copy graphics only: never instantiate AI, reward or health components on a corpse.
            var mapping=new Dictionary<Transform,Transform>();
            Transform CopyTree(Transform from,Transform parent){var to=new GameObject(from.name).transform;to.SetParent(parent,false);to.localPosition=from.localPosition;to.localRotation=from.localRotation;to.localScale=from.localScale;to.gameObject.SetActive(from.gameObject.activeSelf);mapping[from]=to;foreach(Transform child in from)CopyTree(child,to);return to;}
            var visual=CopyTree(source,transform).gameObject;visual.name=(species?species.name:source.name)+" corpse";
            foreach(var pair in mapping){
                var from=pair.Key;var to=pair.Value;
                var mesh=from.GetComponent<MeshFilter>();if(mesh){to.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;var renderer=from.GetComponent<MeshRenderer>();if(renderer){var r=to.gameObject.AddComponent<MeshRenderer>();r.sharedMaterials=renderer.sharedMaterials;r.shadowCastingMode=renderer.shadowCastingMode;r.receiveShadows=renderer.receiveShadows;r.enabled=renderer.enabled;}}
                var skin=from.GetComponent<SkinnedMeshRenderer>();if(skin){var r=to.gameObject.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=skin.sharedMesh;r.sharedMaterials=skin.sharedMaterials;r.localBounds=skin.localBounds;r.shadowCastingMode=skin.shadowCastingMode;r.receiveShadows=skin.receiveShadows;r.updateWhenOffscreen=true;r.enabled=skin.enabled;var remapped=new Transform[skin.bones.Length];for(int k=0;k<remapped.Length;k++)if(skin.bones[k]&&mapping.TryGetValue(skin.bones[k],out var bone))remapped[k]=bone;r.bones=remapped;if(skin.rootBone&&mapping.TryGetValue(skin.rootBone,out var rootBone))r.rootBone=rootBone;if(skin.sharedMesh)for(int k=0;k<skin.sharedMesh.blendShapeCount;k++)r.SetBlendShapeWeight(k,skin.GetBlendShapeWeight(k));}
            }
            foreach(var a in visual.GetComponentsInChildren<Animator>())a.enabled=false;
            foreach(var a in visual.GetComponentsInChildren<AudioSource>())a.enabled=false;
            foreach(var particle in visual.GetComponentsInChildren<ParticleSystem>())particle.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var all=visual.GetComponentsInChildren<Transform>(true);int layer=LayerMask.NameToLayer("Ragdoll");
            foreach(var t in all){if(layer>=0)t.gameObject.layer=layer;if(t.name.StartsWith("Held "))t.gameObject.SetActive(false);}
            foreach(var c in visual.GetComponentsInChildren<Collider>(true))c.enabled=false;
            var candidates=new HashSet<Transform>();foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())foreach(var bone in skin.bones)if(bone)candidates.Add(bone);
            var bodies=new List<Rigidbody>();
            foreach(var t in all){if(!candidates.Contains(t)||bodies.Count>=bodyLimit)continue;string n=t.name.ToLowerInvariant();
                if(n=="root"||n.Contains("finger")||n.Contains("thumb")||n.Contains("index")||n.Contains("middle")||n.Contains("ring")||n.Contains("pinky")||n.Contains("little")||n.Contains("eye")||n.Contains("brow")||n.Contains("tongue")||n.Contains("mouth")||n.Contains("cork")||n.Contains("neck")||n.Contains("headtop"))continue;
                var body=t.GetComponent<Rigidbody>();if(!body)body=t.gameObject.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;body.mass=bodies.Count==0?3:1;bodies.Add(body);
            }
            if(bodies.Count==0){var b=visual.GetComponent<Rigidbody>();if(!b)b=visual.AddComponent<Rigidbody>();b.isKinematic=true;bodies.Add(b);}
            var shapes=new List<Collider>();
            for(int i=0;i<bodies.Count;i++){
                var body = bodies[i];
                Collider shape;
                var glass = i == 0 ? visual.transform.Find("PotionMonster_Rig/Root/Body/Glass_Body") : null;
                if (glass && glass.TryGetComponent<Renderer>(out var renderer))
                {
                    var local = renderer.localBounds;
                    var toBody = body.transform.worldToLocalMatrix * glass.localToWorldMatrix;
                    var bounds = new Bounds(toBody.MultiplyPoint3x4(local.center), Vector3.zero);
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var vertex = new Vector3(
                            (corner & 1) == 0 ? local.min.x : local.max.x,
                            (corner & 2) == 0 ? local.min.y : local.max.y,
                            (corner & 4) == 0 ? local.min.z : local.max.z);
                        bounds.Encapsulate(toBody.MultiplyPoint3x4(vertex));
                    }
                    var box = body.gameObject.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = bounds.size;
                    shape = box;
                }
                else
                {
                    float scale = Mathf.Max(.0001f, Mathf.Abs(body.transform.lossyScale.x), Mathf.Abs(body.transform.lossyScale.y), Mathf.Abs(body.transform.lossyScale.z));
                    var sphere = body.gameObject.AddComponent<SphereCollider>();
                    sphere.radius = (i == 0 ? .3f : .1f) / scale;
                    shape = sphere;
                }
                shape.isTrigger=false;shapes.Add(shape);
                Rigidbody parent=null;for(var t=body.transform.parent;t&&t!=visual.transform.parent;t=t.parent){var b=t.GetComponent<Rigidbody>();if(b&&bodies.Contains(b)){parent=b;break;}}
                // Potion's arms and legs are siblings of Body, so link each detached branch to the core.
                if (!parent && i > 0) parent = bodies[0];
                if (parent)
                {
                    var joint = body.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = parent;
                    joint.anchor = Vector3.zero;
                    Vector3 axis = body.name.Contains("Shin") ? source.right : body.transform.up;
                    if (body.name.Contains("Forearm")) axis = source.up;
                    joint.axis = body.transform.InverseTransformDirection(axis);
                    var swingAxis = Mathf.Abs(Vector3.Dot(axis, source.forward)) < .9f ? source.forward : source.up;
                    joint.swingAxis = body.transform.InverseTransformDirection(Vector3.ProjectOnPlane(swingAxis, axis).normalized);
                }
            }
            for(int i=0;i<shapes.Count;i++)for(int j=i+1;j<shapes.Count;j++)Physics.IgnoreCollision(shapes[i],shapes[j]);
            var player=FindAnyObjectByType<PlayerStats>();if(player)foreach(var c in player.GetComponentsInChildren<Collider>(true))foreach(var s in shapes)Physics.IgnoreCollision(s,c);
            var corpse=new Corpse{species=species,visual=visual,bodies=bodies.ToArray(),shapes=shapes.ToArray(),transforms=all,paths=new string[all.Length]};
            corpse.bodyParents = new Transform[corpse.bodies.Length];
            for (int i = 0; i < corpse.bodies.Length; i++) corpse.bodyParents[i] = corpse.bodies[i].transform.parent;
            for(int i=0;i<all.Length;i++)corpse.paths[i]=Path(all[i],visual.transform);
            visual.SetActive(false);return corpse;
        }
        static string Path(Transform t,Transform root){if(t==root)return "";string path=t.name;while(t.parent&&t.parent!=root){t=t.parent;path=t.name+"/"+path;}return path;}
        void Release(int index,RagdollSettings data)
        {
            var c=active[index];active.RemoveAt(index);foreach(var b in c.bodies){if(!b.isKinematic){b.linearVelocity=Vector3.zero;b.angularVelocity=Vector3.zero;}b.isKinematic=true;}foreach(var s in c.shapes)if(s)s.enabled=false;c.visual.SetActive(false);free.Add(c);
            while(free.Count>Mathf.Max(0,data.maximumCachedCorpses)){var old=free[0];free.RemoveAt(0);Destroy(old.visual);}
        }
        void Update(){var data=RagdollSettings.Current;if(!data)return;for(int i=active.Count-1;i>=0;i--)if(Time.time>=active[i].expires)Release(i,data);while(active.Count>Mathf.Max(1,data.maximumActiveCorpses)){Release(0,data);EvictedCount++;}}
    }
}
