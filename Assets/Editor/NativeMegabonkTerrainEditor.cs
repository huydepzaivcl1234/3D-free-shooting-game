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
        generator.Generate(seed);
        if (!generator.HasLayout || generator.Seed != seed) return;
        generator.GetComponent<MapFeaturePopulator>()?.PopulatePreview();
        EditorUtility.SetDirty(generator);
        EditorUtility.SetDirty(terrainData);
        EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
    }
}
