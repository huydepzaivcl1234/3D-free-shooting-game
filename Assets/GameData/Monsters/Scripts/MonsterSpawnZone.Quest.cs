using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        private QuestNpcEncounter questNpcEncounter;

        private void InitializeQuestEncounter()
        {
            if (!PlayerHuntMode || !player) return;
            if (questNpcEncounter == null)
            {
                var dialogue = Resources.Load<QuestDialogueData>("Quest Dialogue");
                if (!dialogue) return;
                questNpcEncounter = new QuestNpcEncounter(dialogue, player, playerStats, uiCoordinator,
                    audioManager, gameObject.scene, TryFindGround, () => spawnElapsedSeconds, () => SaveWorld());
            }
            questNpcEncounter.Start();
        }
    }
}
