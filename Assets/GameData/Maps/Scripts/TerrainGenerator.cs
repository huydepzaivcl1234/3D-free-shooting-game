using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    internal interface IProceduralArena
    {
        int Seed { get; }
        int MaxTier { get; }
        Vector3 Center { get; }
        bool HasLayout { get; }
        event Action OnMapGenerated;
        bool TrySample(Vector3 point, out Vector3 surface, out Vector3 normal);
        bool TryGetFlatSupport(Vector3 point, float clearance, out Vector3 surface, out Vector3 normal);
        bool TryGetFlatSpot(int minTier, int maxTier, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 position, out Vector3 normal);
        bool TryGetDecorationSpot(System.Random random, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 point);
    }

    [DefaultExecutionOrder(-110)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Terrain), typeof(TerrainCollider))]
    public sealed partial class TerrainGenerator : MonoBehaviour, IProceduralArena
    {
        private const int HeightResolution = 513;
        private const int PaintResolution = 512;
        private const int DefaultLootCount = 10;
        private const int MapSize = 10;
        [SerializeField] private float fallRecoveryHeight = -12f;
        private static readonly Vector2Int[] Directions =
            { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        [SerializeField] private int seed = 2026;
        [SerializeField] private bool randomizeNewRun = true;
        [SerializeField, Min(80f)] private float worldSize = 160f;
        [SerializeField, Min(1f)] private float maxHeight = 28f;
        [SerializeField, Range(.2f, 3f)] private float hilliness = 1.45f;
        [SerializeField, Range(2, 7)] private int terraceSteps = 5;
        [SerializeField, Range(.5f, .98f)] private float cliffSharpness = .86f;
        [SerializeField, Min(.001f)] private float noiseScale = .024f;
        [SerializeField, Min(3)] private int rampHalfWidth = 6;
        [SerializeField, Min(6)] private int rampHalfLength = 10;
        [SerializeField] private TerrainLayer[] layers;
        [SerializeField] private WorldNavigationGrid navigation;
        [SerializeField] private WorldBorder border;
        [SerializeField] private Transform player;
        [SerializeField, HideInInspector] private Vector3[] lootSpawnPoints = Array.Empty<Vector3>();

        private TerrainData runtimeData;
        private TerrainCollider surfaceCollider;
        private readonly List<Vector3> flatSpots = new();
        private readonly List<Vector3> occupied = new();
        private readonly List<(Vector3 point, float spacing)> decorationSpots = new();

        internal void ClearDecorationReservations() => decorationSpots.Clear();
        private float tierHeight;
        private int query;
        private string SeedKey => "Survivor.UnityTerrain.TileSeed/" + gameObject.scene.path;

        public int Seed => seed;
        public int MaxTier => terraceSteps - 1;
        public Vector3 Center => SampleAtCenter();
        public bool HasLayout { get; private set; }
        internal bool MigratesLegacyRun { get; private set; }
        public IReadOnlyList<Vector3> LootSpawnPoints => lootSpawnPoints;
        public event Action OnMapGenerated;

        private void Start()
        {
            if (HasLayout) return;
            var zone = FindAnyObjectByType<MonsterSpawnZone>();
            bool resume = zone && GameSave.HasKey(zone.WorldSaveKey);
            string previousKey = "Survivor.TerracedArena.Seed/" + gameObject.scene.path + "/Terraced Megabonk Arena";
            MigratesLegacyRun = resume && !GameSave.HasKey(SeedKey);
            int nextSeed = resume
                ? GameSave.GetInt(SeedKey, GameSave.GetInt("Survivor.UnityTerrain.Seed/" + gameObject.scene.path, GameSave.GetInt(previousKey, seed)))
                : randomizeNewRun ? Guid.NewGuid().GetHashCode() : seed;
            Generate(nextSeed);
            if (HasLayout && !MigratesLegacyRun) GameSave.SetInt(SeedKey, seed);
        }

        internal void SaveSeed() => GameSave.SetInt(SeedKey, seed);

        internal Vector3 RegroundActor(Vector3 point)
        {
            if (TrySample(point, out var surface, out var normal) && normal.y >= .8f) return surface;
            const float step = 1.5f;
            for (int ring = 1; ring <= 6; ring++)
            for (int side = 0; side < 8; side++)
            {
                float angle = side * Mathf.PI * .25f;
                var candidate = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * step);
                if (TryGetFlatSupport(candidate, .7f, out surface, out normal)) return surface;
            }
            return Center;
        }

        public void Generate(int newSeed)
        {
            if (!UsesBlocks && (layers == null || layers.Length != 3 || Array.Exists(layers, layer => !layer)))
            {
                Debug.LogError("Terrain Generator requires grass, rock and dirt Terrain Layers.", this);
                return;
            }

            int previousSeed = seed;
            seed = newSeed;
            tierHeight = maxHeight / MaxTier;
            int cells = Mathf.RoundToInt(worldSize);
            var random = new System.Random(seed);
            float[,] heights = null;
            float[,] dirt = null;
            int[,] tiers = null, slopes = null;
            for (int attempt = 0; attempt < 36; attempt++)
            {
                if (!GrowTiles(random, out tiers, out slopes)) continue;
                heights = new float[HeightResolution, HeightResolution];
                dirt = new float[HeightResolution, HeightResolution];
                RasterizeTiles(tiers, slopes, heights, dirt);
                if (PrepareSpawns(heights, dirt, cells, random)) break;
                heights = null;
            }
            if (heights == null)
            {
                Debug.LogError($"Terrain seed {seed} could not connect all terraces. Review the height and ramp settings.", this);
                seed = previousSeed;
                return;
            }

            var terrain = GetComponent<Terrain>();
            terrain.heightmapPixelError = 15;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            transform.position = new Vector3(-worldSize * .5f, 0f, -worldSize * .5f);
            surfaceCollider = GetComponent<TerrainCollider>();
            terrain.enabled = !UsesBlocks;
            surfaceCollider.enabled = !UsesBlocks;
            if (UsesBlocks) BuildBlocks(tiers, slopes);
            else
            {
                ClearBlocks();
                if (Application.isPlaying && !runtimeData)
                    runtimeData = Instantiate(terrain.terrainData);
                var data = Application.isPlaying ? runtimeData : terrain.terrainData;
                data.heightmapResolution = HeightResolution;
                data.alphamapResolution = PaintResolution;
                data.size = new Vector3(worldSize, maxHeight, worldSize);
                terrain.terrainData = data;
                data.SetHeights(0, 0, heights);
                data.terrainLayers = layers;
                surfaceCollider.terrainData = null;
                surfaceCollider.terrainData = data;
                Paint(data, dirt);
                terrain.Flush();
            }
            if (border)
            {
                border.enabled = !UsesBlocks;
                if (border.enabled)
                    border.SetSize(new Vector3(worldSize, Mathf.Max(border.Size.y, maxHeight + 8f), worldSize));
            }
            Physics.SyncTransforms();
            HasLayout = true;
            query = 0;
            occupied.Clear();
            ClearDecorationReservations();
            occupied.AddRange(lootSpawnPoints);
            if (player)
            {
                var spawn = Center;
                if (player.TryGetComponent<CapsuleCollider>(out var capsule) && capsule.enabled)
                    spawn.y += player.position.y - capsule.bounds.min.y;
                PlacePlayer(spawn + Vector3.up * .01f);
            }

            Physics.SyncTransforms();
            if (player && player.TryGetComponent<GameData.Player.Script.PlayerController1>(out var motor))
                motor.ResetMotion();
            var grid = navigation ? navigation : WorldNavigationGrid.Instance;
            if (grid) grid.ConfigureArea(Vector3.zero, Vector2.one * worldSize);
            OnMapGenerated?.Invoke();
            Physics.SyncTransforms();
            if (grid)
            {
                if (Application.isPlaying) grid.RequestFullRebake();
            }
        }

        internal bool TryGetLootSpawn(int index, out Vector3 point)
        {
            point = default;
            if ((uint)index >= lootSpawnPoints.Length) return false;
            return TryGetFlatSupport(lootSpawnPoints[index], .7f, out point, out _);
        }

        private bool GrowTiles(System.Random random, out int[,] tiers, out int[,] slopes)
        {
            var levels = new int[MapSize, MapSize];
            var ramps = new int[MapSize, MapSize];
            tiers = levels;
            slopes = ramps;
            for (int z = 0; z < MapSize; z++)
            for (int x = 0; x < MapSize; x++)
            {
                levels[z, x] = -1;
                ramps[z, x] = -1;
            }

            var current = new Vector2Int(MapSize / 2 - 1, MapSize / 2 - 1);
            for (int z = current.y; z <= current.y + 1; z++)
            for (int x = current.x; x <= current.x + 1; x++) levels[z, x] = 0;

            var available = new List<int>(4);
            int lockedDirection = -1;
            int filled = 4;
            float tileSize = worldSize / MapSize;
            float climbChance = Mathf.Clamp01(hilliness / 3f) * .5f;
            while (filled < MapSize * MapSize)
            {
                bool landing = lockedDirection >= 0;
                if (!landing)
                {
                    FindDirections(current);
                    for (int z = 0; z < MapSize && available.Count == 0; z++)
                    for (int x = 0; x < MapSize && available.Count == 0; x++)
                    {
                        if (levels[z, x] < 0 || ramps[z, x] >= 0) continue;
                        current = new Vector2Int(x, z);
                        FindDirections(current);
                    }
                    if (available.Count == 0) return false;
                }

                int direction = landing ? lockedDirection : available[random.Next(available.Count)];
                lockedDirection = -1;
                var next = current + Directions[direction];
                int level = levels[current.y, current.x];
                bool canRise = !landing && level < MaxTier && tierHeight / tileSize < .46f &&
                    Free(next + Directions[direction]);
                if (canRise && random.NextDouble() < climbChance)
                {
                    level++;
                    ramps[next.y, next.x] = direction;
                    lockedDirection = direction;
                }
                levels[next.y, next.x] = level;
                current = next;
                filled++;
            }
            return true;

            bool Free(Vector2Int p) =>
                (uint)p.x < MapSize && (uint)p.y < MapSize && levels[p.y, p.x] < 0;

            void FindDirections(Vector2Int p)
            {
                available.Clear();
                for (int i = 0; i < Directions.Length; i++)
                    if (Free(p + Directions[i])) available.Add(i);
            }
        }

        private void RasterizeTiles(int[,] tiers, int[,] slopes, float[,] heights, float[,] dirt)
        {
            int last = HeightResolution - 1;
            float tileSize = worldSize / MapSize;
            float sampleStep = worldSize / last;
            float edgeBlend = Mathf.Lerp(sampleStep * 1.45f, sampleStep * .45f, cliffSharpness);
            float halfLength = UsesBlocks ? tileSize * .5f :
                Mathf.Min(tileSize * .5f, Mathf.Max(rampHalfLength, tierHeight / .90f));
            for (int z = 0; z <= last; z++)
            for (int x = 0; x <= last; x++)
            {
                var point = new Vector2(x, z) * sampleStep;
                float noise = Mathf.PerlinNoise(point.x * noiseScale + 21f, point.y * noiseScale + 52f);
                float contrast = Mathf.InverseLerp(.2f, .8f, noise);
                float offset = (Mathf.Pow(contrast, 1f / hilliness) - .5f) * sampleStep * .25f;
                float h = 0f, earth = 0f;
                int samples = cliffSharpness >= .85f ? 1 : 4;
                for (int i = 0; i < samples; i++)
                {
                    Vector2 jitter = samples == 1 ? Vector2.zero :
                        new Vector2((i & 1) == 0 ? -edgeBlend : edgeBlend, (i & 2) == 0 ? -edgeBlend : edgeBlend);
                    h += HeightAt(point + jitter, offset, out float dirtWeight);
                    earth += dirtWeight;
                }
                heights[z, x] = h / (samples * MaxTier);
                dirt[z, x] = earth / samples;
            }

            float HeightAt(Vector2 p, float offset, out float earth)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt((p.x + offset) / tileSize), 0, MapSize - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((p.y - offset) / tileSize), 0, MapSize - 1);
                float height = tiers[z, x];
                int slope = slopes[z, x];
                earth = 0f;
                if (slope < 0) return height;

                Vector2 relative = p - new Vector2(x + .5f, z + .5f) * tileSize;
                Vector2 direction = Directions[slope];
                float longitudinal = Vector2.Dot(relative, direction);
                float lateral = Mathf.Abs(relative.x * direction.y - relative.y * direction.x);
                float t = Mathf.InverseLerp(-halfLength, halfLength, longitudinal);
                earth = 1f - Mathf.InverseLerp(rampHalfWidth, tileSize * .5f, lateral);
                return height - 1f + t;
            }
        }

        private bool PrepareSpawns(float[,] heights, float[,] dirt, int size, System.Random random)
        {
            flatSpots.Clear();
            var reachable = new bool[size * size];
            var queue = new Queue<Vector2Int>();
            var start = new Vector2Int(size / 2, size / 2);
            reachable[start.y * size + start.x] = true;
            queue.Enqueue(start);
            float step = worldSize / size;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                float height = Sample(heights, ToLocal(current)) * maxHeight;
                foreach (var direction in Directions)
                {
                    var next = current + direction;
                    if ((uint)next.x >= size || (uint)next.y >= size) continue;
                    int index = next.y * size + next.x;
                    if (reachable[index] || Mathf.Abs(Sample(heights, ToLocal(next)) * maxHeight - height) > .47f) continue;
                    reachable[index] = true;
                    queue.Enqueue(next);
                }
            }

            var byTier = new List<Vector3>[terraceSteps];
            for (int i = 0; i < byTier.Length; i++) byTier[i] = new List<Vector3>();
            for (int z = 2; z < size - 2; z += 2)
            for (int x = 2; x < size - 2; x += 2)
            {
                if (!reachable[z * size + x]) continue;
                Vector2 p = ToLocal(new Vector2Int(x, z));
                float y = Sample(heights, p) * maxHeight;
                if (Sample(dirt, p) > .02f || !Flat(p, y)) continue;
                var spot = new Vector3(p.x - worldSize * .5f, y, p.y - worldSize * .5f);
                if (new Vector2(spot.x, spot.z).sqrMagnitude < 36f) continue;
                flatSpots.Add(spot);
                byTier[Mathf.Clamp(Mathf.RoundToInt(y / tierHeight), 0, MaxTier)].Add(spot);
            }

            var points = new List<Vector3>(DefaultLootCount);
            for (int i = 0; i < DefaultLootCount; i++)
            {
                var tier = byTier[i * terraceSteps / DefaultLootCount];
                if (tier.Count == 0) return false;
                int first = random.Next(tier.Count);
                for (int j = 0; j < tier.Count; j++)
                {
                    var point = tier[(first + j) % tier.Count];
                    if (!IsSeparated(points, point, 8f)) continue;
                    points.Add(point);
                    break;
                }
                if (points.Count != i + 1) return false;
            }
            // Keep terrain seed selection independent of the configured number of lootboxes.
            var lootRule = GetComponent<MapFeaturePopulator>()?.LootRule;
            int requested = lootRule ? Mathf.Clamp(lootRule.spawnCount, 0, 50) : DefaultLootCount;
            if (requested < points.Count)
            {
                var distributed = new List<Vector3>(requested);
                for (int i = 0; i < requested; i++)
                    distributed.Add(points[requested == 1 ? 0 : Mathf.RoundToInt(i * (points.Count - 1f) / (requested - 1))]);
                points = distributed;
            }
            else if (requested > points.Count)
            {
                int firstExtra = random.Next(flatSpots.Count);
                for (int i = 0; i < flatSpots.Count && points.Count < requested; i++)
                {
                    var point = flatSpots[(firstExtra + i) % flatSpots.Count];
                    if (IsSeparated(points, point, 8f)) points.Add(point);
                }
            }
            lootSpawnPoints = points.ToArray();
            return true;

            Vector2 ToLocal(Vector2Int cell) => ((Vector2)cell + Vector2.one * .5f) * step;
            bool Flat(Vector2 p, float y)
            {
                foreach (var direction in Directions)
                    if (Mathf.Abs(Sample(heights, p + (Vector2)direction * 1.4f) * maxHeight - y) > .12f)
                        return false;
                return Mathf.Abs(y - Mathf.Round(y / tierHeight) * tierHeight) <= .16f;
            }
        }

        private float Sample(float[,] values, Vector2 point)
        {
            int last = values.GetLength(0) - 1;
            var p = point * (last / worldSize);
            p.x = Mathf.Clamp(p.x, 0, last);
            p.y = Mathf.Clamp(p.y, 0, last);
            int x = Mathf.Min(Mathf.FloorToInt(p.x), last - 1), z = Mathf.Min(Mathf.FloorToInt(p.y), last - 1);
            return Mathf.Lerp(Mathf.Lerp(values[z, x], values[z, x + 1], p.x - x),
                Mathf.Lerp(values[z + 1, x], values[z + 1, x + 1], p.x - x), p.y - z);
        }

        private void Paint(TerrainData data, float[,] dirt)
        {
            var weights = new float[PaintResolution, PaintResolution, 3];
            for (int z = 0; z < PaintResolution; z++)
            for (int x = 0; x < PaintResolution; x++)
            {
                float u = (x + .5f) / PaintResolution, v = (z + .5f) / PaintResolution;
                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(29f, 53f, data.GetSteepness(u, v)));
                float earth = Sample(dirt, new Vector2(u, v) * worldSize) * (1f - rock);
                weights[z, x, 0] = 1f - rock - earth;
                weights[z, x, 1] = rock;
                weights[z, x, 2] = earth;
            }
            data.SetAlphamaps(0, 0, weights);
        }

        private Vector3 SampleAtCenter()
        {
            if (blockTiers != null && SampleBlock(Vector3.zero, out var center, out _)) return center;
            return new Vector3(0f, GetComponent<Terrain>().terrainData.GetInterpolatedHeight(.5f, .5f), 0f);
        }

        private void FixedUpdate()
        {
            if (!UsesBlocks || !HasLayout || !player || player.position.y >= fallRecoveryHeight) return;
            if (player.TryGetComponent<CharacterHealth>(out var health) && health.Health <= 0f) return;
            PlacePlayer(Center + Vector3.up * 2f);
            if (player.TryGetComponent<GameData.Player.Script.PlayerController1>(out var controller))
                controller.ResetMotion();
        }

        private void PlacePlayer(Vector3 position)
        {
            var motor = player.GetComponent<CharacterController>();
            bool enabled = motor && motor.enabled;
            if (enabled) motor.enabled = false;
            player.position = position;
            if (player.TryGetComponent<Rigidbody>(out var body))
            {
                body.position = position;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            if (enabled) motor.enabled = true;
        }

        public bool TrySample(Vector3 point, out Vector3 surface, out Vector3 normal)
        {
            surface = point;
            normal = Vector3.up;
            if (UsesBlocks && blockTiers != null) return SampleBlock(point, out surface, out normal);
            if (!HasLayout || !surfaceCollider ||
                !surfaceCollider.Raycast(new Ray(new Vector3(point.x, maxHeight + 5f, point.z), Vector3.down),
                    out var hit, maxHeight + 10f)) return false;
            surface = hit.point;
            normal = hit.normal;
            return true;
        }

        public bool TryGetFlatSupport(Vector3 point, float clearance, out Vector3 surface, out Vector3 normal)
        {
            if (!TrySample(point, out surface, out normal) || normal.y < .985f) return false;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                Vector3 test = surface + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * clearance;
                if (!TrySample(test, out var edge, out var edgeNormal) ||
                    Mathf.Abs(surface.y - edge.y) > .18f || edgeNormal.y < .985f) return false;
            }
            return true;
        }

        public bool TryGetFlatSpot(int minTier, int maxTier, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (flatSpots.Count == 0) return false;
            int start = new System.Random(unchecked(seed * 397 ^ ++query)).Next(flatSpots.Count);
            for (int i = 0; i < flatSpots.Count; i++)
            {
                var point = flatSpots[(start + i) % flatSpots.Count];
                int tier = Mathf.RoundToInt(point.y / tierHeight);
                if (tier < minTier || tier > maxTier || !IsSeparated(occupied, point, separation) ||
                    !SeparatedFromDecoration(point, clearance) ||
                    !TryGetFlatSupport(point, clearance, out var surface, out var up) ||
                    (accepts != null && !accepts(surface))) continue;
                position = surface;
                normal = up;
                occupied.Add(surface);
                return true;
            }
            return false;
        }

        public bool TryGetDecorationSpot(System.Random random, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 point)
        {
            point = default;
            if (flatSpots.Count == 0) return false;
            for (int i = 0; i < 64; i++)
            {
                var spot = flatSpots[random.Next(flatSpots.Count)];
                if (!IsSeparated(occupied, spot, Mathf.Max(clearance, separation)) ||
                    !SeparatedFromDecoration(spot, Mathf.Max(clearance, separation)) ||
                    (WorldNavigationGrid.Instance && WorldNavigationGrid.Instance.IsPortalSpawnExcluded(spot, clearance)) ||
                    !TryGetFlatSupport(spot, clearance, out point, out _) ||
                    (accepts != null && !accepts(point))) continue;
                decorationSpots.Add((point, Mathf.Max(clearance, separation)));
                return true;
            }
            return false;
        }

        private bool SeparatedFromDecoration(Vector3 point, float separation)
        {
            foreach (var decoration in decorationSpots)
            {
                float distance = Mathf.Max(decoration.spacing, separation);
                float x = decoration.point.x - point.x, z = decoration.point.z - point.z;
                if (x * x + z * z < distance * distance) return false;
            }
            return true;
        }

        private static bool IsSeparated(List<Vector3> points, Vector3 point, float distance)
        {
            foreach (var existing in points)
            {
                float x = existing.x - point.x, z = existing.z - point.z;
                if (x * x + z * z < distance * distance) return false;
            }
            return true;
        }

        private void OnDestroy()
        {
            ReleaseBlockMeshes();
            if (runtimeData) Destroy(runtimeData);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, .71f, .18f);
            foreach (var point in lootSpawnPoints)
            {
                Gizmos.DrawWireSphere(point + Vector3.up * .5f, .55f);
                Gizmos.DrawLine(point, point + Vector3.up * 1.7f);
            }
        }
    }
}
