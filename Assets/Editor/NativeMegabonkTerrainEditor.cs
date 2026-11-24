using MiningSimulator.Ores;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(TerrainGenerator))]
internal sealed class NativeMegabonkTerrainEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var generator = (TerrainGenerator)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Regenerate Current Seed"))
                Generate(generator, generator.Seed);
            if (GUILayout.Button("Generate New Random Seed"))
                Generate(generator, System.Guid.NewGuid().GetHashCode());
        }
    }

    private static void Generate(TerrainGenerator generator, int seed)
    {
        var terrainData = generator.GetComponent<Terrain>().terrainData;
        Undo.RegisterCompleteObjectUndo(terrainData, "Generate Megabonk Terrain");
        Undo.RecordObject(generator, "Generate Megabonk Terrain");
        var scene = generator.gameObject.scene;
        var surface = generator.transform.Find("Terrain Blocks");
        var previousMesh = surface && surface.TryGetComponent<MeshFilter>(out var previousFilter)
            ? previousFilter.sharedMesh
            : null;
        generator.Generate(seed);
        if (!generator.HasLayout || generator.Seed != seed) return;
        surface = generator.transform.Find("Terrain Blocks");
        var filter = surface ? surface.GetComponent<MeshFilter>() : null;
        if (filter && filter.sharedMesh && !string.IsNullOrEmpty(scene.path))
        {
            var generatedMesh = filter.sharedMesh;
            if (previousMesh && AssetDatabase.Contains(previousMesh))
            {
                Undo.RecordObject(previousMesh, "Regenerate arena surface");
                EditorUtility.CopySerialized(generatedMesh, previousMesh);
                EditorUtility.SetDirty(previousMesh);
                filter.sharedMesh = previousMesh;
                surface.GetComponent<MeshCollider>().sharedMesh = previousMesh;
                AssetDatabase.SaveAssetIfDirty(previousMesh);
            }
            else
            {
                string path = System.IO.Path.ChangeExtension(scene.path, null) + " Surface.asset";
                AssetDatabase.CreateAsset(generatedMesh, AssetDatabase.GenerateUniqueAssetPath(path));
            }
        }
        generator.GetComponent<MapFeaturePopulator>()?.PopulatePreview();
        EditorUtility.SetDirty(generator);
        EditorUtility.SetDirty(terrainData);
        EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
    }
}
