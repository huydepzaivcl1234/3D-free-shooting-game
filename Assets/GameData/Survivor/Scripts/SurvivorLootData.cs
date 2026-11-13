using UnityEngine;
namespace GameData.Player.Script
{
    [CreateAssetMenu(menuName="Survivor/Daily Loot")]
    public sealed class SurvivorLootData : ScriptableObject
    {
        public GameObject prefab;
        [Tooltip("Number of loot boxes placed on the map when a run starts.")]
        [Range(0, 50)] public int startingBoxCount = 7;
        [Header("Sparkling box — Anime Star VFX")]
        public GameObject shinyVfx;
        [Min(.01f)] public float shinyVfxScale=.65f;
        public Vector3 shinyVfxOffset=new(0,.55f,0);
        [Min(1)] public float minimumDistance=7,maximumDistance=10;
        [Range(0,100)] public float shinyChancePercent=10;
        [Min(0)] public float shinyAbilityBonusPercent=20;
        [Min(.1f)] public float pickupRadius=1.6f;
        [Min(0)] public float pickupDelay=.75f;
        public LayerMask groundLayers=~0;
        private void OnValidate(){minimumDistance=Mathf.Max(1,minimumDistance);maximumDistance=Mathf.Max(minimumDistance,maximumDistance);}
    }
}
