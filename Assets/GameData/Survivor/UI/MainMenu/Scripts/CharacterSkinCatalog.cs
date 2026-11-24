using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Survivor/Character Skin Catalog")]
    public sealed class CharacterSkinCatalog : ScriptableObject
    {
        public CharacterSkinData[] skins;
    }
}
