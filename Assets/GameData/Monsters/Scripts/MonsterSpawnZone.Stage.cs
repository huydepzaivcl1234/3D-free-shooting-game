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
        private WorldProximityLabel chestWorldLabel, shrineWorldLabel;
        private MonsterSpawnEntry overtimeEntry;
        private int lastDisplayedSeconds = -1;
        private bool displayedOvertime;
        private MiningLanguage displayedLanguage;

        internal float OvertimeMinutes => Mathf.Max(0f, (spawnElapsedSeconds - CurrentStageDuration) / 60f);
        internal static readonly MonsterSpawnSettings.GhostSettings DefaultGhostSettings = new();
        internal MonsterSpawnSettings.GhostSettings GhostTuning => (spawnSettings ? spawnSettings.ghost : null) ?? DefaultGhostSettings;
        internal float GhostControlCap => GhostTuning.controlCapSeconds *
            (1f - Mathf.Clamp01((OvertimeMinutes - GhostTuning.surgeAtMinute) / Mathf.Max(.01f, GhostTuning.controlDecayMinutes)));
        internal float GhostSurgeSpeed => OvertimeMinutes >= GhostTuning.surgeAtMinute ? GhostTuning.surgeSpeedMultiplier : 1f;

        public bool IsOvertime => spawnSettings && !StageRunComplete && spawnElapsedSeconds >= CurrentStageDuration;

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
            shrinePrompt = CreateStageLabel("Stage portal prompt", -92, 520, 38, 21);
            chestWorldLabel = new WorldProximityLabel(transform, "Chest price (world)");
            shrineWorldLabel = new WorldProximityLabel(transform, "Boss shrine (world)");
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
            if (!stageCanvas.enabled || RunCheckpoint.IsRestoring || restoringWorld)
            {
                chestWorldLabel.Hide();
                shrineWorldLabel.Hide();
                return;
            }
            TickWorldPrompts();
            bool overtime = IsOvertime;
            int seconds = overtime
                ? Mathf.FloorToInt(spawnElapsedSeconds - CurrentStageDuration)
                : Mathf.Max(0, Mathf.CeilToInt(CurrentStageDuration - spawnElapsedSeconds));
            var language = MiningLocalization.CurrentLanguage;
            if (seconds != lastDisplayedSeconds || overtime != displayedOvertime || language != displayedLanguage)
            {
                lastDisplayedSeconds = seconds;
                displayedOvertime = overtime;
                displayedLanguage = language;
                string time = $"{seconds / 60:00}:{seconds % 60:00}";
                string stage = MiningLocalization.Text("STAGE", "MÀN") + $" {CurrentStage}";
                stageTimer.text = StageRunComplete
                    ? MiningLocalization.Text("RUN COMPLETE", "HOÀN THÀNH")
                    : stage + (overtime ? "  +" : "  ") + time;
                stageTimer.color = overtime ? new Color(1,.56f,.42f) : Color.white;
            }

            string prompt = StagePortalPrompt();
            shrinePrompt.transform.parent.gameObject.SetActive(prompt != null);
            if (prompt != null && shrinePrompt.text != prompt) shrinePrompt.text = prompt;
        }

        private void TickWorldPrompts()
        {
            if (!stageCamera || !stageCamera.isActiveAndEnabled) stageCamera = Camera.main;
            if (!stageCamera)
            {
                chestWorldLabel.Hide();
                shrineWorldLabel.Hide();
                return;
            }

            var chest = stageLoot ? stageLoot.NearbyChest() : null;
            if (chest)
                chestWorldLabel.Show(chest.transform, stageCamera, stageLoot.WorldPriceText(),
                    stageLoot.CanAffordNext ? new Color32(255, 218, 125, 255) : new Color32(255, 142, 121, 255));
            else chestWorldLabel.Hide();

            var shrine = !IsOvertime && !StageRunComplete && !RuneStation.PlayerUsesRuneTime
                ? FindNearbyShrine() : null;
            if (shrine != null)
                shrineWorldLabel.Show(shrine.instance.transform, stageCamera,
                    godBoss ? MiningLocalization.Text("Boss active", "Boss đang hoạt động") :
                        MiningLocalization.Text("Summon Boss", "Triệu hồi Boss"),
                    new Color32(255, 226, 179, 255));
            else shrineWorldLabel.Hide();
        }

        private void TickOvertime(float dt)
        {
            if (!IsOvertime || !spawnSettings.overtimeEnemy || dt <= 0) return;
            float minutes = OvertimeMinutes;
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
