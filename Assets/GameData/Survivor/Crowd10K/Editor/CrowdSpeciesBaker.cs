using System;
using System.Collections.Generic;
using System.Linq;
using MiningSimulator.Ores;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survivor.CrowdPrototype
{
    public static class CrowdSpeciesBaker
    {
        public const string CatalogPath = "Assets/GameData/Survivor/Crowd10K/Resources/CrowdSpeciesCatalog.asset";
        private const int Frames = 12;

        private sealed class Part
        {
            public Renderer renderer;
            public Mesh mesh;
            public int start;
            public Material[] materials;
        }

        private readonly struct SourceVertex
        {
            public readonly int index;
            public readonly int material;
            public SourceVertex(int index, int material) { this.index = index; this.material = material; }
        }

        [MenuItem("Survivor/Experimental/Bake All Crowd Species")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Bake crowd visuals outside Play Mode.");

            var settings = AssetDatabase.LoadAssetAtPath<MonsterSpawnSettings>("Assets/GameData/Spawning/MonsterSpawnSettings.asset");
            var god = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameData/Monsters/GodBoss/GodBoss.prefab").GetComponent<GodBossController>();
            var roster = new Dictionary<UnityEngine.Object, GameObject>();
            foreach (MonsterSpawnWorld world in Enum.GetValues(typeof(MonsterSpawnWorld)))
                foreach (var entry in settings.EntriesFor(world))
                    Add(entry?.prefab);
            Add(settings.firstMiniboss);
            Add(settings.secondMiniboss);
            Add(settings.stageBoss);
            Add(settings.overtimeEnemy);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Monsters", "Assets/GameData/Monsters" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab && prefab.TryGetComponent<MushroomMonster>(out var monster)) Add(monster);
            }
            roster[god.Data] = god.gameObject;

            void Add(MushroomMonster actor)
            {
                if (actor && actor.SpeciesData) roster.TryAdd(actor.SpeciesData, actor.gameObject);
            }

            var shader = Shader.Find("Survivor/Experimental/CrowdVAT");
            if (!shader) throw new InvalidOperationException("Crowd VAT shader is missing.");
            var catalog = AssetDatabase.LoadAssetAtPath<CrowdSpeciesCatalog>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<CrowdSpeciesCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var assets = new List<UnityEngine.Object>();
            var entries = new List<CrowdSpeciesCatalog.Species>();
            try
            {
                foreach (var (gameData, prefab) in roster)
                    entries.Add(BakeSpecies(gameData, prefab, shader, assets));

                foreach (var old in AssetDatabase.LoadAllAssetsAtPath(CatalogPath))
                    if (old && old != catalog) UnityEngine.Object.DestroyImmediate(old, allowDestroyingAssets: true);
                foreach (var asset in assets) AssetDatabase.AddObjectToAsset(asset, catalog);
                catalog.species = entries.ToArray();
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                AssetDatabase.ImportAsset(CatalogPath);
                Debug.Log($"Baked crowd visuals for {entries.Count} species: {string.Join(", ", entries.Select(e => e.prefabName))}");
            }
            catch
            {
                foreach (var asset in assets)
                    if (asset && !EditorUtility.IsPersistent(asset)) UnityEngine.Object.DestroyImmediate(asset);
                throw;
            }
        }

        private static CrowdSpeciesCatalog.Species BakeSpecies(UnityEngine.Object gameData, GameObject prefab,
            Shader shader, List<UnityEngine.Object> assets)
        {
            var sample = UnityEngine.Object.Instantiate(prefab);
            sample.hideFlags = HideFlags.HideAndDontSave;
            var scratch = new Mesh();
            try
            {
                sample.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                sample.transform.localScale = Vector3.one;
                var animator = sample.GetComponentInChildren<Animator>();
                var clips = animator && animator.runtimeAnimatorController
                    ? animator.runtimeAnimatorController.animationClips : Array.Empty<AnimationClip>();
                var idle = clips.FirstOrDefault(c => Match(c, "idle", "flap"));
                var walk = clips.FirstOrDefault(c => Match(c, "walk", "run", "flap"));
                if (animator) animator.enabled = false;

                var parts = new List<Part>();
                int vertexCount = 0;
                foreach (var renderer in sample.GetComponentsInChildren<Renderer>())
                {
                    if (!renderer.enabled || renderer.GetComponentInParent<Canvas>() || renderer.GetComponent<TMPro.TMP_SubMesh>() ||
                        renderer.GetComponent<TMPro.TMP_Text>()) continue;
                    Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh :
                        renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                    if (!mesh || mesh.vertexCount == 0 || renderer.sharedMaterials.Length == 0) continue;
                    parts.Add(new Part { renderer = renderer, mesh = mesh, start = vertexCount, materials = renderer.sharedMaterials });
                    vertexCount += mesh.vertexCount;
                }
                if (vertexCount == 0) throw new InvalidOperationException($"{prefab.name} has no crowd geometry.");

                var materials = parts.SelectMany(p => p.materials).Distinct().ToArray();
                var textures = materials.Select(MakeReadableTexture).ToArray();
                var atlas = new Texture2D(8, 8, TextureFormat.RGBA32, false) { name = prefab.name + " Base Map" };
                assets.Add(atlas);
                Rect[] rects;
                try { rects = atlas.PackTextures(textures, 4, 1024, makeNoLongerReadable: true); }
                finally { foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture); }

                var positions = new Vector3[vertexCount];
                var normals = new Vector3[vertexCount];
                var uv = new Vector2[vertexCount];
                foreach (var part in parts)
                {
                    var sourceUV = part.mesh.uv;
                    Array.Copy(sourceUV, 0, uv, part.start, sourceUV.Length);
                }
                CapturePose(parts, sample.transform, scratch, positions, normals);
                var bounds = new Bounds(positions[0], Vector3.zero);
                foreach (var point in positions) bounds.Encapsulate(point);

                var packedIndices = new Dictionary<int, int>();
                var packedSource = new List<int>();
                var lods = new Mesh[4];
                int[] cells = { 48, 24, 14, 8 };
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    float cell = Mathf.Max(.005f, bounds.size.magnitude / cells[lod]);
                    lods[lod] = BuildLod(prefab.name, lod, cell, bounds, parts, materials,
                        positions, uv, rects, packedIndices, packedSource);
                    assets.Add(lods[lod]);
                }

                int width = Mathf.Min(8192, Mathf.NextPowerOfTwo(packedSource.Count));
                int rows = Mathf.CeilToInt(packedSource.Count / (float)width);
                int frames = clips.Length == 0 ? 1 : Frames;
                var pos = NewPoseTexture(prefab.name + " Positions", width, rows * frames * 2);
                var norm = NewPoseTexture(prefab.name + " Normals", width, rows * frames * 2);
                assets.Add(pos);
                assets.Add(norm);
                var posPixels = new Color[width * rows * frames * 2];
                var normPixels = new Color[posPixels.Length];
                var poses = new[] { idle ? idle : walk, walk ? walk : idle };
                var lengths = new float[2];
                for (int clip = 0; clip < poses.Length; clip++)
                {
                    lengths[clip] = poses[clip] ? Mathf.Max(.01f, poses[clip].length) : 1f;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        if (poses[clip] && animator)
                            poses[clip].SampleAnimation(animator.gameObject, lengths[clip] * frame / frames);
                        CapturePose(parts, sample.transform, scratch, positions, normals);
                        int offset = (clip * frames + frame) * rows * width;
                        for (int v = 0; v < packedSource.Count; v++)
                        {
                            int source = packedSource[v];
                            var p = positions[source];
                            var n = normals[source];
                            posPixels[offset + v] = new Color(p.x, p.y, p.z, 1f);
                            normPixels[offset + v] = new Color(n.x, n.y, n.z, 0f);
                            bounds.Encapsulate(p);
                        }
                    }
                }
                pos.SetPixels(posPixels); pos.Apply(false, true);
                norm.SetPixels(normPixels); norm.Apply(false, true);
                foreach (var lod in lods) lod.bounds = bounds;

                var material = new Material(shader) { name = prefab.name + " Crowd Material", enableInstancing = true };
                material.SetTexture("_BaseMap", atlas);
                material.SetTexture("_PositionTex", pos);
                material.SetTexture("_NormalTex", norm);
                material.SetFloat("_VatRowsPerFrame", rows);
                assets.Add(material);
                var data = ScriptableObject.CreateInstance<Crowd10KData>();
                data.name = prefab.name + " Crowd";
                data.meshes = lods;
                data.material = material;
                data.positions = pos;
                data.normals = norm;
                data.framesPerClip = frames;
                data.clipLengths = lengths;
                assets.Add(data);
                return new CrowdSpeciesCatalog.Species
                {
                    gameData = gameData, prefabName = prefab.name, visuals = data,
                    rendererPaths = parts.Select(p => AnimationUtility.CalculateTransformPath(p.renderer.transform, sample.transform)).ToArray()
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scratch);
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }

        private static bool Match(AnimationClip clip, params string[] terms) =>
            terms.Any(term => clip.name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

        private static Texture2D NewPoseTexture(string name, int width, int height) =>
            new Texture2D(width, height, TextureFormat.RGBAHalf, false, true)
            { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        private static Texture2D MakeReadableTexture(Material material)
        {
            Texture source = material ? material.mainTexture : null;
            Color color = material && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
            int width = source ? Mathf.Min(256, source.width) : 4;
            int height = source ? Mathf.Min(256, source.height) : 4;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            if (source)
            {
                var previous = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(width, height, 0);
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            var pixels = result.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = source ? pixels[i] * color : color;
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        private static void CapturePose(List<Part> parts, Transform root, Mesh scratch, Vector3[] positions, Vector3[] normals)
        {
            foreach (var part in parts)
            {
                Mesh mesh = part.mesh;
                if (part.renderer is SkinnedMeshRenderer skin)
                {
                    // The matrix below supplies the renderer scale once, including imported rig units.
                    skin.BakeMesh(scratch, useScale: true);
                    mesh = scratch;
                }
                var matrix = root.worldToLocalMatrix * part.renderer.transform.localToWorldMatrix;
                var normalMatrix = matrix.inverse.transpose;
                var verts = mesh.vertices;
                var ns = mesh.normals;
                for (int v = 0; v < verts.Length; v++)
                {
                    positions[part.start + v] = matrix.MultiplyPoint3x4(verts[v]);
                    normals[part.start + v] = v < ns.Length ? normalMatrix.MultiplyVector(ns[v]).normalized : Vector3.up;
                }
            }
        }

        private static Mesh BuildLod(string name, int level, float cell, Bounds bounds, List<Part> parts,
            Material[] materials, Vector3[] positions, Vector2[] originalUV, Rect[] atlas,
            Dictionary<int, int> packedIndices, List<int> packedSource)
        {
            var clusters = new Dictionary<(int, int, int, int), int>();
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var vertexIds = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var part in parts)
            {
                for (int sub = 0; sub < part.mesh.subMeshCount; sub++)
                {
                    int mat = Array.IndexOf(materials, part.materials[Mathf.Min(sub, part.materials.Length - 1)]);
                    var indices = part.mesh.GetIndices(sub);
                    if (part.mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    int Map(int source)
                    {
                        source += part.start;
                        var point = positions[source] - bounds.min;
                        var key = (Mathf.FloorToInt(point.x / cell), Mathf.FloorToInt(point.y / cell),
                            Mathf.FloorToInt(point.z / cell), mat);
                        if (clusters.TryGetValue(key, out int found)) return found;
                        int vertex = vertices.Count;
                        clusters.Add(key, vertex);
                        vertices.Add(positions[source]);
                        var rect = atlas[mat];
                        var tex = originalUV[source];
                        uv.Add(new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, tex.x), Mathf.Lerp(rect.yMin, rect.yMax, tex.y)));
                        if (!packedIndices.TryGetValue(source, out int index))
                        {
                            index = packedSource.Count;
                            packedIndices.Add(source, index);
                            packedSource.Add(source);
                        }
                        vertexIds.Add(new Vector2(index, 0));
                        return vertex;
                    }
                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        int a = Map(indices[t]), b = Map(indices[t + 1]), c = Map(indices[t + 2]);
                        if (a == b || b == c || a == c) continue;
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    }
                }
            }
            if (triangles.Count == 0) throw new InvalidOperationException($"{name} LOD {level} has no geometry.");
            var mesh = new Mesh { name = name + " LOD " + level, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, vertexIds);
            mesh.SetTriangles(triangles, 0, false);
            return mesh;
        }
    }
}
