using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    public sealed partial class MonsterSpawnZone
    {
        private float overtimeCredit;
        private Canvas stageCanvas;
        private TMP_Text stageTimer, shrinePrompt;
        private Camera stageCamera;
        private GameMainMenu stageMenu;
        private MonsterSpawnEntry overtimeEntry;
        private int lastDisplayedSeconds = -1;
        private bool displayedOvertime;
        private MiningLanguage displayedLanguage;

        public bool IsOvertime => spawnSettings && spawnElapsedSeconds >= spawnSettings.stageSeconds;

        private void InitializeStage()
        {
            if (!PlayerHuntMode || !spawnSettings) return;
            stageMenu = FindAnyObjectByType<GameMainMenu>(FindObjectsInactive.Include);
            stageCamera = Camera.main;
            var hud = new GameObject("Survivor Stage HUD", typeof(Canvas), typeof(CanvasScaler));
            hud.transform.SetParent(transform, false);
            stageCanvas = hud.GetComponent<Canvas>();
            stageCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            stageCanvas.sortingOrder = 95;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.matchWidthOrHeight = .5f;
            stageTimer = CreateStageLabel("Stage timer", -22, 240, 62, 31);
            shrinePrompt = CreateStageLabel("God Boss prompt", -92, 520, 38, 21);
        }

        private TMP_Text CreateStageLabel(string name, float y, float width, float height, float fontSize)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            root.SetParent(stageCanvas.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, 1);
            root.anchoredPosition = new Vector2(0,y);
            root.sizeDelta = new Vector2(width,height);
            var background = root.GetComponent<Image>();
            background.color = new Color(.06f,.08f,.11f,.87f);
            background.raycastTarget = false;
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(root, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private void LateUpdate()
        {
            if (!stageCanvas) return;
            stageCanvas.enabled = !stageMenu || !stageMenu.IsOpen;
            if (!stageCanvas.enabled) return;
            bool overtime = IsOvertime;
            int seconds = overtime
                ? Mathf.FloorToInt(spawnElapsedSeconds - spawnSettings.stageSeconds)
                : Mathf.CeilToInt(spawnSettings.stageSeconds - spawnElapsedSeconds);
            var language = MiningLocalization.CurrentLanguage;
            if (seconds != lastDisplayedSeconds || overtime != displayedOvertime || language != displayedLanguage)
            {
                lastDisplayedSeconds = seconds;
                displayedOvertime = overtime;
                displayedLanguage = language;
                string time = $"{seconds / 60:00}:{seconds % 60:00}";
                stageTimer.text = overtime ? MiningLocalization.TextKey("SURVIVOR_OVERTIME", "OVERTIME") + "  +" + time : time;
                stageTimer.color = overtime ? new Color(1,.56f,.42f) : new Color(1,.89f,.65f);
            }

            bool showPrompt = FindNearbyShrine() != null && !(uiCoordinator && uiCoordinator.BlocksGameplay);
            shrinePrompt.transform.parent.gameObject.SetActive(showPrompt);
            if (showPrompt)
                shrinePrompt.text = godBoss
                    ? MiningLocalization.TextKey("SURVIVOR_GOD_ALREADY_ACTIVE", "God Boss is already active")
                    : MiningLocalization.TextKey("SURVIVOR_SUMMON_GOD", "[E]  Summon God Boss");
        }

        private void TickOvertime(float dt)
        {
            if (!IsOvertime || !spawnSettings.overtimeEnemy || dt <= 0) return;
            float minutes = (spawnElapsedSeconds - spawnSettings.stageSeconds) / 60f;
            float interval = Mathf.Max(Mathf.Max(.1f, spawnSettings.overtimeMinimumInterval),
                spawnSettings.overtimeInterval - minutes * spawnSettings.overtimeIntervalReductionPerMinute);
            overtimeCredit += dt;
            if (overtimeCredit < interval) return;
            overtimeCredit = 0;
            var entry = GetOvertimeEntry();
            if (entry == null) return;

            int count = Mathf.Max(1, spawnSettings.overtimeBatchSize);
            for (int i = 0; i < count; i++)
                if (!SpawnOne(entry, overtime: true)) break;
        }

        private MonsterSpawnEntry GetOvertimeEntry()
        {
            if (!spawnSettings || !spawnSettings.overtimeEnemy) return null;
            if (overtimeEntry?.prefab != spawnSettings.overtimeEnemy)
                overtimeEntry = monsters.Find(candidate => candidate?.prefab == spawnSettings.overtimeEnemy)
                    ?? new MonsterSpawnEntry { prefab = spawnSettings.overtimeEnemy };
            return overtimeEntry;
        }

        private bool TryGetOvertimeCandidate(out Vector3 position)
        {
            float min = spawnSettings.overtimeMinDistance;
            float max = Mathf.Max(min, spawnSettings.overtimeMaxDistance);
            float angle = Random.Range(0, 2 * Mathf.PI);
            float radius = Mathf.Sqrt(Random.Range(min * min, max * max));
            position = player.transform.position + new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)) * radius;
            return true;
        }

        private bool IsOffscreen(Vector3 position)
        {
            if (!stageCamera) return true;
            var screen = stageCamera.WorldToViewportPoint(position + Vector3.up * 1.5f);
            return screen.z <= 0 || screen.x < -.03f || screen.x > 1.03f || screen.y < -.03f || screen.y > 1.03f;
        }
    }
}
