using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class TerrainGenerator
    {
        [SerializeField] private Material blockSurfaceMaterial;
        private const string BlocksRoot = "Terrain Blocks";
        private static readonly Vector2Int[] SurfaceCorners =
            { Vector2Int.zero, Vector2Int.up, Vector2Int.one, Vector2Int.right };
        private static readonly Vector2Int[] SurfaceNeighbors =
            { Vector2Int.left, Vector2Int.up, Vector2Int.right, Vector2Int.down };
        private Mesh blockMesh;
        private readonly List<(Mesh mesh, MeshRenderer renderer, Bounds bounds)> blockChunks = new();
        private int[,] blockTiers, blockSlopes;
        private bool UsesBlocks => blockSurfaceMaterial;

        internal bool TryGetBlockLayout(out int[,] tiers, out int[,] slopes)
        {
            tiers = blockTiers;
            slopes = blockSlopes;
            return UsesBlocks && tiers != null && slopes != null;
        }

        private void BuildBlocks(int[,] tiers, int[,] slopes)
        {
            ClearBlocks();
            blockTiers = tiers;
            blockSlopes = slopes;
            blockMesh = CreateBlockMesh(tiers, slopes);
            var root = new GameObject(BlocksRoot, typeof(MeshCollider));
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RegisterCreatedObjectUndo(root, "Generate terrain surface");
#endif
            var collider = root.GetComponent<MeshCollider>();
            collider.sharedMaterial = surfaceCollider.sharedMaterial;
            collider.sharedMesh = blockMesh;

            var performance = Application.isPlaying ? GameplayPerformanceData.Current : null;
            int chunkTiles = performance ? performance.terrainRenderChunkTiles : 0;
            if (chunkTiles <= 0 || chunkTiles >= MapSize)
            {
                AddBlockRenderer(root, blockMesh);
                return;
            }

            for (int z = 0; z < MapSize; z += chunkTiles)
            for (int x = 0; x < MapSize; x += chunkTiles)
            {
                var chunk = new GameObject($"Chunk {x / chunkTiles}, {z / chunkTiles}");
                chunk.layer = root.layer;
                chunk.transform.SetParent(root.transform, false);
                var mesh = CreateBlockMesh(tiers, slopes, x, z, chunkTiles);
                var renderer = AddBlockRenderer(chunk, mesh);
                blockChunks.Add((mesh, renderer, mesh.bounds));
            }
        }

        private MeshRenderer AddBlockRenderer(GameObject owner, Mesh mesh)
        {
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = owner.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = blockSurfaceMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        internal void UpdateBlockVisibility(float radius)
        {
            if (blockChunks.Count == 0) return;
            Vector3 center = player ? transform.InverseTransformPoint(player.position) : Vector3.zero;
            float radiusSquared = radius * radius;
            foreach (var chunk in blockChunks)
            {
                Vector3 nearest = chunk.bounds.ClosestPoint(new Vector3(center.x, chunk.bounds.center.y, center.z));
                float x = center.x - nearest.x, z = center.z - nearest.z;
                bool visible = !player || radius <= 0f || x * x + z * z <= radiusSquared;
                if (chunk.renderer.enabled != visible) chunk.renderer.enabled = visible;
            }
        }

        private Mesh CreateBlockMesh(int[,] tiers, int[,] slopes, int startX = 0, int startZ = 0, int size = MapSize)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            var corners = new Vector3[4];
            float tileSize = worldSize / MapSize;
            float grassDepth = tierHeight * .24f;

            int endX = Mathf.Min(startX + size, MapSize);
            int endZ = Mathf.Min(startZ + size, MapSize);
            for (int z = startZ; z < endZ; z++)
            for (int x = startX; x < endX; x++)
            {
                for (int k = 0; k < corners.Length; k++)
                {
                    var corner = SurfaceCorners[k];
                    corners[k] = new Vector3((x + corner.x) * tileSize, Height(x, z, corner), (z + corner.y) * tileSize);
                }
                int top = vertices.Count;
                int topTile = slopes[z, x] < 0 ? 0 : 1;
                for (int k = 0; k < corners.Length; k++)
                    Append(corners[k], topTile, SurfaceCorners[k].x, SurfaceCorners[k].y);
                indices.AddRange(new[] { top, top + 1, top + 2, top, top + 2, top + 3 });

                for (int side = 0; side < 4; side++)
                {
                    int next = (side + 1) % 4;
                    var neighbor = SurfaceNeighbors[side];
                    Vector3 a = corners[side], b = corners[next];
                    float depthA = a.y - Height(x + neighbor.x, z + neighbor.y, SurfaceCorners[side] - neighbor);
                    float depthB = b.y - Height(x + neighbor.x, z + neighbor.y, SurfaceCorners[next] - neighbor);
                    float depth = Mathf.Max(depthA, depthB);
                    if (depth <= .001f) continue;
                    Strip(a, b, depthA, depthB, 0f, grassDepth, 2);
                    for (float offset = grassDepth; offset < depth - .001f; offset += tierHeight)
                        Strip(a, b, depthA, depthB, offset, offset + tierHeight, 3);
                }
            }

            var mesh = new Mesh { name = "Arena Surface" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            float Height(int x, int z, Vector2Int corner)
            {
                if ((uint)x >= MapSize || (uint)z >= MapSize) return -tierHeight;
                int slope = slopes[z, x];
                float height = tiers[z, x];
                if (slope >= 0)
                {
                    var up = Directions[slope];
                    height += (corner.x - .5f) * up.x + (corner.y - .5f) * up.y - .5f;
                }
                return height * tierHeight;
            }

            void Append(Vector3 point, int tile, float u, float v)
            {
                // Each 256 px atlas quadrant has an 8 px extruded gutter for mipmaps.
                vertices.Add(point);
                uvs.Add(new Vector2(
                    ((tile % 2) * 256 + 8.5f + u * 239f) / 512,
                    ((1 - tile / 2) * 256 + 8.5f + v * 239f) / 512));
            }

            void Strip(Vector3 a, Vector3 b, float depthA, float depthB, float start, float end, int tile)
            {
                if (Mathf.Max(depthA, depthB) <= start) return;
                float change = depthB - depthA;
                float from = 0f, to = 1f;
                if (Mathf.Min(depthA, depthB) < start)
                {
                    float intersection = Mathf.Clamp01((start - depthA) / change);
                    if (change > 0f) from = intersection;
                    else to = intersection;
                }
                if (to - from < .0001f) return;
                if (Mathf.Min(depthA, depthB) < end && Mathf.Max(depthA, depthB) > end)
                {
                    float split = Mathf.Clamp((end - depthA) / change, from, to);
                    Wall(from, split);
                    Wall(split, to);
                }
                else Wall(from, to);

                void Wall(float left, float right)
                {
                    if (right - left < .0001f) return;
                    float leftDepth = Mathf.Clamp(Mathf.Lerp(depthA, depthB, left), start, end);
                    float rightDepth = Mathf.Clamp(Mathf.Lerp(depthA, depthB, right), start, end);
                    var leftTop = Vector3.Lerp(a, b, left) - Vector3.up * start;
                    var rightTop = Vector3.Lerp(a, b, right) - Vector3.up * start;
                    int first = vertices.Count;
                    Append(leftTop, tile, left, 1f);
                    Append(leftTop - Vector3.up * (leftDepth - start), tile, left, Mathf.InverseLerp(end, start, leftDepth));
                    Append(rightTop - Vector3.up * (rightDepth - start), tile, right, Mathf.InverseLerp(end, start, rightDepth));
                    Append(rightTop, tile, right, 1f);
                    if (leftDepth > start + .001f) indices.AddRange(new[] { first, first + 1, first + 2 });
                    if (rightDepth > start + .001f) indices.AddRange(new[] { first, first + 2, first + 3 });
                }
            }
        }

        private void ClearBlocks()
        {
            var previous = transform.Find(BlocksRoot);
            if (previous)
            {
                previous.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(previous.gameObject);
#if UNITY_EDITOR
                else UnityEditor.Undo.DestroyObjectImmediate(previous.gameObject);
#endif
            }
            ReleaseBlockMeshes();
            blockTiers = blockSlopes = null;
        }

        private void ReleaseBlockMeshes()
        {
            foreach (var chunk in blockChunks)
                if (chunk.mesh) Destroy(chunk.mesh);
            blockChunks.Clear();
            if (!blockMesh) return;
            if (Application.isPlaying) Destroy(blockMesh);
#if UNITY_EDITOR
            else if (!UnityEditor.EditorUtility.IsPersistent(blockMesh)) DestroyImmediate(blockMesh);
#endif
            blockMesh = null;
        }

        internal bool TryGetRisingBlockFace(Vector3 foot, Vector3 inward, float reach, out Vector3 face)
        {
            face = foot;
            float size = worldSize / MapSize;
            Vector3 local = foot - transform.position;
            bool xAxis = inward.x != 0f;
            float coordinate = xAxis ? local.x : local.z;
            float sign = xAxis ? inward.x : inward.z;
            float tile = Mathf.Floor(coordinate / size);
            float edge = (tile + (sign > 0f ? 1f : 0f)) * size;
            float distance = (edge - coordinate) * sign;
            if (distance < 0f || distance > reach) return false;

            face += inward * distance;
            const float sampleInset = .04f;
            return SampleBlock(face - inward * sampleInset, out var lower, out _) &&
                SampleBlock(face + inward * sampleInset, out var upper, out _) &&
                upper.y - lower.y >= .65f;
        }

        private bool SampleBlock(Vector3 point, out Vector3 surface, out Vector3 normal)
        {
            surface = point;
            normal = Vector3.up;
            float size = worldSize / MapSize;
            Vector3 local = point - transform.position;
            int x = Mathf.FloorToInt(local.x / size), z = Mathf.FloorToInt(local.z / size);
            if ((uint)x >= MapSize || (uint)z >= MapSize) return false;
            float level = blockTiers[z, x];
            int slope = blockSlopes[z, x];
            if (slope >= 0)
            {
                var dir = Directions[slope];
                float offset = (local.x / size - x - .5f) * dir.x + (local.z / size - z - .5f) * dir.y;
                level += offset - .5f;
                normal = new Vector3(-dir.x * tierHeight / size, 1f, -dir.y * tierHeight / size).normalized;
            }
            surface.y = transform.position.y + tierHeight * level;
            return true;
        }
    }
}
