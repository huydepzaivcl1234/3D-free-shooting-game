using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Survivor/Quests/Quest Dialogue")]
    public sealed class QuestDialogueData : ScriptableObject
    {
        public enum QuestGoal { DefeatMonster, SpendStamina, Travel, DealDamage }

        [Serializable]
        public sealed class QuestDefinition
        {
            [HideInInspector] public int id;
            public string label;
            public bool hard;
            public QuestGoal goal;
            public MonsterRewardData monster;
            [Min(1)] public float amount = 5;
            [Min(0), Tooltip("Flat amount added every Quest Scaling Seconds.")]
            public float growthPerStep;
        }

        [HideInInspector] public List<QuestDefinition> additionalQuests = new();
        [SerializeField, HideInInspector] private int nextQuestId = 100;

        public QuestDefinition FindQuest(int id)
        {
            if (id < 100) return null;
            foreach (var quest in additionalQuests)
                if (quest.id == id) return quest;
            return null;
        }

        [Header("Quest NPC")]
        public GameObject questNpcModel;
        public DialogueEditor.NPCConversation questConversation;
        public RuntimeAnimatorController questNpcAnimator;
        public string questNpcIdleState;
        public string questNpcName;
        [Tooltip("Leave empty to use the localized message.")]
        [TextArea] public string noAvailableQuestDialogue;
        [Min(1f), Tooltip("How many letters appear per second during NPC dialogue.")]
        public float questTypingCharactersPerSecond = 18f;
        [Header("Dialogue Audio")]
        public AudioClip questTypingSound;
        public AudioClip choiceClickSound;
        [Header("Dialogue UI")]
        public Sprite questDialogueFrame;
        [Min(0)] public float questMinDistance = 15f;
        [Min(0)] public float questMaxDistance = 20f;
        [Min(.5f)] public float questTalkDistance = 3.5f;
        [Min(1)] public float questScalingSeconds = 180f;
        [Header("Easy quests")]
        public MonsterRewardData questMushroom;
        [Min(1)] public int questMushroomCount = 5;
        [Min(1)] public float questStaminaAmount = 500;
        [Min(1)] public float questEasyDistance = 100;
        [Min(0)] public float easyQuestGrowth = .05f;
        [Header("Hard quests")]
        public MonsterRewardData questGolem;
        [Min(1)] public int questGolemCount = 10;
        [Min(1)] public float questDamageAmount = 1000;
        [Min(1)] public float questHardDistance = 250;
        [Min(0)] public float hardQuestGrowth = .10f;

    }
}
