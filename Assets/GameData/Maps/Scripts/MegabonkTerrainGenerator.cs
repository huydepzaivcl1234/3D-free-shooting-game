using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    [DefaultExecutionOrder(-110)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class MegabonkTerrainGenerator : MonoBehaviour, IProceduralArena
    {
        private const int Tiles = 12;
        private const int TileCells = 8;
        private const int Cells = Tiles * TileCells;
        private const int RampDepth = 5;
        private const int RampMin = 1;
        private const int RampMax = TileCells - RampMin;
        private const int MazeLayoutVersion = 3;
        private static readonly Vector2Int[] Directions =
            { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        [SerializeField] private int seed = 1206;
        [SerializeField] private bool generateOnPlay = true;
        [SerializeField] private bool randomizeNewRun = true;
        [SerializeField, Min(80)] private float worldSize = 96;
        [SerializeField, Range(1, 3)] private int maxTiers = 3;
        [SerializeField, HideInInspector] private float tierHeight = 1.8f;
        [SerializeField, HideInInspector] private float hilliness = .15f;
        [SerializeField, Range(.5f, 3.1f), InspectorName("Tier Height")] private float mazeTierHeight = 3.1f;
        [SerializeField, Range(0, 1), InspectorName("Hilliness")] private float mazeHilliness = .42f;
        [SerializeField] private PhysicsMaterial surfaceMaterial;
        [SerializeField] private WorldNavigationGrid navigation;
        [SerializeField] private GameObject[] replacedGeometry = Array.Empty<GameObject>();
        [SerializeField, HideInInspector] private Mesh previewMesh;
        [SerializeField, HideInInspector] private Mesh legacyArena1206;
        private Mesh runtimeMesh;
        private bool hasLayout;
        private float spacing;
        private float activeTierHeight;
        private readonly List<Vector3> flatSpots = new();
        private readonly List<Vector3> reserved = new();
        private int query;
        public int Seed => seed;
        int IProceduralArena.MaxTier => maxTiers;
        Vector3 IProceduralArena.Center => transform.position;
        bool IProceduralArena.HasLayout => HasLayout;
        private string SaveKey => "Survivor.TerracedArena.Seed/" + gameObject.scene.path + "/" + gameObject.name;
        public event Action OnMapGenerated;
        public event Action<int> OnTerrainGenerated;

        bool IProceduralArena.TrySample(Vector3 point, out Vector3 surface, out Vector3 normal) =>
            TrySample(point, out surface, out normal);

        bool IProceduralArena.TryGetFlatSupport(Vector3 point, float clearance, out Vector3 surface, out Vector3 normal) =>
            TryGetFlatSupport(point, clearance, out surface, out normal);

        bool IProceduralArena.TryGetFlatSpot(int minTier, int maxTier, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 point, out Vector3 normal) =>
            TryGetFlatSpot(minTier, maxTier, clearance, separation, accepts, out point, out normal);

        bool IProceduralArena.TryGetDecorationSpot(System.Random random, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 point) =>
            TryGetDecorationSpot(random, clearance, separation, accepts, out point);

        private void Start()
        {
            if (!generateOnPlay || hasLayout) return;
            var stage = FindAnyObjectByType<MonsterSpawnZone>();
            bool resuming = stage && GameSave.HasKey(stage.WorldSaveKey) && GameSave.HasKey(SaveKey);
            int runSeed = resuming ? GameSave.GetInt(SaveKey, seed)
                : randomizeNewRun ? Guid.NewGuid().GetHashCode() : seed;
            int version = resuming ? GameSave.GetInt(SaveKey + "/Version") : MazeLayoutVersion;
            if (resuming && runSeed == 1206 && legacyArena1206 && version < 2)
            {
                seed = runSeed;
                spacing = worldSize / Cells;
                activeTierHeight = tierHeight;
                InstallMesh(legacyArena1206, null);
                var points = legacyArena1206.vertices;
                var indices = legacyArena1206.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    Vector3 a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
                    if (Vector3.Cross(b - a, c - a).normalized.y >= .985f)
                        flatSpots.Add((a + b + c) / 3f);
                }
                FinishGeneration();
                return;
            }
            Generate(runSeed, version < MazeLayoutVersion ? 2 : MazeLayoutVersion);
        }

        public void GenerateNewSeed(int newSeed) => Generate(newSeed, MazeLayoutVersion);

        private void Generate(int newSeed, int version)
        {
            worldSize = Mathf.Max(96f, worldSize);
            maxTiers = Mathf.Clamp(maxTiers, 1, 3);
            activeTierHeight = version == MazeLayoutVersion
                ? Mathf.Clamp(mazeTierHeight, .5f, 3.1f) : Mathf.Clamp(tierHeight, .5f, 1.8f);
            seed = newSeed;
            query = 0;
            reserved.Clear();
            decorations.Clear();
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
            {
                Debug.LogError("Terraced arena requires the Ground layer.", this);
                return;
            }

            Mesh next = BuildMesh(version == MazeLayoutVersion);
            InstallMesh(next, next);
            if (Application.isPlaying)
            {
                GameSave.SetInt(SaveKey, seed);
                GameSave.SetInt(SaveKey + "/Version", version);
            }
            FinishGeneration();
        }

        private void InstallMesh(Mesh mesh, Mesh ownedMesh)
        {
            gameObject.layer = LayerMask.NameToLayer("Ground");
            var collider = GetComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
            collider.sharedMaterial = surfaceMaterial;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            if (runtimeMesh) DestroyMesh(runtimeMesh);
            runtimeMesh = ownedMesh;
            hasLayout = true;
        }

        private void FinishGeneration()
        {
            if (Application.isPlaying) foreach (var geometry in replacedGeometry)
                if (geometry && geometry != gameObject && !transform.IsChildOf(geometry.transform))
                    geometry.SetActive(false);

            Physics.SyncTransforms();
            var grid = navigation ? navigation : WorldNavigationGrid.Instance;
            if (Application.isPlaying && grid) grid.RequestFullRebake();
            OnTerrainGenerated?.Invoke(seed);
            OnMapGenerated?.Invoke();
        }

        public void SetPreviewMesh(Mesh asset)
        {
            var generated = runtimeMesh;
            GetComponent<MeshFilter>().sharedMesh = asset;
            GetComponent<MeshCollider>().sharedMesh = asset;
            previewMesh = asset;
            runtimeMesh = null;
            if (generated && generated != asset) DestroyMesh(generated);
        }

        private Mesh BuildMesh(bool maze)
        {
            spacing = worldSize / Cells;
            var random = new System.Random(seed);
            float noiseOffset = 16f + (float)random.NextDouble() * 360f;
            int[,] levels = new int[Tiles, Tiles];
            int[,] entrances = new int[Tiles, Tiles];
            var order = new List<Vector2Int>(Tiles * Tiles);
            for (int z = 0; z < Tiles; z++)
            for (int x = 0; x < Tiles; x++)
            {
                bool center = x >= Tiles / 2 - 1 && x <= Tiles / 2 && z >= Tiles / 2 - 1 && z <= Tiles / 2;
                levels[x, z] = center ? 0 : -1;
                entrances[x, z] = -1;
                if (center) order.Add(new Vector2Int(x, z));
            }

            var current = order[random.Next(order.Count)];
            var available = new List<int>(4);
            int legacyRoot = 0;
            int lockedDirection = -1;
            var marked = new int[Tiles, Tiles];
            var pending = new Queue<Vector2Int>(Tiles * Tiles);
            int visit = 0;
            while (order.Count < Tiles * Tiles)
            {
                bool landing = lockedDirection >= 0;
                if (!landing)
                {
                    FindDirections(current);
                    if (available.Count == 0)
                    {
                        if (!maze)
                        {
                            current = order[legacyRoot++];
                            continue;
                        }
                        for (int x = 0; x < Tiles && available.Count == 0; x++)
                        for (int z = 0; z < Tiles && available.Count == 0; z++)
                        {
                            if (levels[x, z] < 0 || entrances[x, z] >= 0) continue;
                            current = new Vector2Int(x, z);
                            FindDirections(current);
                        }
                    }
                }

                int direction = landing ? lockedDirection : available[random.Next(available.Count)];
                lockedDirection = -1;
                Vector2Int child = current + Directions[direction];
                int parentTier = levels[current.x, current.y];
                bool rise = !landing && parentTier < maxTiers &&
                    random.NextDouble() < (maze ? mazeHilliness : hilliness) * .5f &&
                    (!maze || CanPlaceRamp(child, child + Directions[direction]));
                levels[child.x, child.y] = parentTier + (rise ? 1 : 0);
                entrances[child.x, child.y] = rise ? (direction + 2) % 4 : -1;
                if (maze && rise) lockedDirection = direction;
                order.Add(child);
                current = child;
            }

            bool InBounds(Vector2Int p) => (uint)p.x < Tiles && (uint)p.y < Tiles;

            void FindDirections(Vector2Int p)
            {
                available.Clear();
                for (int side = 0; side < Directions.Length; side++)
                {
                    var next = p + Directions[side];
                    if (InBounds(next) && levels[next.x, next.y] < 0) available.Add(side);
                }
            }

            bool CanPlaceRamp(Vector2Int ramp, Vector2Int landing)
            {
                if (!InBounds(landing) || levels[landing.x, landing.y] >= 0) return false;

                // Removing the ramp from the branching frontier must leave an entry to every unfilled area.
                visit++;
                int reached = 0;
                Enqueue(landing);
                foreach (var flat in order)
                    if (entrances[flat.x, flat.y] < 0)
                        foreach (var direction in Directions) Enqueue(flat + direction);

                while (pending.Count > 0)
                {
                    var point = pending.Dequeue();
                    foreach (var direction in Directions) Enqueue(point + direction);
                }
                return reached == Tiles * Tiles - order.Count - 1;

                void Enqueue(Vector2Int point)
                {
                    if (!InBounds(point) || point == ramp || levels[point.x, point.y] >= 0 ||
                        marked[point.x, point.y] == visit) return;
                    marked[point.x, point.y] = visit;
                    pending.Enqueue(point);
                    reached++;
                }
            }

            var vertices = new List<Vector3>(Cells * Cells * 5);
            var faces = new List<int>(Cells * Cells * 7);
            var colors = new List<Color>(Cells * Cells * 5);
            var positions = new Vector2[Cells + 1, Cells + 1];
            flatSpots.Clear();
            for (int z = 0; z <= Cells; z++)
            for (int x = 0; x <= Cells; x++)
            {
                var point = new Vector2(x, z) * spacing - Vector2.one * (worldSize * .5f);
                float border = Mathf.Clamp01(Mathf.Min(Mathf.Min(x, z), Mathf.Min(Cells - x, Cells - z)) / 3f);
                float wx = Mathf.PerlinNoise(noiseOffset + x * .083f, 5f + z * .083f) - .5f;
                float wz = Mathf.PerlinNoise(12f + x * .078f, noiseOffset + z * .078f) - .5f;
                float driftX = Mathf.PerlinNoise(noiseOffset + x * .033f, 35f + z * .047f) - .5f;
                float driftZ = Mathf.PerlinNoise(67f + x * .051f, noiseOffset + z * .035f) - .5f;
                Vector2 warp = new Vector2(driftX, driftZ) * .85f + new Vector2(wx, wz) * .22f;
                positions[x, z] = point + warp * spacing * border;
            }

            for (int z = 0; z < Cells; z++)
            for (int x = 0; x < Cells; x++)
            {
                var a = Corner(x, z, 0, 0);
                var b = Corner(x, z, 1, 0);
                var c = Corner(x, z, 0, 1);
                var d = Corner(x, z, 1, 1);
                int tier = levels[x / TileCells, z / TileCells];
                Color grass = Color.Lerp(new Color(.15f, .35f, .06f), new Color(.27f, .48f, .095f), tier / 3f);
                bool ramp = Mathf.Max(Mathf.Abs(a.y - d.y), Mathf.Abs(b.y - c.y)) > .01f;
                Quad(a, c, b, d, ramp ? Color.Lerp(grass, new Color(.48f, .34f, .17f), .42f) : grass);
                if (!ramp && x > 1 && z > 1 && x < Cells - 2 && z < Cells - 2)
                    flatSpots.Add((a + b + c + d) * .25f);

                if (x > 0) Wall(a, c, Corner(x - 1, z, 1, 0), Corner(x - 1, z, 1, 1));
                if (z > 0) Wall(b, a, Corner(x, z - 1, 1, 1), Corner(x, z - 1, 0, 1));
                if (x == 0) Wall(a, c, a - Vector3.up * 1.3f, c - Vector3.up * 1.3f);
                if (z == 0) Wall(b, a, b - Vector3.up * 1.3f, a - Vector3.up * 1.3f);
                if (x == Cells - 1) Wall(d, b, d - Vector3.up * 1.3f, b - Vector3.up * 1.3f);
                if (z == Cells - 1) Wall(c, d, c - Vector3.up * 1.3f, d - Vector3.up * 1.3f);
            }

            var mesh = new Mesh { name = $"Terraced Arena {seed}" };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(faces, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            Vector3 Corner(int x, int z, int dx, int dz)
            {
                int lx = x % TileCells, lz = z % TileCells;
                int entry = entrances[x / TileCells, z / TileCells];
                float height = levels[x / TileCells, z / TileCells] * activeTierHeight;
                int across = entry % 2 == 0 ? lz : lx;
                int depth = entry switch
                {
                    0 => TileCells - lx - dx,
                    1 => TileCells - lz - dz,
                    2 => lx + dx,
                    _ => lz + dz
                };
                if (entry >= 0 && (maze || across >= RampMin && across < RampMax))
                    height -= activeTierHeight * (1f - Mathf.Clamp01((float)depth / (maze ? TileCells : RampDepth)));
                var p = positions[x + dx, z + dz];
                return new Vector3(p.x, height, p.y);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
                faces.Add(i); faces.Add(i + 1); faces.Add(i + 2);
                faces.Add(i + 2); faces.Add(i + 1); faces.Add(i + 3);
            }

            Vector3 GrassLip(Vector3 top, Vector3 basePoint)
            {
                float noise = Mathf.PerlinNoise(noiseOffset + top.x * .32f, 11f + top.z * .32f);
                return top - Vector3.up * Mathf.Min(Mathf.Lerp(.17f, .46f, noise), top.y - basePoint.y);
            }

            void Wall(Vector3 a, Vector3 b, Vector3 otherA, Vector3 otherB)
            {
                if (Mathf.Abs(a.y - otherA.y) + Mathf.Abs(b.y - otherB.y) < .001f) return;
                if ((a.y - otherA.y) * (b.y - otherB.y) < 0f)
                {
                    float cut = (a.y - otherA.y) / (a.y - otherA.y - b.y + otherB.y);
                    Vector3 cross = Vector3.Lerp(a, b, cut);
                    Wall(a, cross, otherA, cross);
                    Wall(cross, b, cross, otherB);
                    return;
                }
                if (a.y + b.y < otherA.y + otherB.y)
                    (a, b, otherA, otherB) = (otherB, otherA, b, a);
                Color rock = new(.39f, .31f, .24f);
                Vector3 lipA = GrassLip(a, otherA);
                Vector3 lipB = GrassLip(b, otherB);
                Quad(a, lipA, b, lipB, new Color(.26f, .43f, .105f));
                Quad(lipA, otherA, lipB, otherB, rock);
            }
        }

        internal bool HasLayout => hasLayout;

        public float SampleHeightAt(Vector3 worldPoint) =>
            TrySample(worldPoint, out var surface, out _) ? surface.y : float.NaN;

        internal bool TrySample(Vector3 worldPoint, out Vector3 surface, out Vector3 normal)
        {
            surface = default;
            normal = Vector3.up;
            if (!hasLayout) return false;
            Vector3 origin = new(worldPoint.x, transform.position.y + 100f, worldPoint.z);
            if (!GetComponent<MeshCollider>().Raycast(new Ray(origin, Vector3.down), out var hit, 200f)) return false;
            surface = hit.point;
            normal = hit.normal;
            return true;
        }

        public bool TryGetFlatSpot(int minTier, int maxTier, float minClearanceFromCliffs,
            float minSpacingFromOthers, out Vector3 position, out Vector3 normal)
        {
            return TryGetFlatSpot(minTier, maxTier, minClearanceFromCliffs, minSpacingFromOthers, null, out position, out normal);
        }

        internal bool TryGetFlatSpot(int minTier, int maxTier, float minClearanceFromCliffs,
            float minSpacingFromOthers, Predicate<Vector3> accepts, out Vector3 position, out Vector3 normal)
        {
            position = default;
            normal = Vector3.up;
            if (!hasLayout || flatSpots.Count == 0 || minTier > maxTier) return false;
            var random = new System.Random(unchecked(seed * 397 ^ ++query));
            int offset = random.Next(flatSpots.Count);
            for (int i = 0; i < flatSpots.Count; i++)
            {
                Vector3 local = flatSpots[(offset + i) % flatSpots.Count];
                int tier = Mathf.Clamp(Mathf.RoundToInt(local.y / activeTierHeight), 0, maxTiers);
                if (tier < minTier || tier > maxTier) continue;
                Vector3 candidate = transform.TransformPoint(local);
                if (reserved.Exists(p => (Vector3.ProjectOnPlane(p - candidate, Vector3.up)).sqrMagnitude <
                    minSpacingFromOthers * minSpacingFromOthers)) continue;

                if (!TryGetFlatSupport(candidate, minClearanceFromCliffs, out var surface, out var surfaceNormal) ||
                    (accepts != null && !accepts(surface))) continue;
                position = surface;
                normal = surfaceNormal;
                reserved.Add(surface);
                return true;
            }
            return false;
        }

        internal bool TryGetFlatSupport(Vector3 point, float clearance, out Vector3 surface, out Vector3 normal)
        {
            if (!TrySample(point, out surface, out normal) || normal.y < .985f) return false;
            clearance = Mathf.Max(.1f, clearance);
            for (int side = 0; side < 8; side++)
            {
                float angle = side * (Mathf.PI / 4f);
                var edge = surface + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * clearance;
                if (!TrySample(edge, out var support, out var sideNormal) ||
                    Mathf.Abs(support.y - surface.y) > .36f || sideNormal.y < .90f) return false;
            }
            return true;
        }

        private readonly List<Vector3> decorations = new();

        internal bool TryGetDecorationSpot(System.Random random, float clearance, float separation,
            Predicate<Vector3> accepts, out Vector3 point)
        {
            point = default;
            if (!hasLayout || flatSpots.Count == 0) return false;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                Vector3 local = flatSpots[random.Next(flatSpots.Count)];
                int x = Mathf.FloorToInt((local.x + worldSize * .5f) / spacing);
                int z = Mathf.FloorToInt((local.z + worldSize * .5f) / spacing);
                // Leave each tile's through lanes open and keep the valley spawn clear.
                float edge = Mathf.Min(Mathf.Min(x % TileCells, TileCells - 1 - x % TileCells),
                    Mathf.Min(z % TileCells, TileCells - 1 - z % TileCells));
                if (edge > 2 || (local.x * local.x + local.z * local.z) < 30f) continue;
                Vector3 candidate = transform.TransformPoint(local);
                if (decorations.Exists(p => (p - candidate).sqrMagnitude < separation * separation) ||
                    reserved.Exists(p => (p - candidate).sqrMagnitude < 4f)) continue;
                bool safe = true;
                for (int side = 0; side < 4 && safe; side++)
                {
                    Vector2Int dir = Directions[side];
                    safe = TrySample(candidate + new Vector3(dir.x, 0, dir.y) * clearance, out var support, out _) &&
                        Mathf.Abs(candidate.y - support.y) < .14f;
                }
                if (!safe || (accepts != null && !accepts(candidate))) continue;
                decorations.Add(candidate);
                point = candidate;
                return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (runtimeMesh && runtimeMesh != previewMesh) DestroyMesh(runtimeMesh);
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}

