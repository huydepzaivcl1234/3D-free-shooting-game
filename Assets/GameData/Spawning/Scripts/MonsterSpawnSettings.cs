using System.Collections.Generic;
using UnityEngine;
namespace MiningSimulator.Ores
{
    public enum MonsterSpawnWorld { World1, World2 }

    [CreateAssetMenu(menuName="Survivor/Spawning/Monster Spawn Settings")]
    public sealed class MonsterSpawnSettings : ScriptableObject
    {
        [Header("Slow start � first monster must be defeated")]
        [Min(.1f),Tooltip("Seconds between regular spawns after the first kill. Each spawn creates one monster.")] public float initialSpawnInterval=8f;
        [Min(1)] public int killsPerSpeedIncrease=10;
        [Min(0),Tooltip("Seconds removed from the interval after each kill milestone.")] public float intervalReductionPerMilestone=.5f;
        [Min(.1f)] public float minimumSpawnInterval=2f;
        [Min(1),Tooltip("Total living monsters across spawners. Boss/special encounter rules are separate.")] public int maximumLivingMonsters=1000;
        public float IntervalAfterKills(int kills)=>Mathf.Max(Mathf.Max(.1f,minimumSpawnInterval),Mathf.Max(.1f,initialSpawnInterval)-Mathf.Max(0,kills)/Mathf.Max(1,killsPerSpeedIncrease)*Mathf.Max(0,intervalReductionPerMilestone));

        [Header("Regular spawn schedules by world")]
        [SerializeField] private List<MonsterSpawnEntry> world1Monsters = new();
        [SerializeField] private List<MonsterSpawnEntry> world2Monsters = new();
        public bool HasWorldSchedules => world1Monsters.Count > 0 || world2Monsters.Count > 0;
        public IReadOnlyList<MonsterSpawnEntry> EntriesFor(MonsterSpawnWorld world) =>
            world == MonsterSpawnWorld.World2 ? world2Monsters : world1Monsters;

        [Header("Boss Shrines per map")]
        [Min(0)] public int world1BossShrineCount = 4;
        [Min(0)] public int world2BossShrineCount = 4;
        public int BossShrinesFor(MonsterSpawnWorld world) =>
            Mathf.Max(0, world == MonsterSpawnWorld.World2 ? world2BossShrineCount : world1BossShrineCount);

        [Header("Survivor stage and overtime")]
        [Min(1)] public float stageSeconds = 600, stageTwoSeconds = 540, stageThreeSeconds = 480;
        [Min(0)] public float difficultyPerStage = .35f;
        [Min(0)] public float eliteChancePerDifficulty = .006f;
        [Min(1)] public float eliteHealth = 3, eliteSize = 1.25f, eliteExperience = 10;
        [Range(0, 1)] public float rewardChestChance = .02f;
        [Min(0)] public float firstMinibossSecond = 180, secondMinibossSecond = 480;
        public MushroomMonster firstMiniboss, secondMiniboss, stageBoss;
        [Min(0)] public float firstSwarmSecond = 240, secondSwarmSecond = 420;
        [Min(1)] public float swarmDuration = 40, firstSwarmRate = 2, secondSwarmRate = 3;

        public float StageDuration(int stage) => Mathf.Max(1, stage switch
        {
            2 => stageTwoSeconds,
            3 => stageThreeSeconds,
            _ => stageSeconds
        });

        [System.Serializable]
        public sealed class StagePhase
        {
            [Min(0)] public float startSecond;
            [Min(1)] public int livingLimit = 25;
            [Min(.1f)] public float spawnInterval = 4;
            [Min(1)] public int batchSize = 1;
            [Min(0)] public float movementBonusPercent;
        }

        [SerializeField] private StagePhase[] stagePhases;
        [Min(1)] public float ordinaryHealthAtStageEnd = 1.75f;

        public StagePhase PhaseAt(float elapsed)
        {
            StagePhase active = null;
            if (stagePhases == null) return active;
            foreach (var phase in stagePhases)
                if (phase != null && elapsed >= phase.startSecond &&
                    (active == null || phase.startSecond >= active.startSecond))
                    active = phase;
            return active;
        }

        [System.Serializable]
        public sealed class AllySettings
        {
            [Range(0, 100)] public float reviveChancePercent = 10;
            [Range(0, 100)] public float inheritedCombatPercent = 10;
            [Min(0)] public float flatPointsPerLevel = 5;
            [Min(0), Tooltip("Maximum range at which an enemy can consider an ally as a target. Zero means unlimited.")]
            public float enemyAggroRange;
            [Min(0), Tooltip("Zero lets allies hunt the closest enemy anywhere on the map.")]
            public float allySearchRange;
            [Min(.05f)] public float targetRefreshSeconds = .25f;
            [Min(0)] public float followDistance = 3f;
        }

        [Header("Raise Ally")]
        public AllySettings supportAlly = new();

        [System.Serializable]
        public sealed class GhostSettings
        {
            [Min(0)] public float hoverHeight = .55f, contactPadding = .08f;
            [Min(0)] public float turnDegreesPerSecond = 540;
            [Min(0)] public float blackAtMinute = 1, purpleAtMinute = 6, redAtMinute = 12;
            [Min(0)] public float blackDamage = 1.2f, purpleDamage = 1.8f, redDamage = 2.25f;
            public Color whiteColor = new Color32(239, 245, 255, 255);
            public Color blackColor = new Color32(54, 49, 68, 255);
            public Color purpleColor = new Color32(159, 90, 240, 255);
            public Color redColor = new Color32(245, 68, 67, 255);
            [Min(0)] public float surgeAtMinute = 20, surgeSpeedMultiplier = 1.35f;
            [Min(0)] public float controlCapSeconds = 15, controlDecayMinutes = 15;
        }

        [Header("Overtime ghosts")]
        public GhostSettings ghost = new();
        public MushroomMonster overtimeEnemy;
        public AudioClip firstOvertimeTrack;
        [Min(1)] public int overtimeBatchSize = 3;
        [Min(.1f)] public float overtimeInterval = 3, overtimeMinimumInterval = .55f;
        [Min(0)] public float overtimeIntervalReductionPerMinute = .35f;
        [Min(.5f)] public float overtimeMinDistance = 18, overtimeMaxDistance = 35;
        [Min(0)] public float overtimeHealthPercentPerMinute = 18, overtimeDamagePercentPerMinute = 12;
        [Header("Regular monsters — horizontal distance in metres")]
        [Tooltip("Off: around the spawner. On: around the moving player. Chest defense uses its own chest settings.")]
        public bool followPlayer;
        [Min(.5f)] public float minimumDistance=12;
        [Min(.5f)] public float maximumDistance=25;
        [Tooltip("No regular monster may appear closer to the player than this distance.")]
        [Min(0)] public float minimumPlayerDistance=3;
    }
}

