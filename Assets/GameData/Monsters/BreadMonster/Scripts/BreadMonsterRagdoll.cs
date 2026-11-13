using UnityEngine;
namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent,DefaultExecutionOrder(300)]
    public sealed class BreadMonsterRagdoll : MonoBehaviour
    {
        [SerializeField] private Rigidbody[] bones=System.Array.Empty<Rigidbody>();
        [SerializeField] private Collider[] shapes=System.Array.Empty<Collider>();
        private Vector3 previous,velocity;
        public bool Active{get;private set;}
        public void Configure(Rigidbody[] bodies,Collider[] colliders){bones=bodies;shapes=colliders;}
        private void OnEnable(){previous=transform.position;velocity=Vector3.zero;}
        private void LateUpdate(){if(!Active&&Time.deltaTime>0){velocity=(transform.position-previous)/Time.deltaTime;previous=transform.position;}}
        public void Activate()
        {
            if(Active||bones.Length==0)return;
            Active=true;
            var status=GetComponent<GameData.Player.Script.SurvivorEnemyStatus>();if(status){var position=transform.position;status.enabled=false;transform.position=position;}
            foreach(var animator in GetComponentsInChildren<Animator>())animator.enabled=false;
            foreach(var shape in shapes)if(shape)shape.enabled=true;
            for(int i=0;i<shapes.Length;i++)for(int j=i+1;j<shapes.Length;j++)if(shapes[i]&&shapes[j])Physics.IgnoreCollision(shapes[i],shapes[j]);
            foreach (var bone in bones) if (bone) bone.transform.SetParent(transform, true);
            RagdollPhysics.Release(bones, velocity, Vector3.zero);
            if (velocity.sqrMagnitude < 1f && bones[0])
                RagdollPhysics.TipStandingCorpse(bones, -transform.forward);
        }
    }
}

