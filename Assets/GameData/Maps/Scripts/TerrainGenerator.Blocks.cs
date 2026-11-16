using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class TerrainGenerator
    {
        [SerializeField] private GameObject regularBlock;
        [SerializeField] private GameObject slopeBlock;
        [SerializeField] private Material foundationMaterial;
        private const string BlocksRoot = "Terrain Blocks";
        private readonly HashSet<Mesh> batchedBlockMeshes = new();
        private int[,] blockTiers, blockSlopes;
        private bool UsesBlocks => regularBlock && slopeBlock && foundationMaterial;

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
            var root = new GameObject(BlocksRoot).transform;
            root.SetParent(transform, false);
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate terrain blocks");
#endif
            float tileSize = worldSize / MapSize;
            Vector3 dimensions = new(tileSize, tierHeight, tileSize);
            for (int z = 0; z < MapSize; z++)
            for (int x = 0; x < MapSize; x++)
            {
                int tier = tiers[z, x];
                int slope = slopes[z, x];
                for (int y = -1; y < tier; y++)
                {
                    bool top = y == tier - 1;
                    var prefab = top && slope >= 0 ? slopeBlock : regularBlock;
                    GameObject block;
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        block = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, root);
                    else
#endif
                        block = Instantiate(prefab, root);
                    block.transform.SetLocalPositionAndRotation(
                        new Vector3((x + .5f) * tileSize, y * tierHeight, (z + .5f) * tileSize),
                        slope >= 0 && top
                            ? Quaternion.LookRotation(new Vector3(Directions[slope].x, 0f, Directions[slope].y))
                            : Quaternion.identity);
                    block.transform.localScale = dimensions;
                    var renderer = block.GetComponent<MeshRenderer>();
                    if (!top)
                    {
                        renderer.sharedMaterials = new[] { foundationMaterial, foundationMaterial };
                        // Only exposed fill needs rendering. Every stacked block still supports collision.
                        bool visible = x == 0 || z == 0 || x == MapSize - 1 || z == MapSize - 1;
                        foreach (var side in Directions)
                        {
                            int nx = x + side.x, nz = z + side.y;
                            if ((uint)nx >= MapSize || (uint)nz >= MapSize) continue;
                            int adjacentMin = tiers[nz, nx] - (slopes[nz, nx] >= 0 ? 1 : 0);
                            if (adjacentMin < y + 1) visible = true;
                        }
                        renderer.enabled = visible;
                    }
                }
            }
            if (!Application.isPlaying) return;
            var filters = root.GetComponentsInChildren<MeshFilter>();
            var sourceMeshes = new HashSet<Mesh>();
            foreach (var filter in filters) sourceMeshes.Add(filter.sharedMesh);
            StaticBatchingUtility.Combine(root.gameObject);
            foreach (var filter in filters)
                if (filter.sharedMesh && !sourceMeshes.Contains(filter.sharedMesh))
                    batchedBlockMeshes.Add(filter.sharedMesh);
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
            foreach (var mesh in batchedBlockMeshes)
                if (mesh) Destroy(mesh);
            batchedBlockMeshes.Clear();
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
