using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GameData.Player.Script;
namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        [Serializable] public sealed class SavedMonster
        {
            public string prefab,id;
            public int kind,level,clip,day,appearance;
            public Vector3 position,scale;
            public Quaternion rotation;
            public float health,maxHealth,armor,resistance,resistanceScale=100,damage,burnDamage,clock,phase,cooldown,lifetime,crowdClimbHeight,godShrineStrength;
            public bool boss,summon;
            [Serializable] internal struct LegacyTraversal { public int mode; }
            [SerializeField] internal LegacyTraversal cliffTraversal;
            public bool hasLifetime;
            public void CaptureHealth(CharacterHealth h){id=RunStateFields.Id(h);health=h.Health;maxHealth=h.MaxHealth;armor=h.Armor;resistance=h.MagicResistance;resistanceScale=h.ResistanceScale;position=h.transform.position;rotation=h.transform.rotation;scale=h.transform.lossyScale;}
            public void RestoreHealth(CharacterHealth h){if(!string.IsNullOrEmpty(id))h.WorldSaveId=id;h.ConfigureSpawnHealth(maxHealth);h.RestoreSavedHealth(health);h.ConfigureDefenses(armor,resistance,resistanceScale);}
        }
        [Serializable] private sealed class SavedWorld
        {
            public int version=1;
            public float elapsed,credit,overtimeCredit;
            public bool godShrinePlaced,godShrineUsed;
            public Vector3 godShrinePoint;
            public List<GodShrineState> godShrines;
            public bool hasQuest;
            public QuestNpcEncounter.QuestEncounter quest;
            public int spawnPacingVersion,spawnKills;public bool initialMonsterSpawned;
            public int pendingStageSpawns;
            public string playerId;
            public RunCheckpoint.State runtime;
            public AluneMeteorEvent.Saved alune;
            public int day,eventKind,thiefDay,thieves;
            public bool eventAnnouncement,skullRolled,skullPending;
            public Vector3 playerPosition;
            public List<SavedMonster> monsters=new();
        }
        private bool restoringWorld,worldLoadFailed;
        private static bool worldStopping;
        private static float nextWorldSave;
        public bool IsRestoringWorld=>restoringWorld;
        public int RestoredMonsterCount {get;private set;}
        public string WorldSaveKey {get {string path=transform.name;for(var p=transform.parent;p;p=p.parent)path=p.name+"/"+path;return "Survivor.WorldMonsters.v1/"+gameObject.scene.path+"/"+path+(spawnWorld==MonsterSpawnWorld.World2?"/World2":string.Empty);}}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWorldSaving(){worldStopping=false;nextWorldSave=15;}
        internal static void ClearResetSchedules(){skullNights.Clear();worldStopping=false;nextWorldSave=Time.unscaledTime+15;}
        public static void CaptureWorldsIfDue(bool stopping=false,bool force=false)
        {
            if(RunCheckpoint.IsRestoring||worldStopping||!Application.isPlaying||!stopping&&!force&&Time.unscaledTime<nextWorldSave)return;
            nextWorldSave=Time.unscaledTime+15;
            bool includeRuntime=true;foreach(var zone in activeZones)if(zone&&zone.started&&!zone.restoringWorld&&!zone.worldLoadFailed){zone.SaveWorld(includeRuntime);includeRuntime=false;}
            if(stopping)worldStopping=true;
        }
        public void SaveWorld(bool includeRuntime=true)
        {
            if(restoringWorld||worldLoadFailed||!started||!player)return;
            var save=new SavedWorld{playerId=RunStateFields.Id(player),elapsed=spawnElapsedSeconds,credit=spawnCredit,overtimeCredit=overtimeCredit,godShrines=godShrines,hasQuest=questNpcEncounter?.State!=null,quest=questNpcEncounter?.State,spawnPacingVersion=1,spawnKills=spawnKills,initialMonsterSpawned=initialMonsterSpawned,playerPosition=player.transform.position,day=SpawnDayNumber,eventKind=(int)CurrentDailyEvent,eventAnnouncement=dailyAnnouncementPending,thiefDay=thiefDay,thieves=spawnedThieves};
            if(alune)save.alune=alune.Capture();
            save.pendingStageSpawns = pendingStageSpawns;
            if(additionalRoster&&skullNights.TryGetValue(additionalRoster,out var roll)){save.skullRolled=roll.IsRolledFor(save.day);save.skullPending=roll.Pending;}
            foreach(var m in alive)if(m&&m.Health&&m.Health.Health>0&&!m.IsDespawning&&liveSpecies.TryGetValue(m,out var entry))save.monsters.Add(m.CaptureWorld(entry.prefab.name));
            foreach(var god in GetComponentsInChildren<GodBossController>())if(god.Health&&god.Health.Health>0){var record=new SavedMonster{kind=2,day=god.EncounterDay,appearance=god.AppearanceNumber,godShrineStrength=god.ShrineStrength};record.CaptureHealth(god.Health);save.monsters.Add(record);}
            if(includeRuntime)save.runtime=RunCheckpoint.Capture(player);
            GameSave.SetString(WorldSaveKey,JsonUtility.ToJson(save));
        }
        private void StartWorldRestore()
        {
            // Upgrade old profiles once: already-unlocked normal species are not new discoveries.
            const string migration="Survivor.DiscoveryMigrated.v1";
            if(GameSave.GetInt(migration)==0){if(playerStats&&playerStats.Level>1)foreach(var e in monsters)if(e?.prefab&&e.RequiredPlayerLevel<=playerStats.Level&&e.RequiredSpawnDay<=SpawnDayNumber)SurvivorMonsterDiscovery.Remember(e.prefab);GameSave.SetInt(migration,1);}
            string json=GameSave.GetString(WorldSaveKey);if(string.IsNullOrEmpty(json))return;
            SavedWorld saved;
            try{saved=JsonUtility.FromJson<SavedWorld>(json);if(saved==null||saved.version!=1||saved.monsters==null)throw new Exception("Unsupported monster world save");}
            catch(Exception e){worldLoadFailed=true;Debug.LogWarning("Monster world save preserved: "+e.Message);return;}
            restoringWorld=true;StartCoroutine(RestoreWorld(saved));
        }
        private IEnumerator RestoreWorld(SavedWorld saved)
        {
            bool restoreRuntime = saved.runtime != null;
            var pauseCoordinator = uiCoordinator && uiCoordinator.isActiveAndEnabled ? uiCoordinator : null;
            float fallbackScale = Time.timeScale;
            TerrainGenerator terrain = null;
            try
            {
                if (restoreRuntime)
                {
                    RunCheckpoint.IsRestoring = true;
                    // Share the pause with the menu so closing it can't release an unfinished restore.
                    if (pauseCoordinator) pauseCoordinator.SyncCheckpointPause();
                    else Time.timeScale = 0f;
                }
                if(player&&!string.IsNullOrEmpty(saved.playerId))player.WorldSaveId=saved.playerId;
                while(!WorldNavigationGrid.Instance||!WorldNavigationGrid.Instance.HasBaked)yield return null;
                terrain = FindAnyObjectByType<TerrainGenerator>();
                if (terrain && terrain.MigratesLegacyRun)
                {
                    saved.playerPosition = terrain.RegroundActor(saved.playerPosition);
                    foreach (var record in saved.monsters)
                        if (record != null)
                        {
                            record.position = terrain.RegroundActor(record.position);
                            record.crowdClimbHeight = 0f;
                            record.cliffTraversal = default;
                        }
                    if (saved.quest != null)
                    {
                        saved.quest.origin = terrain.RegroundActor(saved.quest.origin);
                        if (saved.quest.placed) saved.quest.location = terrain.RegroundActor(saved.quest.location);
                    }
                    RunCheckpoint.RegroundOnNewTerrain(saved.runtime, terrain);
                }
                var navigation = WorldNavigationGrid.Instance;
                if (player && navigation)
                {
                    var capsule = player.GetComponent<CapsuleCollider>();
                    var scale = player.transform.lossyScale;
                    float radius = capsule ? capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : .5f;
                    float height = capsule ? capsule.height * Mathf.Abs(scale.y) : 1.8f;
                    Vector3 spot = navigation.ResolvePortalSpawn(saved.playerPosition, radius, height);
                    if (spot != saved.playerPosition)
                    {
                        saved.playerPosition = spot;
                        RunCheckpoint.MoveSavedActor(saved.runtime, saved.playerId, spot);
                    }
                }
                if(player){var rb=player.GetComponent<Rigidbody>();if(rb){rb.position=saved.playerPosition;rb.linearVelocity=Vector3.zero;}player.transform.position=saved.playerPosition;}
                spawnElapsedSeconds=Mathf.Max(0,saved.elapsed);
                if (saved.quest != null && (saved.hasQuest || saved.quest.placed))
                    questNpcEncounter?.Restore(saved.quest);
                overtimeCredit=Mathf.Max(0,saved.overtimeCredit);
                godShrines.Clear();
                if (saved.godShrines != null)
                    godShrines.AddRange(saved.godShrines.FindAll(shrine => shrine != null));
                else if (saved.godShrinePlaced || saved.godShrineUsed)
                    godShrines.Add(new GodShrineState { point = saved.godShrinePoint, used = saved.godShrineUsed });
                if(saved.spawnPacingVersion==1){spawnKills=Mathf.Max(0,saved.spawnKills);initialMonsterSpawned=saved.initialMonsterSpawned;spawnCredit=Mathf.Clamp(saved.credit,0,CurrentSpawnInterval);}
                else{initialMonsterSpawned=saved.monsters.Count>0||saved.elapsed>0;spawnKills=initialMonsterSpawned&&saved.monsters.Count==0?1:0;spawnCredit=0;} // Older saves keep their monsters, discard old burst credits.
                thiefDay=saved.thiefDay;spawnedThieves=saved.thieves;
                pendingStageSpawns = StagePhase is { } phase ? Mathf.Clamp(saved.pendingStageSpawns, 0, Mathf.Max(1, phase.batchSize)) : 0;
                if(saved.day==SpawnDayNumber){scheduledDay=saved.day;CurrentDailyEvent=(DailyEncounterEvent)saved.eventKind;dailyAnnouncementPending=saved.eventAnnouncement;if(additionalRoster&&saved.skullRolled){var roll=new SkullclawNightRoll();roll.Roll(saved.day,saved.skullPending?100:0,0);skullNights[additionalRoster]=roll;}}
                foreach(var record in saved.monsters){
                    if(record==null||record.health<=0)continue;
                    if(record.kind==2){
                        if(!godBossData||!godBossData.prefab){worldLoadFailed=true;continue;}
                        var godMotor = godBossData.prefab.GetComponent<CharacterController>();
                        ResolvePortalRecord(record, saved.runtime, navigation, godMotor);
                        var god=Instantiate(godBossData.prefab,record.position,record.rotation,transform);god.Initialize(this,player,record.day,Mathf.Max(1,record.appearance),record.godShrineStrength);record.RestoreHealth(god.Health);god.transform.localScale=Vector3.Scale(record.scale,new Vector3(1/transform.lossyScale.x,1/transform.lossyScale.y,1/transform.lossyScale.z));god.Defeated+=OnGodDefeated;godBoss=god;RestoredMonsterCount++;continue;
                    }
                    MonsterSpawnEntry entry = null;
                    foreach (var candidate in RegularEntries)
                        if (candidate?.prefab && candidate.prefab.name == record.prefab) { entry = candidate; break; }
                    entry ??= monsters.Find(e => e?.prefab && e.prefab.name == record.prefab);
                    if(entry==null&&additionalRoster){foreach(var special in new[]{additionalRoster.nightThief,additionalRoster.nightSkullclaw})if(special?.prefab&&special.prefab.name==record.prefab){entry=special;break;}}
                    if(entry==null && GetOvertimeEntry() is MonsterSpawnEntry overtime && overtime.prefab.name==record.prefab)entry=overtime;
                    if(entry==null){worldLoadFailed=true;continue;}
                    if (record.kind == 0 && terrain && (record.cliffTraversal.mode != 0 || record.crowdClimbHeight > .01f))
                        record.position = terrain.RegroundActor(record.position);
                    ResolvePortalRecord(record, saved.runtime, navigation, entry.prefab.GetComponent<CharacterController>());
                    var m = Instantiate(entry.prefab, record.position, record.rotation, transform);
                    m.Initialize(this, player, record.boss, record.summon);
                    m.RestoreWorld(record);
                    alive.Add(m);
                    liveSpecies[m] = entry;
                    if (record.summon) godSummons.Add(m);
                    RestoredMonsterCount++;
                }
                if(saved.alune!=null)Alune.Restore(saved.alune);else if(alune)alune.Clear();
                restoringWorld=false;
                if (restoreRuntime)
                {
                    yield return null;
                    while (Array.Exists(FindObjectsByType<MonsterSpawnZone>(FindObjectsSortMode.None), zone => zone.IsRestoringWorld))
                        yield return null;
                    try { RunCheckpoint.Restore(saved.runtime, player); }
                    catch (Exception e)
                    {
                        worldLoadFailed = true;
                        Debug.LogError("Run checkpoint restore failed; original save preserved: " + e);
                    }
                }
            }
            finally
            {
                restoringWorld = false;
                if (restoreRuntime)
                {
                    RunCheckpoint.IsRestoring = false;
                    if (pauseCoordinator) pauseCoordinator.SyncCheckpointPause();
                    else if (Mathf.Approximately(Time.timeScale, 0f)) Time.timeScale = fallbackScale;
                }
            }
            nextTimedRetry=Time.time+1;
            if (!worldLoadFailed && terrain && terrain.MigratesLegacyRun)
            {
                terrain.SaveSeed();
                SaveWorld();
                GameSave.Save();
            }
            if(worldLoadFailed)Debug.LogWarning("Some saved monsters could not be restored; original checkpoint is preserved.");
            ForecastChanged?.Invoke();
        }

        private static void ResolvePortalRecord(SavedMonster record, RunCheckpoint.State runtime,
            WorldNavigationGrid navigation, CharacterController motor)
        {
            if (!navigation || !motor) return;
            float radius = motor.radius * Mathf.Max(Mathf.Abs(record.scale.x), Mathf.Abs(record.scale.z));
            float height = motor.height * Mathf.Abs(record.scale.y);
            Vector3 position = navigation.ResolvePortalSpawn(record.position, radius, height);
            if (position == record.position) return;
            record.position = position;
            RunCheckpoint.MoveSavedActor(runtime, record.id, position);
        }
    }
}



