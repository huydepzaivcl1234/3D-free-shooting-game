using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using MiningSimulator.Ores;

namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    public sealed class SurvivorUpgradeDebugPanel : MonoBehaviour
    {
        private static readonly Vector2 Center = new(.5f, .5f);
        private static readonly Vector2 TopCenter = new(.5f, 1f);
        private static readonly Color32 PanelColor = new(29, 25, 36, 250);
        private static readonly Color32 RowColor = new(46, 40, 54, 255);
        private static readonly Color32 HeadingColor = new(75, 58, 76, 255);
        private static readonly Color32 GrantColor = new(46, 91, 73, 255);
        private static readonly Color32 MaxColor = new(116, 73, 48, 255);
        private static readonly Color32 TextColor = new(247, 241, 226, 255);
        private const float PanelWidth = 640f;
        private const float PanelHeight = 640f;

        private sealed class Entry
        {
            public string Name;
            public bool IsSupport;
            public SurvivorUpgradeId SurvivorId;
            public SupportUpgradeId SupportId;
            public float AmountPerLevel;
            public TMP_Text Level;
            public Button Grant;
            public Button Max;
        }

        private readonly List<Entry> entries = new();
        private PlayerStats stats;
        private PlayerSupportUpgrades support;
        private SurvivorUpgradeData upgradeData;
        private UiPanelCoordinator coordinator;
        private GameMainMenu mainMenu;
        private RectTransform panel, content;
        private ScrollRect scroll;
        private TMP_FontAsset font;
        private TMP_Text status;
        private bool panelOpen;
        private float previousTimeScale;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;

        internal static void Attach(PlayerStats target)
        {
            if ((!Application.isEditor && !Debug.isDebugBuild) || !target || !target.Support ||
                target.TryGetComponent<SurvivorUpgradeDebugPanel>(out _)) return;
            var health = target.GetComponent<CharacterHealth>();
            if (!health || !health.HealthBar) return;
            var canvas = health.HealthBar.GetComponentInParent<Canvas>(true);
            if (!canvas || canvas.renderMode == RenderMode.WorldSpace) return;

            var debugPanel = target.gameObject.AddComponent<SurvivorUpgradeDebugPanel>();
            debugPanel.Initialize(target, canvas, health);
        }

        private void Initialize(PlayerStats target, Canvas canvas, CharacterHealth health)
        {
            stats = target;
            support = target.Support;
            var cards = FindAnyObjectByType<MiningCardSystem>(FindObjectsInactive.Include);
            upgradeData = cards ? cards.RunUpgradeData : null;
            coordinator = FindFirstObjectByType<UiPanelCoordinator>(FindObjectsInactive.Include);
            mainMenu = FindFirstObjectByType<GameMainMenu>(FindObjectsInactive.Include);
            if (!canvas.TryGetComponent<GraphicRaycaster>(out _))
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            var healthText = health.HealthBar.GetComponentInChildren<TMP_Text>(true);
            if (healthText) font = healthText.font;

            panel = MakeRect(canvas.transform, "Survivor Upgrade Debug Panel", Center, Center, Center,
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            var panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.ignoreParentGroups = true;
            AddImage(panel, PanelColor, true);
            MakeText(MakeRect(panel, "Title", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(18f, -12f), new Vector2(440f, 34f)), "Upgrade Debug", 26f,
                TextAlignmentOptions.MidlineLeft).fontStyle = FontStyles.Bold;
            MakeText(MakeRect(panel, "Shortcut", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(19f, -46f), new Vector2(480f, 24f)), "F8 or Esc to close  ·  Scroll for more upgrades", 15f,
                TextAlignmentOptions.MidlineLeft);
            MakeButton(MakeRect(panel, "Close", Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-12f, -10f), new Vector2(72f, 38f)), "Close", 16f, MaxColor, ClosePanel);

            var scrollRoot = MakeRect(panel, "Upgrade Scroll View", Vector2.up, Vector2.up, Vector2.up,
                new Vector2(12f, -80f), new Vector2(PanelWidth - 24f, PanelHeight - 124f));
            scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 48f;

            var viewport = MakeRect(scrollRoot, "Viewport", Vector2.zero, Vector2.one, Center,
                Vector2.zero, Vector2.zero);
            AddImage(viewport, new Color32(22, 20, 28, 255), true);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            content = MakeRect(viewport, "Upgrade List", new Vector2(0f, 1f), Vector2.one,
                new Vector2(.5f, 1f), Vector2.zero, Vector2.zero);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 5, 5);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            status = MakeText(MakeRect(panel, "Status", Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(16f, 8f), new Vector2(PanelWidth - 32f, 28f)),
                "+1 adds a level. Max sets the level cap. Changes are saved.", 15f,
                TextAlignmentOptions.MidlineLeft);
            status.color = new Color32(218, 204, 185, 255);

            BuildEntries();
            RefreshEntries();
            panel.gameObject.SetActive(false);
            EnsureEventSystem();
        }

        private void BuildEntries()
        {
            AddSection("SURVIVOR UPGRADES");
            foreach (SurvivorUpgradeId id in Enum.GetValues(typeof(SurvivorUpgradeId)))
            {
                if (id == SurvivorUpgradeId.Count) continue;
                var definition = upgradeData ? upgradeData.Find(id) : null;
                if (definition != null && definition.Retired) continue;
                if (definition == null && (id == SurvivorUpgradeId.Magazine || id == SurvivorUpgradeId.ReloadSpeed)) continue;
                string name = definition != null && upgradeData ? upgradeData.Title(id) : FormatName(id.ToString());
                if (string.IsNullOrWhiteSpace(name)) name = FormatName(id.ToString());
                float amount = definition != null && !definition.Ability ? Mathf.Max(.01f, definition.amount) : 1f;
                AddEntry(name, false, id, default, amount);
            }

            AddSection("SUPPORT UPGRADES");
            foreach (var definition in PlayerSupportUpgrades.Definitions)
                AddEntry(definition.Name, true, default, definition.Id, 1f);
        }

        private void AddSection(string title)
        {
            var rect = MakeRect(content, title, Vector2.up, Vector2.one, TopCenter, Vector2.zero, new Vector2(0f, 32f));
            AddImage(rect, HeadingColor);
            var label = MakeText(MakeRect(rect, "Label", Vector2.zero, Vector2.one, Center,
                Vector2.zero, new Vector2(-20f, 0f)), title, 16f, TextAlignmentOptions.MidlineLeft);
            label.fontStyle = FontStyles.Bold;
        }

        private void AddEntry(string name, bool isSupport, SurvivorUpgradeId survivorId,
            SupportUpgradeId supportId, float amountPerLevel)
        {
            var rect = MakeRect(content, name, Vector2.up, Vector2.one, TopCenter, Vector2.zero,
                new Vector2(0f, 44f));
            AddImage(rect, RowColor);
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(10, 7, 4, 4);
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var title = MakeText(MakeRect(rect, "Upgrade Name", Center, Center, Center, Vector2.zero,
                new Vector2(100f, 36f)), name, 18f, TextAlignmentOptions.MidlineLeft);
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var level = MakeText(MakeRect(rect, "Current Level", Center, Center, Center, Vector2.zero,
                new Vector2(74f, 36f)), "Lv 0", 16f, TextAlignmentOptions.Center);
            level.gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
            var entry = new Entry
            {
                Name = name,
                IsSupport = isSupport,
                SurvivorId = survivorId,
                SupportId = supportId,
                AmountPerLevel = amountPerLevel,
                Level = level
            };
            entry.Grant = MakeButton(MakeRect(rect, "Grant One", Center, Center, Center, Vector2.zero,
                new Vector2(64f, 36f)), "+1", 16f, GrantColor, () => GrantOne(entry));
            entry.Grant.gameObject.AddComponent<LayoutElement>().preferredWidth = 64f;
            entry.Max = MakeButton(MakeRect(rect, "Max Upgrade", Center, Center, Center, Vector2.zero,
                new Vector2(76f, 36f)), "MAX", 15f, MaxColor, () => MaxUpgrade(entry));
            entry.Max.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;
            entries.Add(entry);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f8Key.wasPressedThisFrame) TogglePanel();
            else if (panelOpen && keyboard.escapeKey.wasPressedThisFrame) ClosePanel();
        }

        public bool TogglePanel()
        {
            if (!isActiveAndEnabled || !panel) return false;
            if (panelOpen)
            {
                ClosePanel();
                return true;
            }
            if ((coordinator && coordinator.BlocksGameplay) || (mainMenu && mainMenu.IsOpen)) return false;
            OpenPanel();
            return panelOpen;
        }

        private void OpenPanel()
        {
            if (!panel || panelOpen) return;
            panelOpen = true;
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            RefreshEntries();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1f;
            if (coordinator) coordinator.OpenPanel(panel);
            else
            {
                Time.timeScale = 0f;
                panel.gameObject.SetActive(true);
                panel.SetAsLastSibling();
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ClosePanel()
        {
            if (!panelOpen) return;
            panelOpen = false;
            if (coordinator && coordinator.IsOpenPanel(panel)) coordinator.ClosePanel(panel);
            else
            {
                panel.gameObject.SetActive(false);
                if (!coordinator) Time.timeScale = previousTimeScale;
            }
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private void GrantOne(Entry entry) => ApplyLevel(entry, CurrentLevel(entry) + 1);

        private void MaxUpgrade(Entry entry) => ApplyLevel(entry, entry.IsSupport
            ? PlayerSupportUpgrades.LevelCap : SurvivorUpgradeData.LevelCap);

        private void ApplyLevel(Entry entry, int level)
        {
            bool applied = entry.IsSupport
                ? support && support.DebugSetLevel(entry.SupportId, level)
                : stats && stats.DebugSetUpgradeLevel(entry.SurvivorId, level, entry.AmountPerLevel);
            status.text = applied
                ? $"{entry.Name} set to Lv {CurrentLevel(entry)} (saved)."
                : "Could not apply upgrade. Check player health, save access, and run state.";
            RefreshEntries();
        }

        private int CurrentLevel(Entry entry) => entry.IsSupport
            ? support ? support.Level(entry.SupportId) : 0
            : stats ? stats.UpgradeLevel(entry.SurvivorId) : 0;

        private void RefreshEntries()
        {
            foreach (var entry in entries)
            {
                int level = CurrentLevel(entry);
                entry.Level.text = $"Lv {level}";
                bool available = level < (entry.IsSupport ? PlayerSupportUpgrades.LevelCap : SurvivorUpgradeData.LevelCap);
                entry.Grant.interactable = available;
                entry.Max.interactable = available;
            }
        }

        private Button MakeButton(RectTransform rect, string label, float fontSize, Color color,
            UnityEngine.Events.UnityAction onClick)
        {
            var image = AddImage(rect, color, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            MakeText(MakeRect(rect, "Label", Vector2.zero, Vector2.one, Center,
                new Vector2(2f, 1f), new Vector2(-4f, -2f)), label, fontSize, TextAlignmentOptions.Center);
            return button;
        }

        private TMP_Text MakeText(RectTransform rect, string value, float size, TextAlignmentOptions alignment)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            text.fontSize = size;
            text.color = TextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.richText = false;
            text.alignment = alignment;
            text.text = value;
            return text;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static string FormatName(string value)
        {
            var formatted = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                if (i > 0 && char.IsUpper(value[i]) && !char.IsUpper(value[i - 1])) formatted.Append(' ');
                formatted.Append(value[i]);
            }
            return formatted.ToString();
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (!eventSystem)
            {
                var go = new GameObject("Survivor Debug EventSystem", typeof(EventSystem),
                    typeof(InputSystemUIInputModule));
                go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                return;
            }
            if (!eventSystem.TryGetComponent<BaseInputModule>(out _))
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void OnDisable()
        {
            if (panelOpen) ClosePanel();
        }

        private void OnDestroy()
        {
            if (panel) Destroy(panel.gameObject);
        }
    }
}
