using UnityEngine;

namespace MiningSimulator.Ores
{
    public enum MapFeatureTiming { ImmediateOnMapStart, DelayedEvent }

    [CreateAssetMenu(menuName = "Map/Map Feature Rule")]
    public sealed class MapFeatureRule : ScriptableObject
    {
        public string featureName;
        public GameObject prefab;
        [Min(0)] public int spawnCount = 1;
        [Range(0, 3)] public int minTier;
        [Range(0, 3)] public int maxTier = 3;
        public MapFeatureTiming spawnTiming;
        public bool alignToSurfaceNormal;
        public bool managedBySurvivorLoot;
        public bool reservesPortalSpawnArea;
        public LayerMask obstacleLayer = 1;
        [Min(.1f)] public float clearance = 1f;
        [Min(0)] public float spacing = 3f;
    }
}

