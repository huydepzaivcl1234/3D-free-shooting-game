using System;
using System.Collections.Generic;
using MiningSimulator.Ores;
using UnityEngine;

namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    public sealed class SurvivorAbilityLoot : MonoBehaviour
    {
        private const string SaveKey = "Survivor.AbilityLoot.v1";
        private const int MaxBoxes = 50;
        private const float Clearance = .75f;
        private const float Spacing = MapFeaturePopulator.LootSpacing;
        private const int PathChecksPerSearch = 4;

        [Serializable]
        private sealed class Record
        {
            public Vector3 position;
            public float yaw;
            public bool shiny;
            public bool collected;
            public bool reward;
        }

        [Serializable]
        private sealed class Progress
        {
            public int version = 1;
            public int seed;
            public int count;
            public List<Record> boxes = new();
        }

        private SurvivorDailyLoot woodLoot;
        private MiningCardSystem cards;
        private MapFeaturePopulator features;
        private Progress progress;
        private readonly List<Vector3> route = new();
        private readonly List<Vector3> lootPositions = new();
        private readonly List<SurvivorLootBox> live = new();
        internal IReadOnlyList<SurvivorLootBox> CurrentBoxes => live;
        private int nextIndex, failedPlacements, rewardBoxCount;
        private bool loadBlocked;

        public static void Attach(SurvivorDailyLoot owner)
        {
            if (!owner || !owner.data || !owner.data.abilityBoxPrefab ||
                owner.TryGetComponent<SurvivorAbilityLoot>(out _)) return;

            var loot = owner.gameObject.AddComponent<SurvivorAbilityLoot>();
            loot.woodLoot = owner;
            loot.cards = FindAnyObjectByType<MiningCardSystem>();
            loot.features = FindAnyObjectByType<MapFeaturePopulator>();
        }

        private void LateUpdate()
        {
            if (!woodLoot || !woodLoot.isActiveAndEnabled || !cards || !cards.Player ||
                !features || !features.PopulationReady || features.Arena is not { HasLayout: true } arena ||
                RunCheckpoint.IsRestoring || Time.timeScale <= 0f) return;

            var grid = WorldNavigationGrid.Instance;
            if (!grid || !grid.HasBaked || GameSave.SuppressWrites || !GameSave.Store.Writable || loadBlocked) return;

            if (progress == null || progress.seed != arena.Seed)
                LoadLayout(arena.Seed);

            if (progress == null) return;
            if (nextIndex < progress.boxes.Count)
            {
                var record = progress.boxes[nextIndex];
                if (!record.collected)
                {
                    features.CaptureLootPositions(lootPositions);
                    int densityCount = MapFeaturePopulator.CountSpawnDensityItems(lootPositions, arena.Center);
                    if (!record.reward && !FitsDensity(record.position, arena.Center, densityCount) &&
                        TryPlace(arena, grid, nextIndex, densityCount, out var replacement))
                    {
                        record.position = replacement;
                        Save();
                    }
                }
                Spawn(nextIndex++);
            }
            else if (MapBoxCount < progress.count && failedPlacements < 3)
            {
                features.CaptureLootPositions(lootPositions);
                int densityCount = MapFeaturePopulator.CountSpawnDensityItems(lootPositions, arena.Center);
                if (TryPlace(arena, grid, MapBoxCount, densityCount, out var position))
                {
                    var random = new System.Random(unchecked(arena.Seed * 397 ^ nextIndex ^ 0x2B10E));
                    progress.boxes.Add(new Record
                    {
                        position = position,
                        yaw = (float)random.NextDouble() * 360,
                        shiny = random.NextDouble() * 100 < woodLoot.data.shinyChancePercent
                    });
                    Spawn(nextIndex++);
                    failedPlacements = 0;
                    Save();
                }
                else if (++failedPlacements == 3)
                    Debug.LogWarning($"Placed {progress.boxes.Count}/{progress.count} blue Ability Loot Boxes.", this);
            }

            if (!cards.CanCollect) return;
            float radiusSquared = woodLoot.data.pickupRadius * woodLoot.data.pickupRadius;
            Vector3 player = cards.Player.transform.position;
            for (int i = 0; i < live.Count; i++)
            {
                var box = live[i];
                if (!box || !box.ReadyToOpen || (box.transform.position - player).sqrMagnitude > radiusSquared)
                    continue;

                float rarity = box.Shiny ? 1f + Mathf.Max(0, woodLoot.data.shinyRarityBonusPercent) * .01f : 1f;
                if (!cards.QueueLootDraft(rarity, woodLoot)) return;
                progress.boxes[i].collected = true;
                live[i] = null;
                box.gameObject.SetActive(false);
                Destroy(box.gameObject);
                Save();
                MonsterSpawnZone.CaptureWorldsIfDue(force: true);
                return;
            }
        }

        private void LoadLayout(int seed)
        {
            foreach (var box in live)
            {
                if (!box) continue;
                box.gameObject.SetActive(false);
                Destroy(box.gameObject);
            }
            live.Clear();
            nextIndex = failedPlacements = rewardBoxCount = 0;
            string json = GameSave.GetString(SaveKey, "");
            progress = null;
            if (!string.IsNullOrEmpty(json))
            {
                try { progress = JsonUtility.FromJson<Progress>(json); }
                catch (ArgumentException) { }
                if (progress == null || progress.version != 1 || progress.count < 0 ||
                    progress.count > MaxBoxes || progress.boxes == null ||
                    progress.boxes.Exists(box => box == null || !float.IsFinite(box.position.sqrMagnitude) ||
                        !float.IsFinite(box.yaw)))
                {
                    progress = null;
                    loadBlocked = true;
                    Debug.LogWarning("Saved blue Ability Loot Boxes could not be read. Original records were preserved.", this);
                    return;
                }
                if (progress.seed == seed)
                {
                    foreach (var box in progress.boxes) if (box.reward) rewardBoxCount++;
                    if (MapBoxCount <= progress.count) return;
                    progress = null;
                    loadBlocked = true;
                    Debug.LogWarning("Saved Ability Loot Box count exceeds the map layout. Original records were preserved.", this);
                    return;
                }
            }

            progress = new Progress
            {
                seed = seed,
                count = Mathf.Clamp(woodLoot.data.startingAbilityBoxCount, 0, MaxBoxes)
            };
            Save();
        }

        private int MapBoxCount => progress.boxes.Count - rewardBoxCount;

        internal void DropReward(Vector3 position)
        {
            if (!woodLoot || !features || !features.PopulationReady ||
                features.Arena is not { HasLayout: true } arena || GameSave.SuppressWrites ||
                !GameSave.Store.Writable || loadBlocked) return;
            if (progress == null || progress.seed != arena.Seed) LoadLayout(arena.Seed);
            if (progress == null) return;

            var grid = WorldNavigationGrid.Instance;
            if (grid && grid.TryGetGroundPoint(position, out var ground)) position = ground;
            progress.boxes.Add(new Record
            {
                position = position + Vector3.up * .04f,
                yaw = UnityEngine.Random.Range(0f, 360f),
                reward = true
            });
            rewardBoxCount++;
            Save();
        }

        private void Save() => GameSave.SetString(SaveKey, JsonUtility.ToJson(progress));

        private void Spawn(int index)
        {
            live.Add(null);
            var record = progress.boxes[index];
            if (record.collected) return;

            var go = Instantiate(woodLoot.data.abilityBoxPrefab, record.position,
                Quaternion.Euler(0f, record.yaw, 0f), transform);
            if (!go.TryGetComponent(out SurvivorLootBox box)) box = go.AddComponent<SurvivorLootBox>();
            box.Configure(woodLoot, record.shiny);
            WorldActorVisibility.Attach(go);
            live[index] = box;
        }

        private bool FitsDensity(Vector3 point, Vector3 origin, int densityCount) =>
            !MapFeaturePopulator.SpawnDensityLimitReached(point, origin, densityCount) &&
            MapFeaturePopulator.HasLootClearance(lootPositions, point, Spacing);

        private bool TryPlace(IProceduralArena arena, WorldNavigationGrid grid, int index, int densityCount, out Vector3 point)
        {
            var data = woodLoot.data;
            Vector3 origin = arena.Center;
            bool near = index < data.nearBoxCount;
            bool mid = !near && index - data.nearBoxCount < data.midBoxCount;
            float min = near ? data.minimumDistance : mid ? data.midMinimumDistance : data.farMinimumDistance;
            float max = near ? data.maximumDistance : mid ? data.midMaximumDistance : data.farMaximumDistance;
            int pathChecks = 0;
            bool Fits(Vector3 p)
            {
                if (pathChecks >= PathChecksPerSearch ||
                    (cards.Player.transform.position - p).sqrMagnitude < 9f ||
                    !FitsDensity(p, origin, densityCount) || grid.IsPortalSpawnExcluded(p, Clearance) ||
                    !grid.IsCapsulePlacementClear(p, Clearance, 1.25f)) return false;
                pathChecks++;
                return grid.TryFindPath(origin, p, route);
            }

            if (arena.TryGetFlatSpot(0, arena.MaxTier, Clearance, Spacing,
                p => PlanarDistanceSquared(p, origin) >= min * min &&
                     PlanarDistanceSquared(p, origin) <= max * max && Fits(p), out point, out _))
                return true;

            pathChecks = 0;
            return arena.TryGetFlatSpot(0, arena.MaxTier, Clearance, Spacing,
                Fits, out point, out _);
        }

        private static float PlanarDistanceSquared(Vector3 a, Vector3 b)
        {
            Vector3 delta = a - b;
            return delta.x * delta.x + delta.z * delta.z;
        }
    }
}
