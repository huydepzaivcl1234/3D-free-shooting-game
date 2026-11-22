using GameData.Player.Script;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MiningSimulator.Ores
{
    public enum StageEnemyVariant { Normal, Elite, Miniboss, PortalBoss }

    public sealed partial class MonsterSpawnZone
    {
        private int minibossMask;
        private float difficultyBonus, nextMinibossAttempt;
        private bool portalBossActive;
        private Vector3? portalPosition;

        public int CurrentStage { get; private set; } = 1;
        public bool StageRunComplete { get; private set; }
        public float CurrentStageDuration => spawnSettings ? spawnSettings.StageDuration(CurrentStage) : 600f;
        internal bool UsesStageDirector => PlayerHuntMode && spawnSettings && spawnSettings.PhaseAt(0) != null;
        public float Difficulty => UsesStageDirector
            ? 1 + (CurrentStage - 1) * Mathf.Max(0, spawnSettings.difficultyPerStage) + difficultyBonus : 1;
        public bool CanActivateStagePortal => UsesStageDirector && spawnSettings.stageBoss &&
            !StageRunComplete && !portalBossActive;

        public void AddDifficulty(float amount)
        {
            if (float.IsFinite(amount) && amount > 0 && float.IsFinite(Difficulty + amount))
                difficultyBonus += amount;
        }

        public float SwarmRate
        {
            get
            {
                if (!UsesStageDirector || IsOvertime || StageRunComplete) return 1;
                if (InSwarmWindow(spawnSettings.secondSwarmSecond)) return Mathf.Max(1, spawnSettings.secondSwarmRate);
                return InSwarmWindow(spawnSettings.firstSwarmSecond) ? Mathf.Max(1, spawnSettings.firstSwarmRate) : 1;
            }
        }

        private bool InSwarmWindow(float start) => spawnElapsedSeconds >= start &&
            spawnElapsedSeconds < start + Mathf.Max(1, spawnSettings.swarmDuration);

        private void TickDirectorEvents(float previousElapsed)
        {
            if (Time.timeScale <= 0 || previousElapsed >= CurrentStageDuration ||
                (Time.time < nextMinibossAttempt && !IsOvertime)) return;
            bool firstDue = MinibossDue(spawnSettings.firstMiniboss, spawnSettings.firstMinibossSecond, 1);
            bool secondDue = MinibossDue(spawnSettings.secondMiniboss, spawnSettings.secondMinibossSecond, 2);
            if (!firstDue && !secondDue) return;

            nextMinibossAttempt = Time.time + .5f;
            if (firstDue) TrySpawnMiniboss(spawnSettings.firstMiniboss, 1);
            if (secondDue) TrySpawnMiniboss(spawnSettings.secondMiniboss, 2);
        }

        private bool MinibossDue(MushroomMonster prefab, float at, int bit) => prefab &&
            (minibossMask & bit) == 0 && at <= CurrentStageDuration && spawnElapsedSeconds >= at;

        private void TrySpawnMiniboss(MushroomMonster prefab, int bit)
        {
            if (SpawnOne(DirectorEntry(prefab), variant: StageEnemyVariant.Miniboss))
                minibossMask |= bit;
        }

        private MonsterSpawnEntry DirectorEntry(MushroomMonster prefab)
        {
            foreach (var entry in RegularEntries)
                if (entry?.prefab == prefab) return entry;
            return new MonsterSpawnEntry { prefab = prefab };
        }

        private MonsterSpawnEntry FindDirectorEntry(string prefabName)
        {
            if (!spawnSettings) return null;
            foreach (var prefab in new[] { spawnSettings.firstMiniboss, spawnSettings.secondMiniboss, spawnSettings.stageBoss })
                if (prefab && prefab.name == prefabName) return DirectorEntry(prefab);
            return null;
        }

        private bool PlayerAtStagePortal => CanActivateStagePortal && portalPosition is { } point &&
            player && player.Health > 0 && (player.transform.position - point).sqrMagnitude <= 16f;

        private string StagePortalPrompt() => PlayerAtStagePortal
            ? MiningLocalization.Text("[E]  Challenge Stage Boss", "[E]  Thách đấu trùm màn")
            : null;

        internal void TickStagePortal(Vector3 point)
        {
            portalPosition = point;
            if (!PlayerAtStagePortal || Time.timeScale <= 0 || DebugSpawningPaused || restoringWorld ||
                (uiCoordinator && uiCoordinator.BlocksGameplay) || (stageLoot && stageLoot.ClaimsInteract) ||
                Keyboard.current?.eKey.wasPressedThisFrame != true) return;

            var entry = DirectorEntry(spawnSettings.stageBoss);
            portalBossActive = SpawnOne(entry, variant: StageEnemyVariant.PortalBoss, anchor: point) ||
                SpawnOne(entry, variant: StageEnemyVariant.PortalBoss);
            if (portalBossActive) CaptureWorldsIfDue(force: true);
        }

        private float VariantSize(StageEnemyVariant variant) => variant switch
        {
            StageEnemyVariant.Elite => Mathf.Max(1, spawnSettings.eliteSize),
            StageEnemyVariant.Miniboss => 1.35f,
            StageEnemyVariant.PortalBoss => 1.5f,
            _ => 1
        };

        private float VariantHealth(StageEnemyVariant variant) => variant switch
        {
            StageEnemyVariant.Elite => Mathf.Max(1, spawnSettings.eliteHealth),
            StageEnemyVariant.Miniboss => 5,
            StageEnemyVariant.PortalBoss => 12,
            _ => 1
        };

        private static float VariantDamage(StageEnemyVariant variant) => variant switch
        {
            StageEnemyVariant.Miniboss => 1.25f,
            StageEnemyVariant.PortalBoss => 1.6f,
            _ => 1
        };

        internal float VariantExperience(StageEnemyVariant variant) =>
            variant == StageEnemyVariant.Elite && spawnSettings ? Mathf.Max(1, spawnSettings.eliteExperience) : 1;

        internal void OnDirectorEnemyDefeated(StageEnemyVariant variant, Vector3 position)
        {
            if (!UsesStageDirector) return;
            bool scriptedBoss = variant is StageEnemyVariant.Miniboss or StageEnemyVariant.PortalBoss;
            float chance = spawnSettings.rewardChestChance * (variant == StageEnemyVariant.Elite ? 2 : 1);
            if (scriptedBoss || Random.value < chance)
                stageLoot?.GetComponent<SurvivorAbilityLoot>()?.DropReward(position);
            if (variant == StageEnemyVariant.Miniboss && playerStats)
                XPGemManager.For(playerStats).MagnetizeAllGems();
            if (variant != StageEnemyVariant.PortalBoss) return;

            portalBossActive = false;
            if (CurrentStage >= 3) StageRunComplete = true;
            else
            {
                CurrentStage++;
                spawnElapsedSeconds = spawnCredit = overtimeCredit = nextTimedRetry = nextMinibossAttempt = 0;
                pendingStageSpawns = minibossMask = 0;
                initialMonsterSpawned = false;
            }
            lastDisplayedSeconds = -1;
        }

        private void RestoreDirector(SavedWorld saved)
        {
            CurrentStage = Mathf.Clamp(saved.directorStage, 1, 3);
            difficultyBonus = float.IsFinite(saved.directorDifficultyBonus) ? Mathf.Max(0, saved.directorDifficultyBonus) : 0;
            StageRunComplete = CurrentStage == 3 && saved.directorRunComplete;
            portalBossActive = saved.monsters.Exists(monster => monster != null && monster.health > 0 &&
                monster.directorVariant == StageEnemyVariant.PortalBoss);
            if (saved.directorStage != 0) minibossMask = saved.directorMinibossMask;
            else if (UsesStageDirector)
            {
                if (spawnElapsedSeconds >= spawnSettings.firstMinibossSecond) minibossMask |= 1;
                if (spawnElapsedSeconds >= spawnSettings.secondMinibossSecond) minibossMask |= 2;
            }
        }
    }

    public sealed partial class MushroomMonster
    {
        internal StageEnemyVariant DirectorVariant { get; private set; }
        internal bool FollowsStageDirector => zone && zone.UsesStageDirector && !IsSupportAlly && !IsGodSummon;

        internal void SetDirectorVariant(StageEnemyVariant variant)
        {
            DirectorVariant = variant;
            if (variant == StageEnemyVariant.Normal || !health || !health.HealthBar) return;

            var marker = new GameObject("Enemy tier", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            marker.transform.SetParent(health.HealthBar, false);
            var label = marker.GetComponent<TMPro.TextMeshProUGUI>();
            label.font = Resources.Load<TMPro.TMP_FontAsset>("SurvivorRoboto");
            label.text = variant switch
            {
                StageEnemyVariant.Elite => "ELITE",
                StageEnemyVariant.Miniboss => "MINIBOSS",
                _ => "STAGE BOSS"
            };
            label.alignment = TMPro.TextAlignmentOptions.Bottom;
            label.fontStyle = TMPro.FontStyles.Bold;
            label.enableAutoSizing = true;
            label.fontSizeMin = 4;
            label.fontSizeMax = 18;
            label.color = new Color32(255, 194, 89, 255);
            label.raycastTarget = false;
            label.rectTransform.anchorMin = new Vector2(0, 1);
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(.5f, 0);
            label.rectTransform.sizeDelta = new Vector2(0, 16);
        }
    }
}
