using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class MushroomMonster
    {
        public bool IsSupportAlly { get; private set; }
        private float supportMoveSpeed, supportAttackSpeed, nextSupportTargetScan;

        internal void InitializeSupportAlly(MonsterSpawnZone owner, CharacterHealth summoner, MushroomMonster original, int level)
        {
            IsSupportAlly = true;
            ActiveMonsters.Remove(this);
            Initialize(owner, summoner);
            health.ExcludeSupportAllyFromEnemyTargets();
            gameObject.name = $"{original.SpeciesData?.name ?? original.name} (Ally)";
            Level = original.Level;
            float bonus = 5f * (level - 1);
            health.ConfigureSpawnHealth(original.health.MaxHealth * .10f + bonus);
            health.ConfigureDefenses(original.health.Armor * .10f + bonus,
                original.health.MagicResistance * .10f + bonus, original.health.ResistanceScale);
            scaledDamage = original.scaledDamage * .10f + bonus;
            scaledBurnDamage = original.scaledBurnDamage > 0f ? original.scaledBurnDamage * .10f + bonus : 0f;
            supportMoveSpeed = original.BaseMoveSpeed * .10f + bonus;
            // Attack speed is stored as a multiplier, so each raw point is one percentage point here.
            supportAttackSpeed = original.AttackSpeed * .10f + bonus * .01f;
            target = null;
            targetCollider = null;
            nextSupportTargetScan = Time.time + (GetEntityId().GetHashCode() & 127) * .001f;
        }

        internal static bool IsSupportEnemy(CharacterHealth actor) =>
            actor && actor.IsMonster && actor.IsTargetable && actor.isActiveAndEnabled &&
            (!actor.TryGetComponent<MushroomMonster>(out var monster) || !monster.IsSupportAlly);

        private bool UpdateSupportTarget()
        {
            const float searchRangeSquared = 24f * 24f;
            var position = transform.position;
            if (Time.time >= nextSupportTargetScan)
            {
                nextSupportTargetScan = Time.time + .45f;
                float closest = searchRangeSquared;
                CharacterHealth selected = null;
                foreach (var enemy in CharacterHealth.ActiveMonsters)
                {
                    if (!IsSupportEnemy(enemy)) continue;
                    float distance = (enemy.transform.position - position).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    selected = enemy;
                }
                if (selected != target)
                {
                    target = selected;
                    targetCollider = selected ? selected.GetComponent<Collider>() : null;
                    navigation?.Reset();
                }
            }
            playerDetected = IsSupportEnemy(target);
            if (playerDetected) lastKnownPlayer = target.transform.position;
            return playerDetected;
        }

        private Vector3 SupportFollowPoint()
        {
            var position = transform.position;
            return playerTarget && playerTarget.IsTargetable &&
                (position - playerTarget.transform.position).sqrMagnitude > 9f
                ? playerTarget.transform.position : position;
        }

        private bool TryRememberSupportAttacker()
        {
            var source = health.LastDamageSource;
            if (!source || !source.TryGetComponent<MushroomMonster>(out var ally) ||
                !ally.IsSupportAlly || !ally.health.IsTargetable) return false;
            if (retaliationTarget != ally.health || !retaliationClock.Active)
            {
                retaliationTarget = ally.health;
                returningFromRetaliation = false;
                retaliationClock.Acquire();
            }
            return true;
        }
    }
}
