using UnityEngine;
namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        private float spawnElapsedSeconds,spawnCredit,nextTimedRetry;
        private int pendingStageSpawns;
        private const int StageSpawnsPerFrame = 3;
        private bool initialMonsterSpawned;
        private MonsterSpawnSettings.StagePhase StagePhase =>
            PlayerHuntMode && spawnSettings ? spawnSettings.PhaseAt(spawnElapsedSeconds) : null;

        internal float StageHealthMultiplier => StagePhase != null
            ? Mathf.Lerp(1, Mathf.Max(1, spawnSettings.ordinaryHealthAtStageEnd),
                spawnElapsedSeconds / CurrentStageDuration) * Difficulty : 1;

        internal float StageMoveMultiplier => (1 + Mathf.Max(0, StagePhase?.movementBonusPercent ?? 0) * .01f) *
            (1 + (Difficulty - 1) * .15f);
        private int spawnKills;
        public float SpawnElapsedSeconds=>spawnElapsedSeconds;
        public int SpawnKills=>spawnKills;
        public float CurrentSpawnInterval => StagePhase is { } phase ? Mathf.Max(.1f, phase.spawnInterval / (Mathf.Min(Difficulty, 6) * SwarmRate)) : spawnSettings?spawnSettings.IntervalAfterKills(spawnKills):Mathf.Max(2f,8f-(spawnKills/10)*.5f);
        public float CurrentSpawnsPerSecond => StagePhase is { } phase
            ? IsOvertime || StageRunComplete ? 0 : Mathf.Max(1, phase.batchSize) / CurrentSpawnInterval
            : initialMonsterSpawned && spawnKills == 0 ? 0 : 1f / CurrentSpawnInterval;
        internal bool TryPrepareOpeningWave()
        {
            if (!PlayerHuntMode || !started || restoringWorld || StageRunComplete || !player ||
                player.Health <= 0f || initialMonsterSpawned || spawnKills > 0 || spawnElapsedSeconds > .05f || DebugSpawningPaused)
                return true;
            TickTimedSpawns(0f, preparingOpeningWave: true);
            return initialMonsterSpawned;
        }

        private void TickTimedSpawns(float seconds, bool preparingOpeningWave = false)
        {
            if (!preparingOpeningWave && (seconds <= 0f || Time.time < nextTimedRetry)) return;
            if (StagePhase is { } phase)
            {
                if (!IsOvertime) TickStagePhase(seconds, phase);
                else pendingStageSpawns = 0;
                return;
            }
            pendingStageSpawns = 0;
            if(initialMonsterSpawned){
                if(spawnKills==0)return;
                spawnCredit+=seconds;
                if(spawnCredit<CurrentSpawnInterval)return;
            }
            // Failed/blocked spawns never build a catch-up burst.
            if(LivingCountOnField()>=LivingMonsterLimit){spawnCredit=0;nextTimedRetry=Time.time+.25f;return;}
            bool invasion=spawnKills>0&&CurrentDailyEvent==DailyEncounterEvent.BossInvasion;
            var entry=RollMonsterEntry(invasion);
            if(entry==null||!SpawnOne(entry,invasion,invasion,regular:true)){
                spawnCredit=0;nextTimedRetry=Time.time+.25f;return;
            }
            initialMonsterSpawned=true;spawnCredit=0;
        }

        private void TickStagePhase(float seconds, MonsterSpawnSettings.StagePhase phase)
        {
            spawnCredit += seconds;
            if (pendingStageSpawns <= 0)
            {
                if (initialMonsterSpawned && spawnCredit < CurrentSpawnInterval) return;
                spawnCredit = 0;
                pendingStageSpawns = Mathf.Max(1, phase.batchSize);
            }
            int phaseLimit = Mathf.CeilToInt(Mathf.Max(1, phase.livingLimit) * SwarmRate);
            int available = Mathf.Min(LivingMonsterLimit, phaseLimit) - LivingCountOnField();
            pendingStageSpawns = Mathf.Min(pendingStageSpawns, Mathf.Max(0, available));
            int count = Mathf.Min(pendingStageSpawns, StageSpawnsPerFrame);
            bool invasion = spawnKills > 0 && CurrentDailyEvent == DailyEncounterEvent.BossInvasion;
            for (int i = 0; i < count; i++)
            {
                var entry = RollMonsterEntry(invasion);
                if (entry == null || !SpawnOne(entry, invasion, invasion, regular: true))
                {
                    pendingStageSpawns = 0;
                    nextTimedRetry = Time.time + .25f;
                    break;
                }
                pendingStageSpawns--;
                initialMonsterSpawned = true;
            }
        }
    }
}
