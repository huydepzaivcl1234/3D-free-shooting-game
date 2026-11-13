using System.Collections.Generic;
using DialogueEditor;
using MiningSimulator.Ores;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(QuestDialogueData))]
public sealed class QuestDialogueDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Edit NPC, audio, and quest settings in Dialogue Editor.", MessageType.Info);
        var settings = (QuestDialogueData)target;
        using (new EditorGUI.DisabledScope(!settings.questConversation || EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Open Quest Dialogue"))
                DialogueEditor.DialogueEditorWindow.ShowWindow().LoadNewAsset(settings.questConversation);
        }
    }

    private const string QuestChoice = "QuestChoice";
    private static bool showNpc = true, showPlacement, showBuiltIn, showCustom = true;
    private static readonly string[] BuiltInLabels =
    {
        "Easy / Defeat Mushroom", "Easy / Spend stamina", "Easy / Travel",
        "Hard / Defeat Golem", "Hard / Deal damage", "Hard / Travel"
    };

    private static QuestDialogueData SettingsFor(NPCConversation conversation)
    {
        var settings = Resources.Load<QuestDialogueData>("Quest Dialogue");
        return settings && conversation == settings.questConversation ? settings : null;
    }

    internal static bool IsQuestGraph(NPCConversation conversation) => SettingsFor(conversation) != null;

    internal static void DrawGraphSettings(NPCConversation conversation)
    {
        var settings = SettingsFor(conversation);
        if (!settings) return;
        using var serialized = new SerializedObject(settings);
        showNpc = EditorGUILayout.Foldout(showNpc, "NPC name & dialogue audio", true);
        if (showNpc)
        {
            EditorGUILayout.PropertyField(serialized.FindProperty("questNpcName"), new GUIContent("NPC Name", "Default speaker name. A Speech node can override it."));
            EditorGUILayout.PropertyField(serialized.FindProperty("questTypingSound"), new GUIContent("Typing Sound"));
            EditorGUILayout.PropertyField(serialized.FindProperty("choiceClickSound"), new GUIContent("Choice Sound"));
            DrawFields(serialized, "questTypingCharactersPerSecond", "noAvailableQuestDialogue", "questDialogueFrame");
        }
        showPlacement = EditorGUILayout.Foldout(showPlacement, "NPC model & map placement", true);
        if (showPlacement)
        {
            DrawFields(serialized, "questNpcModel", "questNpcAnimator", "questNpcIdleState", "questTalkDistance");
            EditorGUILayout.HelpBox("For a fixed NPC, place one empty GameObject on the ground in the map and add Quest NPC Anchor. Maps without an anchor use the random distances below.", MessageType.Info);
            EditorGUILayout.LabelField("Random placement", EditorStyles.boldLabel);
            DrawFields(serialized, "questMinDistance", "questMaxDistance");
        }
        showBuiltIn = EditorGUILayout.Foldout(showBuiltIn, "Built-in quests & scaling", true);
        if (showBuiltIn)
        {
            DrawFields(serialized, "questScalingSeconds", "questMushroom", "questMushroomCount",
                "questStaminaAmount", "questEasyDistance", "easyQuestGrowth", "questGolem", "questGolemCount",
                "questDamageAmount", "questHardDistance", "hardQuestGrowth");
        }
        showCustom = EditorGUILayout.Foldout(showCustom, "Custom quests", true);
        if (showCustom)
        {
            var quests = serialized.FindProperty("additionalQuests");
            EditorGUILayout.HelpBox("Add a quest, then connect an Option node under the Easy or Hard branch and pick it in the Quest dropdown.", MessageType.Info);
            for (int i = 0; i < quests.arraySize; i++)
            {
                var entry = quests.GetArrayElementAtIndex(i);
                var label = entry.FindPropertyRelative("label");
                entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded,
                    string.IsNullOrWhiteSpace(label.stringValue) ? "Unnamed quest" : label.stringValue, true);
                if (!entry.isExpanded) continue;
                EditorGUILayout.PropertyField(label, new GUIContent("Tracker title"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("hard"), new GUIContent("Hard (Ability card)"));
                var goal = entry.FindPropertyRelative("goal");
                EditorGUILayout.PropertyField(goal);
                if (goal.enumValueIndex == (int)QuestDialogueData.QuestGoal.DefeatMonster)
                    EditorGUILayout.PropertyField(entry.FindPropertyRelative("monster"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("amount"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("growthPerStep"));
            }

            if (GUILayout.Button("Add quest"))
            {
                var id = serialized.FindProperty("nextQuestId");
                var entry = quests.GetArrayElementAtIndex(quests.arraySize++);
                entry.FindPropertyRelative("id").intValue = id.intValue++;
                entry.FindPropertyRelative("label").stringValue = "New quest";
                entry.FindPropertyRelative("hard").boolValue = false;
                entry.FindPropertyRelative("goal").enumValueIndex = (int)QuestDialogueData.QuestGoal.Travel;
                entry.FindPropertyRelative("monster").objectReferenceValue = null;
                entry.FindPropertyRelative("amount").floatValue = 100;
                entry.FindPropertyRelative("growthPerStep").floatValue = 0;
                entry.isExpanded = true;
            }
        }
        if (serialized.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(settings);
        EditorGUILayout.Space();
    }

    internal static void DrawQuestOption(NPCConversation conversation, EditableOptionNode node)
    {
        var settings = SettingsFor(conversation);
        if (!settings) return;
        if (conversation.GetParameter(QuestChoice) is not EditableIntParameter)
        {
            EditorGUILayout.HelpBox("Add an int parameter named QuestChoice to bind quest options.", MessageType.Warning);
            return;
        }
        var action = node.ParamActions.Find(item => item.ParameterName == QuestChoice) as EditableSetIntParamAction;
        int current = action?.Value ?? -1;
        var labels = new List<string> { "None (dialogue only)" };
        var ids = new List<int> { -1 };
        for (int i = 0; i < BuiltInLabels.Length; i++)
        {
            labels.Add(BuiltInLabels[i]);
            ids.Add(i);
        }
        foreach (var quest in settings.additionalQuests)
        {
            labels.Add((quest.hard ? "Hard / " : "Easy / ") + quest.label + " (#" + quest.id + ")");
            ids.Add(quest.id);
        }
        if (!ids.Contains(current))
        {
            labels.Add("Missing quest #" + current);
            ids.Add(current);
        }
        int selected = ids[EditorGUILayout.Popup("Quest", ids.IndexOf(current), labels.ToArray())];
        if (selected == current) return;
        if (selected < 0) node.ParamActions.Remove(action);
        else
        {
            if (action == null)
            {
                action = new EditableSetIntParamAction(QuestChoice);
                node.ParamActions.Add(action);
            }
            action.Value = selected;
        }
    }

    private static void DrawFields(SerializedObject serialized, params string[] names)
    {
        foreach (string name in names) EditorGUILayout.PropertyField(serialized.FindProperty(name), true);
    }
}
