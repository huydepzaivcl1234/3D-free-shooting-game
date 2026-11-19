using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class MushroomMonster
    {
        public bool IsSupportAlly { get; private set; }
        private float supportMoveSpeed, supportAttackSpeed, nextSupportTargetScan;
        private CharacterHealth supportAggroTarget;
        private float nextSupportAggroScan;

        internal void InitializeSupportAlly(MonsterSpawnZone owner, CharacterHealth summoner, MushroomMonster original, int level)
        {
            IsSupportAlly = true;
            ActiveMonsters.Remove(this);
            Initialize(owner, summoner);
            health.RegisterSupportAlly();
            gameObject.name = $"{original.SpeciesData?.name ?? original.name} (Ally)";
            Level = original.Level;
            float bonus = 5f * (level - 1);
            health.ConfigureSpawnHealth(original.health.MaxHealth * .10f + bonus);
            health.ConfigureDefenses(original.health.Armor * .10f + bonus,
                original.health.MagicResistance * .10f + bonus, original.health.ResistanceScale);
            scaledDamage = original.scaledDamage * .10f + bonus;
            scaledBurnDamage = original.scaledBurnDamage > 0f ? original.scaledBurnDamage * .10f + bonus : 0f;
            supportMoveSpeed = original.BaseMoveSpeed;
            // Attack speed is stored as a multiplier, so each raw point is one percentage point here.
            supportAttackSpeed = original.AttackSpeed * .10f + bonus * .01f;
            target = null;
            targetCollider = null;
            nextSupportTargetScan = Time.time + (GetEntityId().GetHashCode() & 127) * .001f;
        }

        internal static bool IsSupportEnemy(CharacterHealth actor) =>
            actor && actor.IsMonster && actor.IsTargetable && actor.isActiveAndEnabled &&
            (!actor.TryGetComponent<MushroomMonster>(out var monster) || !monster.IsSupportAlly);

        private bool TrySelectNearbySupportAlly()
        {
            var settings = CombatData ?? legacyNavigation;
            if (Time.time >= nextSupportAggroScan)
            {
                nextSupportAggroScan = Time.time + .45f;
                float range = EffectiveDetectionRange;
                if (supportAggroTarget == target && playerDetected)
                    range += Mathf.Max(0f, settings.detectionHysteresis);
                float closest = range * range;
                CharacterHealth selected = null;
                foreach (var ally in CharacterHealth.ActiveSupportAllies)
                {
                    if (!ally || !ally.IsTargetable || !ally.isActiveAndEnabled || ally.gameObject.scene != gameObject.scene ||
                        !ally.TryGetComponent<MushroomMonster>(out var monster) || !monster.IsSupportAlly) continue;
                    float distance = Vector3.ProjectOnPlane(ally.transform.position - transform.position, Vector3.up).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    selected = ally;
                }
                supportAggroTarget = selected;
            }

            if (!supportAggroTarget || !supportAggroTarget.IsTargetable || !supportAggroTarget.isActiveAndEnabled ||
                supportAggroTarget.gameObject.scene != gameObject.scene)
            {
                supportAggroTarget = null;
                return false;
            }
            float detectionRange = EffectiveDetectionRange +
                (supportAggroTarget == target && playerDetected ? Mathf.Max(0f, settings.detectionHysteresis) : 0f);
            if (Vector3.ProjectOnPlane(supportAggroTarget.transform.position - transform.position, Vector3.up).sqrMagnitude > detectionRange * detectionRange)
            {
                supportAggroTarget = null;
                return false;
            }
            if (target != supportAggroTarget)
            {
                navigation?.Reset();
                target = supportAggroTarget;
                targetCollider = target.GetComponent<Collider>();
            }
            playerDetected = true;
            lastKnownPlayer = target.transform.position;
            return true;
        }

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
