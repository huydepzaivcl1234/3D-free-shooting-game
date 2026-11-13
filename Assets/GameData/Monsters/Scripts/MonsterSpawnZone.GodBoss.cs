using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        [Header("God Boss encounter")]
        [SerializeField] private GodBossData godBossData;
        private GodBossController godBoss;
        private float godRetry;
        private readonly List<MushroomMonster> godSummons = new();
        private const string GodAppearanceKey = "World.GodBoss.Appearance.v2";
        [SerializeField, Min(-1), Tooltip("-1 uses this world's count from Monster Spawn Settings. 0 disables shrine placement on this map.")]
        private int bossShrineCountOverride = -1;

        [System.Serializable]
        private sealed class GodShrineState
        {
            public Vector3 point;
            public bool used;
            [System.NonSerialized] public GameObject instance;
        }

        private readonly List<GodShrineState> godShrines = new();
        public GodBossController ActiveGodBoss => godBoss;
        private int BossShrineCount => bossShrineCountOverride >= 0 ? bossShrineCountOverride :
            spawnSettings ? spawnSettings.BossShrinesFor(spawnWorld) : 1;

        private GodShrineState FindNearbyShrine()
        {
            if (!godBossData || !player || player.Health <= 0) return null;
            GodShrineState nearest = null;
            float best = godBossData.shrineInteractionRange * godBossData.shrineInteractionRange;
            foreach (var shrine in godShrines)
            {
                if (shrine.used || !shrine.instance) continue;
                float distance = (player.transform.position - shrine.point).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                nearest = shrine;
            }
            return nearest;
        }

        private void TickGodEncounter()
        {
            if (!PlayerHuntMode || !godBossData || !godBossData.prefab || Time.deltaTime <= 0) return;
            if (Time.time >= godRetry)
            {
                godRetry = Time.time + 1;
                int count = BossShrineCount;
                for (int i = 0; i < godShrines.Count && i < count; i++)
                {
                    var saved = godShrines[i];
                    if (!saved.used && !saved.instance) CreateGodShrine(saved);
                }
                int attempts = 16;
                while (godShrines.Count < count && attempts-- > 0)
                {
                    float distance = Random.Range(godBossData.shrineMinDistance, Mathf.Max(godBossData.shrineMinDistance, godBossData.shrineMaxDistance));
                    var offset = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * distance;
                    if (!TryFindGround(player.transform.position + offset, out var ground)) continue;
                    if (Physics.CheckSphere(ground.point + Vector3.up, .55f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (godShrines.Exists(shrine => (shrine.point - ground.point).sqrMagnitude < 9f)) continue;
                    var shrine = new GodShrineState { point = ground.point };
                    godShrines.Add(shrine);
                    CreateGodShrine(shrine);
                }
            }

            if (!godBoss && !(uiCoordinator && uiCoordinator.BlocksGameplay) &&
                UnityEngine.InputSystem.Keyboard.current?.eKey.wasPressedThisFrame == true)
                TrySummonGod(FindNearbyShrine());
        }

        private void CreateGodShrine(GodShrineState shrine)
        {
            GameObject instance;
            if (godBossData.shrinePrefab)
            {
                instance = Instantiate(godBossData.shrinePrefab, shrine.point, Quaternion.identity, transform);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                instance.GetComponent<Collider>().enabled = false;
                instance.transform.SetParent(transform, true);
                instance.transform.position = shrine.point + Vector3.up * 1.35f;
                instance.transform.localScale = new Vector3(1.05f, 1.35f, 1.05f);
                var color = new MaterialPropertyBlock();
                color.SetColor("_BaseColor", new Color(.75f, .28f, .95f));
                instance.GetComponent<Renderer>().SetPropertyBlock(color);
            }
            instance.name = "God Boss Shrine";
            shrine.instance = instance;
        }

        private bool TrySummonGod(GodShrineState shrine)
        {
            if (shrine == null || shrine.used || godBoss) return false;

            var motor = godBossData.prefab.GetComponent<CharacterController>();
            if (!motor) return false;
            float radius = motor.radius;
            float height = Mathf.Max(radius * 2, godBossData.height);
            for (int i = 0; i < 16; i++)
            {
                Vector3 offset = Quaternion.Euler(0, Random.Range(0,360f), 0) * Vector3.forward * godBossData.spawnDistance;
                if (!TryFindGround(shrine.point + offset, out var ground)) continue;
                if (Physics.CheckCapsule(ground.point + Vector3.up * (radius + .15f),
                    ground.point + Vector3.up * Mathf.Max(radius + .15f, height - radius), radius * .95f,
                    ~LayerMask.GetMask("Ragdoll"), QueryTriggerInteraction.Ignore)) continue;

                int appearance = Mathf.Max(1, GameSave.GetInt(GodAppearanceKey) + 1);
                godBoss = Instantiate(godBossData.prefab, ground.point + Vector3.up * .1f, Quaternion.identity, transform);
                godBoss.Initialize(this, player, SpawnDayNumber, appearance, godBossData.ShrineMultiplier(spawnElapsedSeconds));
                godBoss.Defeated += OnGodDefeated;
                shrine.used = true;
                shrine.instance.SetActive(false);
                GameSave.SetInt(GodAppearanceKey, appearance);
                SaveWorld();
                GameSave.Save();
                GameData.Player.Script.SurvivorMonsterDiscovery.Announce(godBoss, godBossData.prefab, "God Boss", true);
                ForecastChanged?.Invoke();
                return true;
            }
            return false;
        }
        private void OnGodDefeated(GodBossController boss)
        {
            boss.Defeated -= OnGodDefeated;
            if(godBossData&&godBossData.lootAndRewards){
                var loot=godBossData.lootAndRewards;
                float xpMultiplier = MonsterLootSettings.BossExperience(loot.boss.experienceMultiplier) * boss.ExperienceMultiplier;
                MonsterKillCredit.Notify(boss.Health.LastDamageSource,loot,true,MonsterLootSettings.RewardsFor(loot).experience*xpMultiplier);
                var cards=FindAnyObjectByType<MiningCardSystem>();if(cards)cards.TryDrop(boss.transform.position,true,loot);
                GrantRewards(loot,boss.transform.position,MonsterLootSettings.BossGold(loot.boss.goldMultiplier),xpMultiplier,boss.AppearanceNumber);
            }
            NotifyDefeated(boss.transform.position);
            godBoss = null; ForecastChanged?.Invoke();
        }
        public void SummonGodMinions(GodBossController boss, float missingHealth)
        {
            if (DebugSpawningPaused) return;
            if (!boss || boss != godBoss || !godBossData) return;
            godSummons.RemoveAll(m => !m || !m.Health || m.Health.Health <= 0 || m.IsDespawning);
            var entries = new List<MonsterSpawnEntry>(monsters);
            if (additionalRoster)
            { entries.Add(additionalRoster.nightThief); entries.Add(additionalRoster.nightSkullclaw); }
            var seen = new HashSet<MushroomMonster>();
            foreach (var entry in entries)
            {
                if (entry == null || !entry.prefab || !seen.Add(entry.prefab)) continue;
                for (int variant = 0; variant < (godBossData.summonBossVariants ? 2 : 1); variant++)
                {
                    if (godSummons.Count >= godBossData.maxLivingSummons || LivingCountOnField() >= LivingMonsterLimit) return;
                    if (variant == 1 && (entry.prefab.RewardData == null || entry.prefab.RewardData.boss == null)) continue;
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        Vector3 point = boss.transform.position + Quaternion.Euler(0,Random.Range(0,360),0) * Vector3.forward * Random.Range(Mathf.Max(0,godBossData.summonMinimumDistance),Mathf.Max(godBossData.summonMinimumDistance,godBossData.summonMaximumDistance));
                        if (!TryFindGround(point, out var hit)) continue;
                        if ((player.transform.position-hit.point).sqrMagnitude < godBossData.summonMinimumPlayerDistance * godBossData.summonMinimumPlayerDistance) continue;
                        if (Physics.CheckSphere(hit.point + Vector3.up * 1.2f, .8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        var minion = Instantiate(entry.prefab, hit.point + Vector3.up * .05f, Quaternion.identity, transform);
                        minion.Initialize(this, player, variant == 1, true);
                        GameData.Player.Script.SurvivorMonsterDiscovery.Announce(minion,entry,variant==1);
                        minion.ApplyEncounterModifiers(1, Mathf.Lerp(1, godBossData.summonDamageAtZeroBossHP, missingHealth),
                            Mathf.Lerp(1, godBossData.summonHealthAtZeroBossHP, missingHealth));
                        alive.Add(minion); liveSpecies[minion] = entry; godSummons.Add(minion);
                        break;
                    }
                }
            }
            ForecastChanged?.Invoke();
        }
        private void ResetGodEncounter()
        {
            if (godBoss) { godBoss.Defeated -= OnGodDefeated; godBoss.gameObject.SetActive(false); Destroy(godBoss.gameObject); }
            foreach (var shrine in godShrines)
            {
                if (!shrine.instance) continue;
                shrine.instance.SetActive(false);
                Destroy(shrine.instance);
            }
            godShrines.Clear();
            godBoss = null; godRetry = 0; godSummons.Clear();
        }
    }
}
