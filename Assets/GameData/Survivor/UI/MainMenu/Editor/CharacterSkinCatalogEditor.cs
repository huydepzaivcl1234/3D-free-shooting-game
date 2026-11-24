using UnityEditor;
using UnityEngine;

namespace MiningSimulator.Ores.Editor
{
    [CustomEditor(typeof(CharacterSkinCatalog))]
    internal sealed class CharacterSkinCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var catalog = (CharacterSkinCatalog)target;
            if (catalog.skins == null) return;
            foreach (var skin in catalog.skins)
            {
                if (!skin) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(skin.displayName, EditorStyles.boldLabel);
                    var data = new SerializedObject(skin);
                    EditorGUILayout.PropertyField(data.FindProperty("displayName"));
                    EditorGUILayout.PropertyField(data.FindProperty("bodyTint"));
                    EditorGUILayout.PropertyField(data.FindProperty("unlockCostGold"), new GUIContent("Gold"));
                    EditorGUILayout.PropertyField(data.FindProperty("unlockCostGems"), new GUIContent("Gems"));
                    data.ApplyModifiedProperties();
                }
            }
        }
    }
}
