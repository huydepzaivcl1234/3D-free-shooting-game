using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName="Survivor/Game Data/Gameplay Performance")]
    public sealed class GameplayPerformanceData : ScriptableObject
    {
        [Tooltip("Khoảng vẽ camera thế giới, mét. 0 giữ camera gốc. AI ngoài camera tuân theo Freeze Offscreen Simulation.")]
        [Min(0)] public float renderDistance=120f;
        [Header("Camera-frustum presentation / Ngoài khung camera")]
        public bool cullOffscreenActors = true;
        [Tooltip("Ẩn pose ngoài camera. Khi Freeze bật sẽ dừng animation; khi Freeze tắt vẫn giữ state machine.")]
        public bool cullOffscreenAnimation = true;
        [Tooltip("Đứng yên ngoài camera: dừng AI, animation và vật lý; tự khôi phục khi nhìn thấy lại.")]
        public bool freezeOffscreenSimulation = true;
        [Min(0)] public float actorBoundsPadding = .2f;
        [Tooltip("Luôn giữ mô phỏng cho đối tượng trong bán kính này quanh người chơi, kể cả ngoài camera.")]
        [Min(0)] public float nearbySimulationRadius = 24f;
        [Header("Procedural terrain")]
        [Tooltip("Số ô địa hình mỗi cạnh chunk render. 0 dùng mesh hiển thị liền.")]
        [Range(0, 10)] public int terrainRenderChunkTiles = 2;
        [Header("Monster CPU / Tải AI quái")]
        [Tooltip("Giây giữa các lần tính đường/né vật cản. Di chuyển, đòn đánh và collider vẫn xử lý mỗi frame. 0 = tính mỗi frame như cũ.")]
        [Min(0)] public float monsterSteeringInterval=.05f;
        [Tooltip("Ngân sách tìm đường mỗi frame (ms). Công việc còn lại tiếp tục frame sau; 0 bỏ giới hạn. Một phép dò hoặc callback có thể vượt ngân sách.")]
        [Min(0)] public float navigationBudgetMilliseconds=2f;
        [Min(0)] public float distantSteeringDistance=12;
        [Min(0)] public float distantSteeringInterval=.15f;
        public float SteeringInterval(float distance)=>monsterSteeringInterval<=0?0:distance>distantSteeringDistance?Mathf.Max(monsterSteeringInterval,distantSteeringInterval):monsterSteeringInterval;
        [Tooltip("Poll ordinary distant locomotion less often. Movement remains per frame; combat, special monsters and nearby actors always use full logic.")]
        public bool simplifyDistantAI=true;
        [Min(.02f)] public float distantBrainInterval=.1f;
        [Header("Crowd visuals")]
        public bool enableCrowdVisuals = true;
        [Min(0)] public float crowdVisualDistance = 26f;
        [Header("Crowd physics")]
        [Tooltip("Apply local steering separation to ordinary enemies. Hard Enemy-to-Enemy contacts are disabled in the layer collision matrix.")]
        public bool softCrowdSeparation=true;
        [Tooltip("Extra ground-detail cells sampled on demand per frame. Zero uses the navigation grid heights only.")]
        [Range(0, 64)] public int crowdGroundProbeCellsPerFrame = 8;
        [Tooltip("Maximum height in metres an ordinary GPU crowd enemy can climb on nearby enemies. Zero disables climbing.")]
        [Min(0)] public float crowdMaximumClimbHeight = 1.8f;
        [Min(0)] public float crowdSeparationStrength=12;
        [Min(0)] public float crowdPadding=.03f;
        private static GameplayPerformanceData current;
        private static bool loaded;
        public static GameplayPerformanceData Current
        {
            get
            {
                if(!loaded){current=Resources.Load<GameplayPerformanceData>("GameplayPerformanceData");loaded=true;}
                return current;
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache(){current=null;loaded=false;}
    }
}
