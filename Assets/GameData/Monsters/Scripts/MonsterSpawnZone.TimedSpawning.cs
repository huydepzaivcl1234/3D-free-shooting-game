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
                spawnElapsedSeconds / Mathf.Max(1, spawnSettings.stageSeconds)) : 1;

        internal float StageMoveMultiplier => 1 + Mathf.Max(0, StagePhase?.movementBonusPercent ?? 0) * .01f;
        private int spawnKills;
        public float SpawnElapsedSeconds=>spawnElapsedSeconds;
        public int SpawnKills=>spawnKills;
        public float CurrentSpawnInterval => StagePhase is { } phase ? Mathf.Max(.1f, phase.spawnInterval) : spawnSettings?spawnSettings.IntervalAfterKills(spawnKills):Mathf.Max(2f,8f-(spawnKills/10)*.5f);
        public float CurrentSpawnsPerSecond => StagePhase is { } phase
            ? IsOvertime ? 0 : Mathf.Max(1, phase.batchSize) / CurrentSpawnInterval
            : initialMonsterSpawned && spawnKills == 0 ? 0 : 1f / CurrentSpawnInterval;
        private void TickTimedSpawns(float seconds)
        {
            if(seconds<=0||Time.time<nextTimedRetry)return;
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
                if (initialMonsterSpawned && spawnCredit < Mathf.Max(.1f, phase.spawnInterval)) return;
                spawnCredit = 0;
                pendingStageSpawns = Mathf.Max(1, phase.batchSize);
            }
            int available = Mathf.Min(LivingMonsterLimit, Mathf.Max(1, phase.livingLimit)) - LivingCountOnField();
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
