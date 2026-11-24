using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Survivor/Character Skin")]
    public sealed class CharacterSkinData : ScriptableObject
    {
        public string skinId;
        public string displayName;
        [TextArea] public string description;
        [Range(1, 5)] public int rarityStars = 1;
        public Sprite cardIcon;
        public Color bodyTint = Color.white;
        [Min(0)] public int unlockCostGold;
        [Min(0)] public int unlockCostGems;
        public bool unlockedByDefault;
    }
}
