using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MiningSimulator.Ores.MegabonkTerrainGenerator))]
internal sealed class TerracedArenaEditor : Editor
{
    private const string Menu = "Tools/Create Terraced Megabonk Arena";
    private const string Root = "Assets/GameData/Maps/";

    [MenuItem(Menu, true)]
    private static bool CanCreate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(Menu)]
    private static void Create()
    {
        if (GameObject.Find("Terraced Megabonk Arena"))
        {
            Debug.LogWarning("Select the existing Terraced Megabonk Arena to regenerate it.");
            return;
        }
        var arena = new GameObject("Terraced Megabonk Arena");
        Undo.RegisterCreatedObjectUndo(arena, "Create Terraced Megabonk Arena");
        var terrain = arena.AddComponent<MiningSimulator.Ores.MegabonkTerrainGenerator>();
        var populator = arena.AddComponent<MiningSimulator.Ores.MapFeaturePopulator>();
        var terrainFields = new SerializedObject(terrain);
        terrainFields.FindProperty("surfaceMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/GameData/ZeroFriction.physicMaterial");
        terrainFields.ApplyModifiedProperties();

        var rules = new SerializedObject(populator);
        rules.FindProperty("obstacleMaterial").objectReferenceValue = terrainFields.FindProperty("surfaceMaterial").objectReferenceValue;
        var list = rules.FindProperty("featureRules");
        string[] names = { "Rule_Early10Lootboxes", "Rule_WorldPortalGate" };
        list.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MiningSimulator.Ores.MapFeatureRule>(Root + "FeatureRules/" + names[i] + ".asset");
        rules.ApplyModifiedProperties();

        var renderer = arena.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "TerracedArena.mat");
        GeneratePreview(terrain, terrain.Seed);
        Selection.activeGameObject = arena;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var terrain = (MiningSimulator.Ores.MegabonkTerrainGenerator)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Generate Random Map (New Seed)"))
                GeneratePreview(terrain, Guid.NewGuid().GetHashCode());
            if (GUILayout.Button("Regenerate Current Seed"))
                GeneratePreview(terrain, terrain.Seed);
        }
    }

    internal static void GeneratePreview(MiningSimulator.Ores.MegabonkTerrainGenerator terrain, int seed)
    {
        Undo.RecordObjects(new UnityEngine.Object[] {
            terrain, terrain.gameObject, terrain.GetComponent<MeshFilter>(), terrain.GetComponent<MeshCollider>()
        }, "Generate Terraced Arena");
        terrain.GenerateNewSeed(seed);
        var generated = terrain.GetComponent<MeshFilter>().sharedMesh;
        if (!generated || EditorUtility.IsPersistent(generated)) return;

        var asset = Instantiate(generated);
        var previous = (Mesh)new SerializedObject(terrain).FindProperty("previewMesh").objectReferenceValue;
        if (previous && AssetDatabase.IsMainAsset(previous))
        {
            Undo.RecordObject(previous, "Regenerate arena mesh");
            previous.Clear();
            previous.name = generated.name;
            previous.indexFormat = generated.indexFormat;
            previous.vertices = generated.vertices;
            previous.triangles = generated.triangles;
            previous.colors = generated.colors;
            previous.RecalculateNormals();
            previous.RecalculateBounds();
            EditorUtility.SetDirty(previous);
            DestroyImmediate(asset);
            asset = previous;
        }
        else
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(Root + "TerracedArenaPreview.asset");
            AssetDatabase.CreateAsset(asset, path);
        }
        terrain.GetComponent<MiningSimulator.Ores.MapFeaturePopulator>()?.PopulateDecoration();
        terrain.SetPreviewMesh(asset);
        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(terrain);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
    }
}

