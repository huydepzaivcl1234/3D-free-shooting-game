using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Map/Lantern Pillar Settings")]
    public sealed class LanternPillarSettings : ScriptableObject
    {
        [Min(0), Tooltip("Target number of lantern pillars generated on each map using this prefab.")]
        public int spawnCount = 8;
        [Min(0f), Tooltip("Minimum center-to-center spacing in meters. Also respects the activation radius.")]
        public float minimumSpacing = 8.1f;
        [Min(.5f)] public float activationRadius = 4f;
        [Min(.1f)] public float chargeSeconds = 3f;

        [Header("Pillar dimensions")]
        [Min(.1f)] public float widthMultiplier = 1.5f;
        [Min(.1f)] public float heightMultiplier = 2.05f;
        public Vector2 sizeVariation = new(.95f, 1.08f);

        [Header("Progress ring")]
        [Min(.1f), Tooltip("Diameter in world meters.")]
        public float progressDiameter = .7f;
        [Range(0f, 1f), Tooltip("Position along the pillar height, measured from its base.")]
        public float progressHeight = .52f;
    }
}

