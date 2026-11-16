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
        private GameData.Player.Script.SurvivorDailyLoot stageLoot;
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
            stageLoot = FindAnyObjectByType<GameData.Player.Script.SurvivorDailyLoot>(FindObjectsInactive.Include);
            var hud = new GameObject("Survivor Stage HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            hud.transform.SetParent(transform, false);
            stageCanvas = hud.GetComponent<Canvas>();
            stageCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            stageCanvas.sortingOrder = 95;
            var scaler = hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920,1080);
            scaler.matchWidthOrHeight = .5f;
            stageTimer = CreateStageLabel("Stage timer", -24, 300, 59, 38, false);
            shrinePrompt = CreateStageLabel("God Boss prompt", -92, 520, 38, 21);
            hud.AddComponent<SurvivorPlayHud>().Initialize(this, player, wallet, (RectTransform)stageCanvas.transform);
            stageTimer.transform.parent.SetAsLastSibling();
            shrinePrompt.transform.parent.SetAsLastSibling();
        }

        private TMP_Text CreateStageLabel(string name, float y, float width, float height, float fontSize, bool showBackground = true)
        {
            var root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(stageCanvas.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, 1);
            root.anchoredPosition = new Vector2(0,y);
            root.sizeDelta = new Vector2(width,height);
            if (showBackground)
            {
                var background = root.gameObject.AddComponent<Image>();
                background.color = new Color(.06f,.08f,.11f,.87f);
                background.raycastTarget = false;
            }
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(root, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.sizeDelta = Vector2.zero;
            label.font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            if (!showBackground)
            {
                label.fontStyle = FontStyles.Bold;
                label.outlineColor = new Color32(12, 18, 26, 255);
                label.outlineWidth = .22f;
            }
            return label;
        }

        private void LateUpdate()
        {
            if (!stageCanvas) return;
            stageCanvas.enabled = (!stageMenu || !stageMenu.IsOpen) && (!uiCoordinator || !uiCoordinator.BlocksGameplay);
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
                stageTimer.color = overtime ? new Color(1,.56f,.42f) : Color.white;
            }

            string prompt = stageLoot ? stageLoot.ChestPrompt() : null;
            if (prompt == null && FindNearbyShrine() != null)
            {
                prompt = godBoss
                    ? MiningLocalization.TextKey("SURVIVOR_GOD_ALREADY_ACTIVE", "God Boss is already active")
                    : MiningLocalization.TextKey("SURVIVOR_SUMMON_GOD", "[E]  Summon God Boss");
            }
            shrinePrompt.transform.parent.gameObject.SetActive(prompt != null);
            if (prompt != null && shrinePrompt.text != prompt) shrinePrompt.text = prompt;
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
