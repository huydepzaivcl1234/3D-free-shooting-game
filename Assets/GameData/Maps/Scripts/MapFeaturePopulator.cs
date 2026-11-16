using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class MapFeaturePopulator : MonoBehaviour
    {
        [SerializeField] private MapFeatureRule[] featureRules = Array.Empty<MapFeatureRule>();
        [SerializeField] private PhysicsMaterial obstacleMaterial;
        [Serializable]
        private sealed class Decoration
        {
            public GameObject prefab;
            [Min(0)] public int count;
            [Min(.1f)] public float clearance = 1;
            [Min(0)] public float spacing = 3;
            public Vector2 scale = new(.85f, 1.15f);
            public bool solidTrunk;
            public bool staticBoulder;
        }

        [SerializeField] private Decoration[] decorations = Array.Empty<Decoration>();
        [SerializeField] private string overtimeFeature = "BossPortalGate";

        private IProceduralArena terrain;
        internal IProceduralArena Arena => terrain;
        private MonsterSpawnZone stage;
        private Transform featureRoot;
        private StagePortalVisual stagePortal;
        private Coroutine population;
        private readonly Dictionary<string, List<GameObject>> delayed = new();
        private readonly HashSet<string> activated = new();
        private bool overtimeActivated;
        private readonly HashSet<Mesh> combinedMeshes = new();
        public bool PopulationReady { get; private set; }

        public MapFeatureRule LootRule
        {
            get
            {
                foreach (var rule in featureRules)
                    if (rule && rule.managedBySurvivorLoot && rule.prefab) return rule;
                return null;
            }
        }

        private void OnEnable()
        {
            terrain = GetComponent<IProceduralArena>();
            if (terrain == null) return;
            terrain.OnMapGenerated += SchedulePopulation;
            stage = FindAnyObjectByType<MonsterSpawnZone>();
            if (Application.isPlaying && terrain.HasLayout) SchedulePopulation();
        }

        private void OnDisable()
        {
            if (terrain != null) terrain.OnMapGenerated -= SchedulePopulation;
            if (population != null) StopCoroutine(population);
            population = null;
        }

        private void SchedulePopulation()
        {
            if (!Application.isPlaying) return;
            if (population != null) StopCoroutine(population);
            PopulationReady = false;
            overtimeActivated = false;
            activated.Clear();
            delayed.Clear();
            BuildFeatureRoot();
            PopulateDecoration();
            population = StartCoroutine(Populate());
        }

        private void BuildFeatureRoot()
        {
            RemoveGenerated(featureRoot ? featureRoot : transform.Find("Generated Map Features"));
            featureRoot = new GameObject("Generated Map Features").transform;
            featureRoot.SetParent(transform, false);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(featureRoot.gameObject, "Generate stage portal");
#endif
            PlaceStagePortal();
        }

        public void PopulatePreview()
        {
            if (Application.isPlaying) return;
            terrain ??= GetComponent<IProceduralArena>();
            if (terrain == null || !terrain.HasLayout) return;
            BuildFeatureRoot();
            PopulateDecoration();
        }

        private IEnumerator Populate()
        {
            // Wait until the collider is baked by the actual Survivor A* grid before placing features.
            yield return null;
            var grid = WorldNavigationGrid.Instance;
            while (grid && !grid.HasBaked) yield return null;
            var route = new List<Vector3>();
            var changedObstacles = new List<Collider>();
            foreach (var rule in featureRules)
            {
                if (!rule || !rule.prefab || rule.managedBySurvivorLoot || rule.reservesPortalSpawnArea) continue;
                int placed = 0;
                for (; placed < rule.spawnCount; placed++)
                {
                    if (!terrain.TryGetFlatSpot(rule.minTier, terrain is TerrainGenerator ? terrain.MaxTier : rule.maxTier,
                        rule.clearance, rule.spacing,
                        p => !grid || (grid.IsCapsulePlacementClear(p, rule.clearance, 2f) &&
                            grid.TryFindPath(terrain.Center, p, route)), out Vector3 point, out Vector3 normal)) break;

                    var anchor = new GameObject(rule.featureName);
                    anchor.transform.SetParent(featureRoot, false);
                    anchor.transform.SetPositionAndRotation(point,
                        rule.alignToSurfaceNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity);
                    bool isDelayed = rule.spawnTiming == MapFeatureTiming.DelayedEvent;
                    anchor.SetActive(!isDelayed || activated.Contains(rule.featureName));
                    var instance = Instantiate(rule.prefab, point, anchor.transform.rotation, anchor.transform);
                    int layer = GetSingleLayer(rule.obstacleLayer);
                    if (layer >= 0)
                        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                        {
                            if (collider.isTrigger) continue;
                            collider.gameObject.layer = layer;
                            if (!collider.sharedMaterial && obstacleMaterial) collider.sharedMaterial = obstacleMaterial;
                            if (collider.enabled && collider.gameObject.activeInHierarchy) changedObstacles.Add(collider);
                        }
                    if (!isDelayed) continue;
                    if (!delayed.TryGetValue(rule.featureName, out var anchors))
                        delayed.Add(rule.featureName, anchors = new List<GameObject>());
                    anchors.Add(anchor);
                }
                if (placed < rule.spawnCount)
                    Debug.LogWarning($"Placed {placed}/{rule.spawnCount} {rule.featureName}. Review its tier and clearance settings.", rule);
            }

            Physics.SyncTransforms();
            if (grid) foreach (var obstacle in changedObstacles) grid.MarkDirty(obstacle.bounds);
            PopulationReady = true;
        }

        private void PlaceStagePortal()
        {
            stagePortal = null;
            var grid = WorldNavigationGrid.Instance;
            if (grid) grid.SetPortalSpawnArea(null, 0f);
            foreach (var rule in featureRules)
            {
                if (!rule || !rule.reservesPortalSpawnArea || !rule.prefab) continue;
                float radius = rule.clearance;
                Vector3 center = terrain.Center;
                bool AwayFromPlayerStart(Vector3 point)
                {
                    Vector2 offset = new Vector2(point.x - center.x, point.z - center.z);
                    float distance = radius + 5f;
                    return offset.sqrMagnitude >= distance * distance;
                }
                if (!terrain.TryGetFlatSpot(rule.minTier, terrain.MaxTier, radius, rule.spacing,
                    AwayFromPlayerStart, out var spot, out _) &&
                    !terrain.TryGetFlatSpot(0, terrain.MaxTier, radius, radius, AwayFromPlayerStart, out spot, out _))
                {
                    Debug.LogWarning($"No flat portal area for map seed {terrain.Seed}.", this);
                    break;
                }
                var instance = Instantiate(rule.prefab, spot, Quaternion.identity, featureRoot);
                stagePortal = instance.GetComponent<StagePortalVisual>();
                if (grid) grid.SetPortalSpawnArea(instance.transform, radius);
                break;
            }
        }

        private Transform foliageRoot;

        public void PopulateDecoration()
        {
            terrain ??= GetComponent<IProceduralArena>();
            if (terrain == null) return;
            var oldFoliage = foliageRoot ? foliageRoot : transform.Find("Arena Foliage");
            var grid = WorldNavigationGrid.Instance;
            var dirtyBounds = Application.isPlaying && grid && grid.CellCount > 0 ? new List<Bounds>() : null;
            if (dirtyBounds != null && oldFoliage)
                foreach (var obstacle in oldFoliage.GetComponentsInChildren<Collider>())
                    if (obstacle.enabled && !obstacle.isTrigger) dirtyBounds.Add(obstacle.bounds);
            RemoveGenerated(oldFoliage);
            if (terrain is TerrainGenerator generator) generator.ClearDecorationReservations();
            ReleaseCombinedMeshes();

            var parent = new GameObject("Arena Foliage").transform;
            foliageRoot = parent;
            parent.SetParent(transform, false);
            var boulders = new GameObject("Static Boulders").transform;
            boulders.SetParent(parent, false);
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCreatedObjectUndo(parent.gameObject, "Generate arena foliage");
#endif
            var random = new System.Random(terrain.Seed ^ 81319);
            int pillarSlot = 0;
            CharacterHealth player = null;
            UiPanelCoordinator coordinator = null;
            GameMainMenu menu = null;
            if (Application.isPlaying)
            {
                var stats = FindAnyObjectByType<PlayerStats>(FindObjectsInactive.Include);
                if (stats) player = stats.GetComponent<CharacterHealth>();
                coordinator = FindAnyObjectByType<UiPanelCoordinator>(FindObjectsInactive.Include);
                menu = FindAnyObjectByType<GameMainMenu>(FindObjectsInactive.Include);
            }
            foreach (var decoration in decorations)
            {
                if (decoration == null || !decoration.prefab) continue;
                var pillarPrefab = decoration.prefab.GetComponent<SupportUpgradePillar>();
                var pillarSettings = pillarPrefab ? pillarPrefab.Settings : null;
                var authoredBox = decoration.prefab.GetComponent<BoxCollider>();
                float clearance = decoration.clearance;
                float spacing = decoration.spacing;
                if (pillarPrefab)
                {
                    clearance = Mathf.Max(clearance, pillarPrefab.Radius + .1f);
                    spacing = Mathf.Max(pillarSettings ? pillarSettings.minimumSpacing : spacing, pillarPrefab.Radius * 2f + .1f);
                }
                else if (authoredBox && authoredBox.enabled && !authoredBox.isTrigger)
                {
                    Vector3 extents = authoredBox.size * .5f;
                    Vector3 center = authoredBox.center;
                    float footprint = new Vector2(extents.x + Mathf.Abs(center.x), extents.z + Mathf.Abs(center.z)).magnitude;
                    footprint *= Mathf.Max(decoration.scale.x, decoration.scale.y);
                    clearance = Mathf.Max(clearance, footprint);
                    spacing = Mathf.Max(spacing, footprint * 2f);
                }
                int count = pillarSettings ? Mathf.Max(0, pillarSettings.spawnCount) : decoration.count;
                for (int i = 0; i < count; i++)
                {
                    if (!terrain.TryGetDecorationSpot(random, clearance, spacing, out var point)) break;
                    var prop = Instantiate(decoration.prefab, point,
                        Quaternion.Euler(0, (float)random.NextDouble() * 360, 0),
                        decoration.staticBoulder ? boulders : parent);
                    var variation = pillarSettings ? pillarSettings.sizeVariation : decoration.scale;
                    float scale = Mathf.Lerp(variation.x, variation.y, (float)random.NextDouble());
                    if (pillarSettings)
                    {
                        float width = Mathf.Max(.1f, pillarSettings.widthMultiplier);
                        float height = Mathf.Max(.1f, pillarSettings.heightMultiplier);
                        prop.transform.localScale = Vector3.Scale(prop.transform.localScale, new Vector3(width, height, width)) *
                            Mathf.Max(.1f, scale);
                    }
                    else prop.transform.localScale *= pillarPrefab ? scale * SupportUpgradePillar.LegacySizeMultiplier : scale;
                    PrepareDecoration(prop, decoration);
                    if (pillarPrefab)
                        prop.GetComponent<SupportUpgradePillar>().Initialize(terrain, pillarSlot++, player, stage, coordinator, menu);
                }
            }
            if (dirtyBounds != null)
            {
                Physics.SyncTransforms();
                foreach (var obstacle in parent.GetComponentsInChildren<Collider>())
                    if (obstacle.enabled && !obstacle.isTrigger) dirtyBounds.Add(obstacle.bounds);
                foreach (var bounds in dirtyBounds) grid.MarkDirty(bounds);
            }
            if (Application.isPlaying && boulders.childCount > 0)
            {
                var filters = boulders.GetComponentsInChildren<MeshFilter>();
                var originals = new HashSet<Mesh>();
                foreach (var filter in filters) originals.Add(filter.sharedMesh);
                StaticBatchingUtility.Combine(boulders.gameObject);
                foreach (var filter in filters)
                    if (filter.sharedMesh && !originals.Contains(filter.sharedMesh))
                        combinedMeshes.Add(filter.sharedMesh);
            }
        }

        private void PrepareDecoration(GameObject prop, Decoration decoration)
        {
            var authoredBox = prop.GetComponent<BoxCollider>();
            if (authoredBox && (!authoredBox.enabled || authoredBox.isTrigger)) authoredBox = null;
            foreach (var collider in prop.GetComponentsInChildren<Collider>(true))
            {
                if (collider == authoredBox) continue;
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
#if UNITY_EDITOR
                else UnityEditor.Undo.DestroyObjectImmediate(collider);
#endif
            }

            var renderers = prop.GetComponentsInChildren<MeshRenderer>(true);
            float height = 0f;
            foreach (var renderer in renderers) height = Mathf.Max(height, renderer.bounds.size.y);
            bool solid = authoredBox || decoration.staticBoulder || (height >= 1.5f && decoration.solidTrunk);
            foreach (var renderer in renderers)
                if (!solid || renderer.bounds.size.y < 1.5f)
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (!solid) return;
            prop.layer = LayerMask.NameToLayer("MapObstacle");
            Collider obstacle = authoredBox;
            if (!obstacle && decoration.staticBoulder && renderers.Length > 0)
            {
                var rock = prop.AddComponent<BoxCollider>();
                Bounds bounds = GetDecorationBounds(prop.transform, renderers);
                rock.size = bounds.size;
                rock.center = bounds.center;
                obstacle = rock;
            }
            if (!obstacle && decoration.solidTrunk)
            {
                var trunk = prop.AddComponent<CapsuleCollider>();
                trunk.radius = .24f;
                trunk.height = 2f;
                trunk.center = Vector3.up;
                obstacle = trunk;
            }
            if (obstacle && !obstacle.sharedMaterial) obstacle.sharedMaterial = obstacleMaterial;
        }

        private static Bounds GetDecorationBounds(Transform root, MeshRenderer[] renderers)
        {
            var bounds = new Bounds(root.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var renderer in renderers)
            {
                Bounds local = renderer.localBounds;
                Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    bounds.Encapsulate(toRoot.MultiplyPoint3x4(corner));
                }
            }
            return bounds;
        }

        private void RemoveGenerated(Transform root)
        {
            if (!root) return;
            root.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(root.gameObject);
#if UNITY_EDITOR
            else UnityEditor.Undo.DestroyObjectImmediate(root.gameObject);
#endif
        }

        private void OnDestroy() => ReleaseCombinedMeshes();

        private void ReleaseCombinedMeshes()
        {
            foreach (var mesh in combinedMeshes)
                if (mesh) Destroy(mesh);
            combinedMeshes.Clear();
        }

        private void Update()
        {
            if (stagePortal && stage && !RunCheckpoint.IsRestoring)
                stagePortal.SetOpen(stage.IsOvertime);
            if (!PopulationReady || overtimeActivated || !stage || !stage.IsOvertime ||
                RunCheckpoint.IsRestoring || string.IsNullOrEmpty(overtimeFeature)) return;
            ActivateFeature(overtimeFeature);
            overtimeActivated = true;
        }

        public bool ActivateFeature(string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName)) return false;
            activated.Add(featureName);
            if (!delayed.TryGetValue(featureName, out var anchors)) return false;
            bool changed = false;
            foreach (var anchor in anchors)
            {
                if (!anchor || anchor.activeSelf) continue;
                anchor.SetActive(true);
                changed = true;
            }
            if (changed && WorldNavigationGrid.Instance)
            {
                Physics.SyncTransforms();
                foreach (var anchor in anchors)
                    if (anchor)
                        foreach (var obstacle in anchor.GetComponentsInChildren<Collider>())
                            if (obstacle.enabled && !obstacle.isTrigger)
                                WorldNavigationGrid.Instance.MarkDirty(obstacle.bounds);
            }
            return true;
        }

        private static int GetSingleLayer(LayerMask layer)
        {
            uint mask = unchecked((uint)layer.value);
            if (mask == 0 || (mask & (mask - 1)) != 0) return -1;
            int index = 0;
            while ((mask >>= 1) != 0) index++;
            return index;
        }
    }
}

