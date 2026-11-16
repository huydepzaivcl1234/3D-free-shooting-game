using System;
using System.Collections.Generic;
using UnityEngine;
using GameData.Player.Script;

namespace MiningSimulator.Ores
{
    public enum SupportUpgradeId
    {
        Burn, ProjectileCrit, ExtraLootBox, RunSpeed, AttackSpeed, Healing, LifestealArmor, ItemDrop,
        ReviveAlly, Armor, MagicArmor, ProjectileKnockback, LowHealthShield, HurtSprint, QuestReward,
        GiantProjectile, Overtime, MissingHealth, ShieldBurst, RepeatAbility
    }

    public enum SupportUpgradeRarity { Common, Rare, Epic, Legendary }

    public sealed class SupportUpgradeDefinition
    {
        public readonly SupportUpgradeId Id;
        private readonly string nameKey, englishName;
        private readonly string descriptionKey, englishDescription;
        public readonly SupportUpgradeRarity Rarity;
        public string Name => MiningLocalization.TextKey(nameKey, englishName);
        public string Description => MiningLocalization.TextKey(descriptionKey, englishDescription);
        public string RarityLabel => Rarity switch
        {
            SupportUpgradeRarity.Rare => MiningLocalization.TextKey("SUPPORT_RARITY_RARE", "Rare"),
            SupportUpgradeRarity.Epic => MiningLocalization.TextKey("SUPPORT_RARITY_EPIC", "Epic"),
            SupportUpgradeRarity.Legendary => MiningLocalization.TextKey("SUPPORT_RARITY_LEGENDARY", "Legendary"),
            _ => MiningLocalization.TextKey("SUPPORT_RARITY_COMMON", "Common")
        };
        private Sprite icon;
        public Sprite Icon => icon ? icon : icon = Resources.Load<Sprite>("SupportUpgrades/Icons/" + Id);

        internal SupportUpgradeDefinition(SupportUpgradeId id, string name, SupportUpgradeRarity rarity, string description)
        {
            Id = id;
            string key = "SUPPORT_UPGRADE_" + id.ToString().ToUpperInvariant();
            nameKey = key + "_NAME";
            descriptionKey = key + "_DESCRIPTION";
            englishName = name;
            Rarity = rarity;
            englishDescription = description;
        }
    }

    [DisallowMultipleComponent]
    public sealed class PlayerSupportUpgrades : MonoBehaviour
    {
        public const int LevelCap = 99;
        private const string SaveKey = "Survivor.SupportUpgrades.v1";
        private const SupportUpgradeRarity Common = SupportUpgradeRarity.Common;
        private const SupportUpgradeRarity Rare = SupportUpgradeRarity.Rare;
        private const SupportUpgradeRarity Epic = SupportUpgradeRarity.Epic;
        private const SupportUpgradeRarity Legendary = SupportUpgradeRarity.Legendary;
        public static SupportUpgradeDefinition[] Definitions { get; } =
        {
            new(SupportUpgradeId.Burn, "Burn", Common, "Each burn tick deals extra magic damage equal to 1% of the enemy’s max HP. Each additional level adds 0.05 percentage points."),
            new(SupportUpgradeId.ProjectileCrit, "Critical Shots", Common, "Projectiles have a 1% chance to deal double damage. Each additional level adds 1 percentage point."),
            new(SupportUpgradeId.ExtraLootBox, "Extra Chest", Common, "After all chests are opened, there is a 5% chance to spawn one more. Each additional level adds 0.025 percentage points."),
            new(SupportUpgradeId.RunSpeed, "Movement Speed", Common, "+1 movement speed per level."),
            new(SupportUpgradeId.AttackSpeed, "Attack Speed", Common, "+1% attack speed per level."),
            new(SupportUpgradeId.Healing, "Healing", Common, "+1% healing effectiveness per level."),
            new(SupportUpgradeId.LifestealArmor, "Lifesteal Armor", Common, "Lifesteal grants armor equal to 15% of the health stolen for 5 seconds. Each additional level adds 1 armor."),
            new(SupportUpgradeId.ItemDrop, "Item Drops", Common, "Power-up drop chance increases by 1 percentage point. Each additional level adds 0.25 percentage points."),
            new(SupportUpgradeId.ReviveAlly, "Raise Ally", Common, "Kills have a 10% chance to revive an enemy as an ally with 10% of its original stats. Each additional level adds 5 to those stats."),
            new(SupportUpgradeId.Armor, "Armor", Common, "+1 physical armor per level."),
            new(SupportUpgradeId.MagicArmor, "Magic Armor", Common, "+1 magic armor per level."),
            new(SupportUpgradeId.ProjectileKnockback, "Knockback", Rare, "Projectiles gain 1% of standard knockback. Each additional level adds 0.25 percentage points."),
            new(SupportUpgradeId.LowHealthShield, "Last Stand Shield", Rare, "Below 30% HP, gain a shield equal to 50% of max HP that fades over 5 seconds. 50-second cooldown, reduced by 0.2 seconds per additional level."),
            new(SupportUpgradeId.HurtSprint, "Burst of Speed", Rare, "Taking damage continuously for 3 seconds increases movement speed by 10% for 3 seconds. 25-second cooldown, reduced by 0.1 seconds per additional level."),
            new(SupportUpgradeId.QuestReward, "Quest Bonus", Rare, "Completing a quest has a 5% chance to grant an extra card or support upgrade. Each additional level adds 0.25 percentage points."),
            new(SupportUpgradeId.GiantProjectile, "Giant Projectile", Epic, "10% chance to fire a 35% larger projectile that deals 35% extra projectile damage plus 15% of Attack Damage. Each additional level adds 1 damage."),
            new(SupportUpgradeId.Overtime, "Overtime Boost", Epic, "Gain 5% combat stats upon entering overtime. Each additional level adds 0.2 percentage points."),
            new(SupportUpgradeId.MissingHealth, "Vengeance", Epic, "Each 1% of missing HP adds 1% Attack Damage and attack speed. Each additional level adds 1 percentage point to that multiplier."),
            new(SupportUpgradeId.ShieldBurst, "Shield Burst", Epic, "Shields are 30% stronger. When a shield breaks, it pushes enemies back and deals 10 physical damage. Each additional level adds 1% burst damage."),
            new(SupportUpgradeId.RepeatAbility, "Double Cast", Legendary, "Abilities have a 50% chance to cast again. The repeat cast deals 10% of the original damage, plus 10 damage per additional level.")
        };

        private static readonly SequentialRaritySettings RarityChances = new();
        private readonly List<SupportUpgradeDefinition> eligible = new(Definitions.Length);
        [Serializable]
        private sealed class Progress
        {
            public int version = 1;
            public int[] levels;
            public List<string> claimedPillars;
        }

        [Serializable]
        public sealed class SavedCombat
        {
            public float leechArmor, leechTime, shield, shieldDecay, shieldCooldown;
            public float sprintTime, sprintCooldown, hurtAge, hurtChainAge;
        }

        internal SavedCombat CaptureCombat()
        {
            float now = Time.time;
            return new SavedCombat
            {
                leechArmor = LeechArmor, leechTime = Mathf.Max(0f, leechEnds - now),
                shield = temporaryShield, shieldDecay = shieldDecay, shieldCooldown = Mathf.Max(0f, shieldReady - now),
                sprintTime = Mathf.Max(0f, sprintEnds - now), sprintCooldown = Mathf.Max(0f, sprintReady - now),
                hurtAge = now - lastHurt, hurtChainAge = now - hurtChainStart
            };
        }

        internal void RestoreCombat(SavedCombat saved)
        {
            if (saved == null) return;
            float now = Time.time;
            leechArmor = Mathf.Max(0f, saved.leechArmor);
            leechEnds = now + Mathf.Clamp(saved.leechTime, 0f, 5f);
            temporaryShield = Mathf.Clamp(saved.shield, 0f, health.MaxHealth * .5f * ShieldStrengthMultiplier);
            shieldDecay = Mathf.Max(0f, saved.shieldDecay);
            shieldReady = now + Mathf.Clamp(saved.shieldCooldown, 0f, 50f);
            sprintEnds = now + Mathf.Clamp(saved.sprintTime, 0f, 3f);
            sprintReady = now + Mathf.Clamp(saved.sprintCooldown, 0f, 25f);
            lastHurt = now - Mathf.Max(0f, saved.hurtAge);
            hurtChainStart = now - Mathf.Max(0f, saved.hurtChainAge);
        }

        private readonly int[] levels = new int[Definitions.Length];
        private readonly HashSet<string> claimedPillars = new(StringComparer.Ordinal);
        private readonly List<CharacterHealth> shieldTargets = new();
        private CharacterHealth health;
        private SurvivorAutoCombat combat;
        private MonsterSpawnZone stage;
        private bool invalidSave, bursting;
        private float leechArmor, leechEnds, temporaryShield, shieldDecay, shieldReady;
        private float sprintEnds, sprintReady, lastHurt = -100f, hurtChainStart;

        public event Action<SupportUpgradeId> Granted;
        public int Level(SupportUpgradeId id) => (uint)id < (uint)levels.Length ? levels[(int)id] : 0;
        public bool HasClaimedPillar(string key) => !string.IsNullOrEmpty(key) && claimedPillars.Contains(key);
        public float OvertimeMultiplier => stage && stage.IsOvertime
            ? 1f + Scaled(SupportUpgradeId.Overtime, 5f, .2f) * .01f : 1f;
        public float MissingHealthPercent => health && health.Health > 0f
            ? Mathf.Clamp01(1f - health.Health / health.MaxHealth) * 100f * Level(SupportUpgradeId.MissingHealth) : 0f;
        public float RunBonus => Level(SupportUpgradeId.RunSpeed);
        public float SprintMultiplier => Time.time < sprintEnds ? 1.1f : 1f;
        public float LeechArmor => Time.time < leechEnds ? leechArmor : 0f;
        public float ShieldStrengthMultiplier => Level(SupportUpgradeId.ShieldBurst) > 0 ? 1.3f : 1f;
        public float TemporaryShield => temporaryShield;

        public static PlayerSupportUpgrades For(Component player)
        {
            if (!player || !player.TryGetComponent<PlayerStats>(out var stats)) return null;
            return stats.Support;
        }

        private void Awake()
        {
            health = GetComponent<CharacterHealth>();
            combat = GetComponent<SurvivorAutoCombat>();
            if (!Application.isPlaying) return;
            string json = GameSave.GetString(SaveKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var progress = JsonUtility.FromJson<Progress>(json);
                if (progress == null || progress.version != 1 || progress.levels == null)
                {
                    invalidSave = true;
                    return;
                }
                for (int i = 0; i < Mathf.Min(levels.Length, progress.levels.Length); i++)
                    levels[i] = Mathf.Clamp(progress.levels[i], 0, LevelCap);
                if (progress.claimedPillars != null)
                    foreach (var key in progress.claimedPillars)
                        if (!string.IsNullOrEmpty(key)) claimedPillars.Add(key);
            }
            catch (ArgumentException) { invalidSave = true; }
        }

        private void Start()
        {
            foreach (var zone in FindObjectsByType<MonsterSpawnZone>(FindObjectsSortMode.None))
                if (zone.gameObject.scene == gameObject.scene && zone.PlayerHuntMode)
                {
                    stage = zone;
                    break;
                }
            SupportUpgradeHud.Attach(this);
        }

        public float Scaled(SupportUpgradeId id, float first, float perExtra)
        {
            int level = Level(id);
            return level > 0 ? first + (level - 1) * perExtra : 0f;
        }

        public bool TryGrantRandom(out SupportUpgradeId id) => TryGrant(null, out id);

        internal bool HasEligibleUpgrade
        {
            get
            {
                foreach (var definition in Definitions)
                    if (Level(definition.Id) < LevelCap) return true;
                return false;
            }
        }

        internal bool TryBuyRandom(PlayerWallet wallet, float price, float rarityMultiplier, out SupportUpgradeId id)
        {
            id = default;
            return wallet && wallet.CurrentMoney >= price && TryGrant(null, out id, wallet, price, rarityMultiplier);
        }

        public bool TryGrantFromPillar(string key, out SupportUpgradeId id)
        {
            id = default;
            return !string.IsNullOrWhiteSpace(key) && !HasClaimedPillar(key) && TryGrant(key, out id);
        }

        private bool TryGrant(string pillar, out SupportUpgradeId id, PlayerWallet wallet = null,
            float price = 0f, float rarityMultiplier = 1f)
        {
            id = default;
            if (invalidSave || !Application.isPlaying || GameSave.SuppressWrites || !GameSave.Store.Writable ||
                RunCheckpoint.IsRestoring || !health || health.Health <= 0) return false;

            eligible.Clear();
            foreach (var definition in Definitions)
                if (Level(definition.Id) < LevelCap) eligible.Add(definition);
            if (eligible.Count == 0) return false;

            int pick;
            var chances = rarityMultiplier <= 1f ? RarityChances : new SequentialRaritySettings
            {
                legendaryPercent = RarityChances.legendaryPercent * rarityMultiplier,
                epicPercent = RarityChances.epicPercent * rarityMultiplier,
                rarePercent = RarityChances.rarePercent * rarityMultiplier
            };
            if (!SequentialRarity.TryRoll(eligible, chances, RarityForRoll, _ => 1f, out pick))
            {
                // A completed pillar still pays out after every Common upgrade has reached the cap.
                var lowest = eligible[0].Rarity;
                int fallbackCount = 1;
                while (fallbackCount < eligible.Count && eligible[fallbackCount].Rarity == lowest) fallbackCount++;
                pick = UnityEngine.Random.Range(0, fallbackCount);
            }

            if (wallet && !wallet.TrySpend(price)) return false;
            id = eligible[pick].Id;
            levels[(int)id]++;
            if (id == SupportUpgradeId.ShieldBurst && Level(id) == 1)
            {
                temporaryShield *= ShieldStrengthMultiplier;
                shieldDecay *= ShieldStrengthMultiplier;
                if (TryGetComponent<SurvivorAbilities>(out var abilities))
                    abilities.ScaleSupportShield(ShieldStrengthMultiplier);
            }
            if (pillar != null) claimedPillars.Add(pillar);
            GameSave.SetString(SaveKey, JsonUtility.ToJson(new Progress
            {
                levels = levels,
                claimedPillars = new List<string>(claimedPillars)
            }));
            GameSave.Save();
            Granted?.Invoke(id);
            return true;
        }

        private static MiningItemRarity RarityForRoll(SupportUpgradeDefinition definition) => definition.Rarity switch
        {
            Rare => MiningItemRarity.Rare,
            Epic => MiningItemRarity.Epic,
            Legendary => MiningItemRarity.Legendary,
            _ => MiningItemRarity.Common
        };

        public void RecordLifesteal(float amount)
        {
            int level = Level(SupportUpgradeId.LifestealArmor);
            if (level == 0 || amount <= 0f) return;
            leechArmor = Mathf.Max(LeechArmor, amount * .15f + level - 1);
            leechEnds = Time.time + 5f;
        }

        internal void RecordDamageTaken()
        {
            TryActivateLowHealthShield();
            int level = Level(SupportUpgradeId.HurtSprint);
            if (level == 0 || Time.time < sprintReady) return;
            float now = Time.time;
            if (now - lastHurt > 1.25f) hurtChainStart = now;
            lastHurt = now;
            if (now - hurtChainStart < 3f) return;
            sprintEnds = now + 3f;
            sprintReady = now + 25f - (level - 1) * .1f;
        }

        internal float AbsorbWithTemporaryShield(float damage)
        {
            if (damage <= 0 || temporaryShield <= 0) return damage;
            float absorbed = Mathf.Min(damage, temporaryShield);
            temporaryShield -= absorbed;
            if (temporaryShield <= 0f) BurstShield();
            return damage - absorbed;
        }

        internal void BurstShield()
        {
            int level = Level(SupportUpgradeId.ShieldBurst);
            if (level == 0 || bursting) return;
            bursting = true;
            try
            {
                Vector3 origin = transform.position;
                if (combat) combat.PlaySupportShieldBreak(origin + Vector3.up * .8f);
                foreach (var target in CharacterHealth.ActiveMonsters)
                    if (target && target.IsTargetable && (target.transform.position - origin).sqrMagnitude <= 9f)
                        shieldTargets.Add(target);
                foreach (var target in shieldTargets)
                {
                    if (!target || !target.IsTargetable) continue;
                    Knockback(target, origin, 2.5f);
                    target.DealDamage(10f * (1f + (level - 1) * .01f), CombatDamageType.Physical, gameObject);
                }
            }
            finally
            {
                shieldTargets.Clear();
                bursting = false;
            }
        }

        internal static void Knockback(CharacterHealth target, Vector3 origin, float strength)
        {
            Vector3 away = Vector3.ProjectOnPlane(target.transform.position - origin, Vector3.up).normalized;
            if (away.sqrMagnitude > .01f && target.TryGetComponent<MushroomMonster>(out var monster))
                monster.ApplySupportKnockback(away * strength);
        }

        private void Update()
        {
            if (!health || health.Health <= 0 || RunCheckpoint.IsRestoring) return;
            temporaryShield = Mathf.MoveTowards(temporaryShield, 0f, shieldDecay * Time.deltaTime);
            TryActivateLowHealthShield();
        }

        private void TryActivateLowHealthShield()
        {
            int level = Level(SupportUpgradeId.LowHealthShield);
            if (level == 0 || Time.time < shieldReady || health.Health >= health.MaxHealth * .3f) return;
            temporaryShield = health.MaxHealth * .5f * ShieldStrengthMultiplier;
            shieldDecay = temporaryShield / 5f;
            shieldReady = Time.time + 50f - (level - 1) * .2f;
        }

        internal void ResetLife()
        {
            leechArmor = leechEnds = temporaryShield = shieldDecay = shieldReady = sprintEnds = sprintReady = 0f;
            lastHurt = -100f;
        }
    }
}

