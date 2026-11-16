using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Survivor/Monsters/Forest Golem")]
    public sealed class ForestGolemData : MonsterRewardData
    {
        [Header("Forest Golem: shockwave, charge and boss skills")]
        public ForestGolemSettings forestGolem = new();
    }
}
