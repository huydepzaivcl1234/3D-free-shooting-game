using System.Collections.Generic;
using MiningSimulator.Ores;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class MegabonkTerrainGenerator
{
    private const string GenerateMenu = "Tools/Generate Megabonk Arena";
    private const string ValidateMenu = "Tools/Validate Walkable Slopes";
    private const int Cells = 64;
    private const float Size = 96f;
    private const float WarningAngle = 40f;
    private const float MaximumGrade = .34f;
    private static readonly Vector2[] Hills =
    {
        new(-31, -32), new(32, -29), new(-29, 32), new(34, 30)
    };
    private static readonly List<Vector3> WarningLines = new();

    [MenuItem(GenerateMenu, true)]
    private static bool CanGenerate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(GenerateMenu)]
    private static void Generate()
    {
        int layer = LayerMask.NameToLayer("Ground");
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (layer < 0 || !shader)
        {
            Debug.LogError("Megabonk Arena requires the Ground layer and a lit shader.");
            return;
        }

        Mesh mesh = BuildMesh(out float maximumSlope);
        var material = new Material(shader) { name = "Arena Grass", color = new Color(.32f, .47f, .22f) };
        material.SetFloat("_Smoothness", .12f);

        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/GameData/MegabonkArena.asset");
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.AddObjectToAsset(material, mesh);
        AssetDatabase.SaveAssets();

        var arena = new GameObject("Megabonk Arena", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        arena.layer = layer;
        arena.GetComponent<MeshFilter>().sharedMesh = mesh;
        arena.GetComponent<MeshRenderer>().sharedMaterial = material;
        var collider = arena.GetComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/GameData/ZeroFriction.physicMaterial");
        Undo.RegisterCreatedObjectUndo(arena, "Generate Megabonk Arena");
        Selection.activeGameObject = arena;
        Debug.Log($"Megabonk Arena: {Cells} x {Cells} cells, maximum face slope {maximumSlope:F2} degrees.", arena);
    }

    private static Mesh BuildMesh(out float maximumSlope)
    {
        int stride = Cells + 1;
        var vertices = new Vector3[stride * stride];
        var uv = new Vector2[vertices.Length];
        var indices = new int[Cells * Cells * 6];
        float spacing = Size / Cells;
        for (int z = 0; z <= Cells; z++)
        for (int x = 0; x <= Cells; x++)
        {
            int i = z * stride + x;
            var point = new Vector2(x * spacing - Size * .5f, z * spacing - Size * .5f);
            vertices[i] = new Vector3(point.x, HeightAt(point), point.y);
            uv[i] = point / 8f;
        }

        int index = 0;
        for (int z = 0; z < Cells; z++)
        for (int x = 0; x < Cells; x++)
        {
            int a = z * stride + x;
            indices[index++] = a;
            indices[index++] = a + stride;
            indices[index++] = a + 1;
            indices[index++] = a + 1;
            indices[index++] = a + stride;
            indices[index++] = a + stride + 1;
        }

        // Scale the full height field uniformly to preserve smooth joins and enforce the limit on every triangle.
        // The current 1 m A* grid also needs adjacent samples to stay within its 0.5 m step limit.
        maximumSlope = MaximumSlope(vertices, indices);
        float grade = Mathf.Tan(maximumSlope * Mathf.Deg2Rad);
        if (grade > MaximumGrade)
        {
            float scale = MaximumGrade / grade;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y *= scale;
            maximumSlope = MaximumSlope(vertices, indices);
        }

        var mesh = new Mesh { name = "Megabonk Arena Terrain" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float HeightAt(Vector2 point)
    {
        float basin = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(17f, 33f, point.magnitude));
        float noise = Mathf.PerlinNoise(point.x * .057f + 18, point.y * .057f + 31);
        float detail = Mathf.PerlinNoise(point.x * .118f + 42, point.y * .118f + 12);
        float edge = Mathf.SmoothStep(0, 1,
            Mathf.InverseLerp(34f, 48f, Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y))));
        float hill = 0;
        foreach (var center in Hills)
        {
            float distance = (point - center).sqrMagnitude;
            hill += 3.25f * Mathf.Exp(-distance / (2f * 9.5f * 9.5f));
        }
        return Mathf.Clamp(.05f + .5f * noise + .18f * detail + basin * (hill + 1.25f * edge), 0f, 4.5f);
    }

    private static float MaximumSlope(Vector3[] vertices, int[] indices)
    {
        float maximum = 0;
        for (int i = 0; i < indices.Length; i += 3)
            maximum = Mathf.Max(maximum, Angle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]));
        return maximum;
    }

    private static float Angle(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Angle(Vector3.Cross(b - a, c - a), Vector3.up);

    [MenuItem(ValidateMenu)]
    private static void ValidateSlopes()
    {
        WarningLines.Clear();
        int mask = GroundSurfaceLayers.Mask;
        var grids = Object.FindObjectsByType<WorldNavigationGrid>(FindObjectsSortMode.None);
        if (grids.Length > 0)
        {
            mask = 0;
            foreach (var grid in grids)
                mask |= new SerializedObject(grid).FindProperty("groundLayers").intValue;
        }

        int checkedColliders = 0;
        var colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (var collider in colliders)
        {
            if (!collider.enabled || collider.isTrigger || EditorUtility.IsPersistent(collider) ||
                (mask & (1 << collider.gameObject.layer)) == 0) continue;
            switch (collider)
            {
                case MeshCollider meshCollider when meshCollider.sharedMesh:
                    using (var meshes = Mesh.AcquireReadOnlyMeshData(meshCollider.sharedMesh))
                    {
                        var mesh = meshes[0];
                        using var vertices = new Unity.Collections.NativeArray<Vector3>(
                            mesh.vertexCount, Unity.Collections.Allocator.Temp);
                        mesh.GetVertices(vertices);
                        for (int sub = 0; sub < mesh.subMeshCount; sub++)
                        {
                            var section = mesh.GetSubMesh(sub);
                            if (section.topology != MeshTopology.Triangles) continue;
                            using var indices = new Unity.Collections.NativeArray<int>(
                                section.indexCount, Unity.Collections.Allocator.Temp);
                            mesh.GetIndices(indices, sub);
                            for (int i = 0; i < indices.Length; i += 3)
                                CheckFace(collider.transform, vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                        }
                    }
                    break;
                case BoxCollider box:
                    CheckBox(box);
                    break;
                case TerrainCollider terrain when terrain.terrainData:
                    CheckTerrain(terrain);
                    break;
                default:
                    continue;
            }
            checkedColliders++;
        }

        SceneView.duringSceneGui -= DrawWarnings;
        if (WarningLines.Count > 0)
        {
            SceneView.duringSceneGui += DrawWarnings;
            for (int i = 0; i < WarningLines.Count; i += 2)
                Debug.DrawLine(WarningLines[i], WarningLines[i + 1], Color.red, 12f, false);
        }
        SceneView.RepaintAll();
        Debug.Log($"Walkable Slopes: {WarningLines.Count / 6} faces above {WarningAngle} degrees across {checkedColliders} ground colliders.");
    }

    private static void CheckBox(BoxCollider box)
    {
        Vector3 half = box.size * .5f;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 u = Vector3.zero, v = Vector3.zero, offset = Vector3.zero;
            u[(axis + 1) % 3] = half[(axis + 1) % 3];
            v[(axis + 2) % 3] = half[(axis + 2) % 3];
            offset[axis] = half[axis];
            Vector3 a = box.center + offset;
            Vector3 b = box.center - offset;
            CheckFace(box.transform, a - u - v, a + u - v, a + u + v);
            CheckFace(box.transform, a - u - v, a + u + v, a - u + v);
            CheckFace(box.transform, b - u - v, b - u + v, b + u + v);
            CheckFace(box.transform, b - u - v, b + u + v, b + u - v);
        }
    }

    private static void CheckTerrain(TerrainCollider collider)
    {
        var data = collider.terrainData;
        int cells = data.heightmapResolution - 1;
        var step = data.heightmapScale;
        var heights = data.GetHeights(0, 0, cells + 1, cells + 1);
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            if (data.IsHole(x, z)) continue;
            Vector3 a = Point(x, z), b = Point(x + 1, z), c = Point(x, z + 1), d = Point(x + 1, z + 1);
            if (((x + z) & 1) == 0)
            {
                CheckFace(collider.transform, a, c, d);
                CheckFace(collider.transform, a, d, b);
            }
            else
            {
                CheckFace(collider.transform, a, c, b);
                CheckFace(collider.transform, b, c, d);
            }
        }
        Vector3 Point(int x, int z) => new Vector3(x * step.x, heights[z, x] * step.y, z * step.z);
    }

    private static void CheckFace(Transform transform, Vector3 a, Vector3 b, Vector3 c)
    {
        a = transform.TransformPoint(a);
        b = transform.TransformPoint(b);
        c = transform.TransformPoint(c);
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.y < -.00001f || normal.sqrMagnitude < .00000001f || Angle(a, b, c) <= WarningAngle) return;
        WarningLines.Add(a); WarningLines.Add(b);
        WarningLines.Add(b); WarningLines.Add(c);
        WarningLines.Add(c); WarningLines.Add(a);
    }

    private static void DrawWarnings(SceneView view)
    {
        if (Event.current.type != EventType.Repaint) return;
        using var scope = new Handles.DrawingScope(Color.red);
        var depth = Handles.zTest;
        Handles.zTest = CompareFunction.Always;
        try { Handles.DrawLines(WarningLines.ToArray()); }
        finally { Handles.zTest = depth; }
    }
}

