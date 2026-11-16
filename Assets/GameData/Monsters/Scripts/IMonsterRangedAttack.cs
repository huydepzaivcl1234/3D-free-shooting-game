using UnityEngine;

namespace MiningSimulator.Ores
{
    internal static class MonsterProjectileSight
    {
        private static readonly int CoverLayers = ~LayerMask.GetMask("Enemy", "Ragdoll");

        internal static bool CanReach(Vector3 origin, Transform owner, Transform target, RaycastHit[] hits)
        {
            Vector3 ray = HomingMonsterProjectile.AimPoint(target) - origin;
            float distance = ray.magnitude;
            if (distance < .03f) return true;
            Vector3 direction = ray / distance;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, CoverLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(origin, direction, distance, CoverLayers, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider.transform.IsChildOf(owner) || collider.transform.IsChildOf(target)) continue;
                var health = collider.GetComponentInParent<CharacterHealth>();
                if (!health || !health.IsMonster) return false;
            }
            return true;
        }
    }

    public interface IMonsterRangedAttack
    {
        bool IsReady { get; }
        string FireState { get; }
        float ReleaseMoment { get; }
        float Range { get; }
        bool CanReach(Transform victim);
        void Fire(MushroomMonster owner, Transform victim);
    }
}
