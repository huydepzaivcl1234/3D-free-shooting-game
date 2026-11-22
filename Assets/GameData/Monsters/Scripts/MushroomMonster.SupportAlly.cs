using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        internal static readonly MonsterSpawnSettings.AllySettings DefaultAllySettings = new();
        internal MonsterSpawnSettings.AllySettings AllyTuning =>
            (spawnSettings ? spawnSettings.supportAlly : null) ?? DefaultAllySettings;

        internal static MonsterSpawnSettings.AllySettings ActiveAllyTuning
        {
            get
            {
                foreach (var zone in activeZones)
                    if (zone && zone.PlayerHuntMode) return zone.AllyTuning;
                return DefaultAllySettings;
            }
        }
    }

    public sealed partial class MushroomMonster
    {
        public bool IsSupportAlly { get; private set; }
        internal CharacterHealth CombatTarget => target;
        private float supportMoveSpeed, supportAttackSpeed, nextSupportTargetScan;
        private CharacterHealth supportAggroTarget;
        private float nextSupportAggroScan;
        private int knownAllyCount;
        private PlayerSupportUpgrades summonerUpgrades;
        private int appliedSupportLevel;
        private float inheritedHealth, inheritedArmor, inheritedResistance, inheritedDamage, inheritedBurn;
        private float inheritedResistanceScale;
        private MonsterSpawnSettings.AllySettings AllyTuning => zone ? zone.AllyTuning : MonsterSpawnZone.DefaultAllySettings;

        internal void InitializeSupportAlly(MonsterSpawnZone owner, CharacterHealth summoner, MushroomMonster original, int level)
        {
            IsSupportAlly = true;
            ActiveMonsters.Remove(this);
            Initialize(owner, summoner);
            health.RegisterSupportAlly();
            gameObject.name = original.name.Replace("(Clone)", "").TrimEnd();
            Level = original.Level;
            if (IsOvertimeGhost) SetGhostTier(original.ghostTier);

            inheritedHealth = original.health.MaxHealth;
            inheritedArmor = original.health.Armor;
            inheritedResistance = original.health.MagicResistance;
            inheritedDamage = original.scaledDamage;
            inheritedBurn = original.scaledBurnDamage;
            inheritedResistanceScale = original.health.ResistanceScale;
            summonerUpgrades = PlayerSupportUpgrades.For(summoner);
            ApplySupportLevel(level, spawning: true);
            supportMoveSpeed = original.BaseMoveSpeed * (original.IsOvertimeGhost ? original.GhostFlightSpeedMultiplier : 1f);
            supportAttackSpeed = original.AttackSpeed;
            SetCombatTarget(null);
            nextSupportTargetScan = Time.time;
        }

        private void ApplySupportLevel(int level, bool spawning = false)
        {
            appliedSupportLevel = level;
            float inherited = Mathf.Clamp01(AllyTuning.inheritedCombatPercent * .01f);
            float bonus = Mathf.Max(0, level) * Mathf.Max(0f, AllyTuning.flatPointsPerLevel);
            float maxHealth = inheritedHealth * inherited + bonus;
            if (spawning) health.ConfigureSpawnHealth(maxHealth);
            else health.ConfigureMaximumHealth(maxHealth);
            health.ConfigureDefenses(inheritedArmor * inherited + bonus,
                inheritedResistance * inherited + bonus, inheritedResistanceScale);
            scaledDamage = inheritedDamage * inherited + bonus;
            scaledBurnDamage = inheritedBurn > 0f ? inheritedBurn * inherited + bonus : 0f;
        }

        internal static bool IsSupportEnemy(CharacterHealth actor) =>
            actor && actor.IsMonster && actor.IsTargetable && actor.isActiveAndEnabled &&
            (!actor.TryGetComponent<MushroomMonster>(out var monster) || !monster.IsSupportAlly);

        private static bool ValidAlly(CharacterHealth ally, Scene scene) =>
            ally && ally.IsTargetable && ally.isActiveAndEnabled && ally.gameObject.scene == scene;

        internal static CharacterHealth NearestSupportAlly(Vector3 position, Scene scene, float range)
        {
            float closest = range > 0f ? range * range : float.PositiveInfinity;
            CharacterHealth nearest = null;
            foreach (var ally in CharacterHealth.ActiveSupportAllies)
            {
                if (!ValidAlly(ally, scene)) continue;
                float distance = (ally.transform.position - position).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                nearest = ally;
            }
            return nearest;
        }

        internal static bool IsAllyCloser(Vector3 position, Scene scene, CharacterHealth ally,
            CharacterHealth player, float range)
        {
            if (!ValidAlly(ally, scene)) return false;

            float distance = (ally.transform.position - position).sqrMagnitude;
            if (range > 0f && distance > range * range) return false;

            return !player || !player.IsTargetable || !player.isActiveAndEnabled || player.gameObject.scene != scene ||
                distance < (player.transform.position - position).sqrMagnitude;
        }

        private void SetCombatTarget(CharacterHealth next)
        {
            if (target == next) return;
            target = next;
            targetCollider = target ? target.GetComponent<Collider>() : null;
            navigation?.Reset();
        }

        private bool TrySelectCloserAlly()
        {
            int count = CharacterHealth.ActiveSupportAllies.Count;
            if (count == 0)
            {
                knownAllyCount = 0;
                supportAggroTarget = null;
                return false;
            }

            float range = AllyTuning.enemyAggroRange;
            if (count != knownAllyCount || (supportAggroTarget && !ValidAlly(supportAggroTarget, gameObject.scene)) ||
                Time.time >= nextSupportAggroScan)
            {
                knownAllyCount = count;
                nextSupportAggroScan = Time.time + Mathf.Max(.05f, AllyTuning.targetRefreshSeconds);
                supportAggroTarget = NearestSupportAlly(transform.position, gameObject.scene, range);
            }

            if (!IsAllyCloser(transform.position, gameObject.scene, supportAggroTarget, playerTarget, range))
                return false;

            SetCombatTarget(supportAggroTarget);
            playerDetected = true;
            lastKnownPlayer = supportAggroTarget.transform.position;
            return true;
        }

        private bool UpdateSupportTarget()
        {
            if (Time.time >= nextSupportTargetScan || (target && !IsSupportEnemy(target)))
            {
                nextSupportTargetScan = Time.time + Mathf.Max(.05f, AllyTuning.targetRefreshSeconds);
                int currentLevel = summonerUpgrades ? summonerUpgrades.Level(SupportUpgradeId.ReviveAlly) : appliedSupportLevel;
                if (currentLevel != appliedSupportLevel) ApplySupportLevel(currentLevel);
                float range = AllyTuning.allySearchRange;
                float closest = range > 0f ? range * range : float.PositiveInfinity;
                CharacterHealth selected = null;
                foreach (var enemy in CharacterHealth.ActiveMonsters)
                {
                    if (!IsSupportEnemy(enemy) || enemy.gameObject.scene != gameObject.scene) continue;
                    float distance = (enemy.transform.position - transform.position).sqrMagnitude;
                    if (distance >= closest) continue;
                    closest = distance;
                    selected = enemy;
                }
                SetCombatTarget(selected);
            }

            playerDetected = IsSupportEnemy(target);
            if (playerDetected) lastKnownPlayer = target.transform.position;
            return playerDetected;
        }

        private Vector3 SupportFollowPoint()
        {
            var position = transform.position;
            float distance = AllyTuning.followDistance;
            return playerTarget && playerTarget.IsTargetable &&
                (position - playerTarget.transform.position).sqrMagnitude > distance * distance
                ? playerTarget.transform.position : position;
        }

        private bool TryRememberSupportAttacker()
        {
            var source = health.LastDamageSource;
            if (!source || !source.TryGetComponent<MushroomMonster>(out var ally) ||
                !ally.IsSupportAlly || !ally.health.IsTargetable) return false;
            nextSupportAggroScan = 0f;
            return true;
        }
    }
}
