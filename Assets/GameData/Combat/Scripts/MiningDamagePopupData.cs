using UnityEngine;
namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName="Survivor/Game Data/Damage Popup")]
    public sealed class MiningDamagePopupData : ScriptableObject
    {
        public MiningDamagePopupSettings settings=new();
    }
}
