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
        [Min(1)] public float stageSeconds = 600;

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

        public MushroomMonster overtimeEnemy;
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

