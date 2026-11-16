using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class SupportUpgradeHud : MonoBehaviour
    {
        private const int MaxStripIcons = 6;
        private const float StripHeight = 31f;
        private const float BottomMargin = 5f;
        private const float VitalsGap = 4f;
        private const float PanelHeight = 292f;
        private const float PanelWidth = 404f;
        private static readonly Vector2 BottomCenter = new(.5f, 0f);
        private static readonly Vector2 TopLeft = new(0f, 1f);
        private static readonly Vector2 TopRight = Vector2.one;
        private static readonly Color PanelColor = new Color32(36, 23, 41, 249);
        private static readonly Color Gold = new Color32(223, 183, 116, 255);
        private static readonly Color Cream = new Color32(255, 245, 220, 255);
        private static readonly Color Subtle = new Color32(220, 203, 212, 255);

        private sealed class IconView
        {
            public Image Image;
            public TMP_Text Count;

            public void Set(SupportUpgradeDefinition definition, int level)
            {
                Image.sprite = definition.Icon;
                Count.text = $"x{level}";
            }
        }

        private sealed class UpgradeRow
        {
            public RectTransform Rect;
            public IconView Icon;
            public TMP_Text Rarity;
            public TMP_Text Name;
            public TMP_Text Description;
        }

        private readonly List<SupportUpgradeId> recent = new();
        private readonly Queue<(SupportUpgradeId id, int level)> notices = new();
        private readonly IconView[] slots = new IconView[MaxStripIcons];
        private readonly List<UpgradeRow> rows = new();
        private PlayerSupportUpgrades state;
        private RectTransform vitals, vitalsVisual, root, strip, panel, notice;
        private ScrollRect scroll;
        private RectTransform list;
        private Image noticeIcon;
        private TMP_Text heading, noticeText;
        private TMP_FontAsset font;
        private UiPanelCoordinator coordinator;
        private GameMainMenu mainMenu;
        private float noticeRemaining;
        private SupportUpgradeId noticeId;
        private int noticeLevel;
        private int owned;
        private float vitalsLift;
        private bool mountedInPlayHud;

        public static void Attach(PlayerSupportUpgrades state)
        {
            if (!state || state.TryGetComponent<SupportUpgradeHud>(out _)) return;
            if (!state.TryGetComponent<CharacterHealth>(out var health) ||
                health.HealthBar is not RectTransform vitals || !vitals) return;
            var canvas = vitals.GetComponentInParent<Canvas>(true);
            if (!canvas || canvas.renderMode == RenderMode.WorldSpace) return;

            var hud = state.gameObject.AddComponent<SupportUpgradeHud>();
            hud.Initialize(state, vitals);
        }

        internal static void MountPlayHud(PlayerSupportUpgrades state, RectTransform parent)
        {
            Attach(state);
            if (!state.TryGetComponent<SupportUpgradeHud>(out var ui) || !ui.root) return;
            if (ui.vitalsLift > 0f) ui.vitals.anchoredPosition -= Vector2.up * ui.vitalsLift;
            ui.vitalsLift = 0f;
            ui.mountedInPlayHud = true;
            ui.root.SetParent(parent, false);
            ui.root.anchorMin = ui.root.anchorMax = BottomCenter;
            ui.root.anchoredPosition = new Vector2(0f, 24f);
            ui.panel.anchoredPosition = new Vector2(0f, StripHeight + 9f);
            ui.SyncVisibility();
            ui.PositionNotice();
        }

        private void Initialize(PlayerSupportUpgrades upgrades, RectTransform bar)
        {
            state = upgrades;
            vitals = bar;
            vitalsVisual = vitals.Find("Styled Micro Bar") as RectTransform;
            if (!vitalsVisual) vitalsVisual = vitals;
            var healthLabel = vitals.GetComponentInChildren<TMP_Text>(true);
            if (healthLabel) font = healthLabel.font;
            coordinator = FindFirstObjectByType<UiPanelCoordinator>(FindObjectsInactive.Include);
            mainMenu = FindFirstObjectByType<GameMainMenu>(FindObjectsInactive.Include);

            root = MakeRect(vitals, "Support Upgrades HUD", BottomCenter, BottomCenter,
                new Vector2(0f, -StripHeight - VitalsGap), new Vector2(vitals.rect.width, StripHeight));
            strip = MakeRect(root, "Upgrade icon strip", BottomCenter, BottomCenter, Vector2.zero, root.sizeDelta);
            var horizontal = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.spacing = 5;
            horizontal.childControlWidth = horizontal.childControlHeight = false;
            horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = false;
            for (int i = 0; i < slots.Length; i++)
                slots[i] = MakeIcon(strip, $"Upgrade slot {i + 1}", Vector2.zero, Vector2.zero,
                    Vector2.zero, new Vector2(52, 31));

            var more = MakeRect(strip, "All support upgrades", Vector2.zero, Vector2.zero,
                Vector2.zero, new Vector2(30, 27));
            MakeButton(more, "...", 17, TogglePanel);

            BuildPanel();
            BuildNotice();
            state.Granted += OnGranted;
            MiningLocalization.LanguageChanged += Refresh;
            Refresh();
        }

        private void BuildPanel()
        {
            panel = MakeRect(root, "Support upgrade list", BottomCenter, BottomCenter,
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            AddPanelBackground(panel);
            heading = MakeText(MakeRect(panel, "Heading", TopLeft, TopLeft,
                new Vector2(12, -7), new Vector2(PanelWidth - 50, 26)), "", 18, TextAlignmentOptions.MidlineLeft);
            MakeButton(MakeRect(panel, "Close", TopRight, TopRight, new Vector2(-8, -6),
                new Vector2(27, 26)), "×", 20, TogglePanel);

            var listArea = MakeRect(panel, "Scroll view", TopLeft, TopLeft, new Vector2(8, -39),
                new Vector2(PanelWidth - 16, PanelHeight - 47));
            scroll = listArea.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 38f;
            var viewport = MakeRect(listArea, "Viewport", TopLeft, TopLeft, Vector2.zero, listArea.sizeDelta);
            viewport.gameObject.AddComponent<RectMask2D>();
            AddImage(viewport, PanelColor, true);
            scroll.viewport = viewport;
            list = MakeRect(viewport, "Owned upgrades", TopLeft, TopLeft, Vector2.zero, viewport.sizeDelta);
            scroll.content = list;
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 4;
            layout.childControlWidth = layout.childControlHeight = false;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.gameObject.SetActive(false);
        }

        private void BuildNotice()
        {
            notice = MakeRect(root, "Support upgrade received", BottomCenter, BottomCenter,
                Vector2.zero, new Vector2(330, 46));
            AddPanelBackground(notice);
            noticeIcon = AddImage(MakeRect(notice, "Received icon", new Vector2(0, .5f),
                new Vector2(0, .5f), new Vector2(6, 0), new Vector2(59, 36)), Color.white);
            noticeIcon.preserveAspect = true;
            noticeText = MakeText(MakeRect(notice, "Received text", new Vector2(0, .5f),
                new Vector2(0, .5f), new Vector2(72, 0), new Vector2(250, 40)), "", 17,
                TextAlignmentOptions.MidlineLeft);
            noticeText.textWrappingMode = TextWrappingModes.Normal;
            notice.gameObject.SetActive(false);
        }

        private void OnGranted(SupportUpgradeId id)
        {
            recent.Remove(id);
            recent.Insert(0, id);
            notices.Enqueue((id, state.Level(id)));
            Refresh();
        }

        private void Refresh()
        {
            var definitions = PlayerSupportUpgrades.Definitions;
            owned = 0;
            foreach (var id in recent) AddOwned(definitions[(int)id]);
            foreach (var definition in definitions)
                if (!recent.Contains(definition.Id)) AddOwned(definition);

            for (int i = 0; i < slots.Length; i++)
                slots[i].Image.gameObject.SetActive(i < owned);
            for (int i = owned; i < rows.Count; i++)
                rows[i].Rect.gameObject.SetActive(false);

            if (panel.gameObject.activeInHierarchy) SizeOwnedRows();
            heading.text = MiningLocalization.TextKey("SUPPORT_UPGRADES_HEADING", "Support Upgrades");
            if (noticeRemaining > 0f) RefreshNoticeText();
            SyncVisibility();
        }

        private void AddOwned(SupportUpgradeDefinition definition)
        {
            int level = state.Level(definition.Id);
            if (level <= 0) return;

            if (owned < slots.Length) slots[owned].Set(definition, level);
            if (owned >= rows.Count) rows.Add(BuildRow());
            var row = rows[owned++];
            row.Rect.gameObject.SetActive(true);
            row.Icon.Set(definition, level);
            row.Name.text = definition.Name;
            row.Rarity.text = definition.RarityLabel;
            row.Rarity.color = RarityColor(definition.Rarity);
            row.Description.text = definition.Description;
        }

        private void SizeOwnedRows()
        {
            // TMP initializes its text layout when the panel first becomes active.
            for (int i = 0; i < owned; i++)
            {
                var row = rows[i];
                float descHeight = Mathf.Ceil(row.Description.preferredHeight) + 4f;
                row.Description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, descHeight);
                row.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(64f, 38f + descHeight + 7f));
            }
        }

        private UpgradeRow BuildRow()
        {
            const float rowWidth = PanelWidth - 16;
            const float textWidth = rowWidth - 92;
            var row = MakeRect(list, "Owned upgrade", TopLeft, TopLeft, Vector2.zero, new Vector2(rowWidth, 70));
            AddImage(row, new Color32(59, 40, 59, 255));
            var icon = MakeIcon(row, "Icon", TopLeft, TopLeft, new Vector2(8, -9), new Vector2(65, 41));
            var name = MakeText(MakeRect(row, "Name", TopLeft, TopLeft, new Vector2(82, -3),
                new Vector2(textWidth, 22)), "", 16, TextAlignmentOptions.MidlineLeft);
            var rarity = MakeText(MakeRect(row, "Rarity", TopLeft, TopLeft, new Vector2(82, -24),
                new Vector2(textWidth, 15)), "", 12, TextAlignmentOptions.MidlineLeft);
            var description = MakeText(MakeRect(row, "Description", TopLeft, TopLeft,
                new Vector2(82, -38), new Vector2(textWidth, 18)), "", 13, TextAlignmentOptions.TopLeft);
            description.color = Subtle;
            description.textWrappingMode = TextWrappingModes.Normal;
            return new UpgradeRow { Rect = row, Icon = icon, Rarity = rarity, Name = name, Description = description };
        }

        private void TogglePanel()
        {
            bool open = !panel.gameObject.activeSelf;
            panel.gameObject.SetActive(open);
            if (open)
            {
                SizeOwnedRows();
                LayoutRebuilder.ForceRebuildLayoutImmediate(list);
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            PositionNotice();
        }

        private void SyncVisibility()
        {
            bool blocked = (coordinator && coordinator.BlocksGameplay) || (mainMenu && mainMenu.IsOpen);
            bool visible = isActiveAndEnabled && owned > 0 && !blocked &&
                (mountedInPlayHud || vitals.gameObject.activeInHierarchy);

            if (root.gameObject.activeSelf != visible)
            {
                root.gameObject.SetActive(visible);
                if (!visible) ClosePanel();
            }

            if (mountedInPlayHud) return;

            // The styled bar is offset inside the vitals container.
            float visualBottom = GetVitalsVisualY(vitalsVisual.rect.yMin);
            float stripY = visualBottom - StripHeight - VitalsGap;
            if (!Mathf.Approximately(root.anchoredPosition.y, stripY))
                root.anchoredPosition = new Vector2(0f, stripY);

            float home = vitals.anchoredPosition.y - vitalsLift;
            float lift = visible ? Mathf.Max(0, BottomMargin - home - stripY) : 0f;
            if (!Mathf.Approximately(lift, vitalsLift))
            {
                vitals.anchoredPosition += Vector2.up * (lift - vitalsLift);
                vitalsLift = lift;
            }
            float panelY = GetVitalsVisualY(vitalsVisual.rect.yMax) + 6f - stripY;
            if (!Mathf.Approximately(panel.anchoredPosition.y, panelY))
            {
                panel.anchoredPosition = new Vector2(0, panelY);
                PositionNotice();
            }
        }

        private float GetVitalsVisualY(float localY)
        {
            var world = vitalsVisual.TransformPoint(new Vector3(0f, localY));
            return vitals.InverseTransformPoint(world).y - vitals.rect.yMin;
        }

        private void LateUpdate()
        {
            if (!root) return;
            SyncVisibility();
            if (!root.gameObject.activeInHierarchy) return;
            if (noticeRemaining > 0f)
            {
                noticeRemaining -= Time.unscaledDeltaTime;
                if (noticeRemaining > 0f) return;
            }
            if (notices.Count == 0)
            {
                if (notice.gameObject.activeSelf) notice.gameObject.SetActive(false);
                return;
            }

            var (id, level) = notices.Dequeue();
            var definition = PlayerSupportUpgrades.Definitions[(int)id];
            noticeIcon.sprite = definition.Icon;
            noticeId = id;
            noticeLevel = level;
            RefreshNoticeText();
            noticeText.color = RarityColor(definition.Rarity);
            noticeRemaining = 1.6f;
            notice.gameObject.SetActive(true);
        }

        private void RefreshNoticeText()
        {
            var definition = PlayerSupportUpgrades.Definitions[(int)noticeId];
            noticeText.text = string.Format(
                MiningLocalization.TextKey("SUPPORT_UPGRADES_RECEIVED", "Received {0}  x{1}"),
                definition.Name, noticeLevel);
        }

        private void ClosePanel()
        {
            panel.gameObject.SetActive(false);
            PositionNotice();
        }

        private void PositionNotice()
        {
            notice.anchoredPosition = panel.anchoredPosition + Vector2.up *
                (panel.gameObject.activeSelf ? PanelHeight + 5f : 0f);
        }

        private void OnDisable()
        {
            if (root) root.gameObject.SetActive(false);
            if (panel) ClosePanel();
            if (vitals && vitalsLift > 0f) vitals.anchoredPosition -= Vector2.up * vitalsLift;
            vitalsLift = 0f;
        }

        private void OnEnable()
        {
            if (root) Refresh();
        }

        private void OnDestroy()
        {
            if (state) state.Granted -= OnGranted;
            MiningLocalization.LanguageChanged -= Refresh;
            if (root) Destroy(root.gameObject);
        }

        private IconView MakeIcon(RectTransform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var rect = MakeRect(parent, name, anchor, pivot, position, size);
            var image = AddImage(rect, Color.white);
            image.preserveAspect = true;
            var count = MakeRect(rect, "Count", Vector2.right, Vector2.right, Vector2.zero, new Vector2(26, 15));
            AddImage(count, PanelColor);
            var textRect = MakeRect(count, "Count label", new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                Vector2.zero, count.sizeDelta);
            return new IconView { Image = image, Count = MakeText(textRect, "", 13, TextAlignmentOptions.Center) };
        }

        private void MakeButton(RectTransform rect, string label, float size, UnityEngine.Events.UnityAction onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = AddImage(rect, new Color32(72, 48, 70, 255), true);
            button.onClick.AddListener(onClick);
            MakeText(MakeRect(rect, "Label", Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, rect.sizeDelta),
                label, size, TextAlignmentOptions.Center);
        }

        private static void AddPanelBackground(RectTransform rect)
        {
            AddImage(rect, PanelColor);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Gold;
            outline.effectDistance = new Vector2(1, -1);
        }

        private TMP_Text MakeText(RectTransform rect, string value, float size, TextAlignmentOptions alignment)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) label.font = font;
            label.fontSize = size;
            label.color = Cream;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.richText = false;
            label.alignment = alignment;
            label.text = value;
            return label;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Color RarityColor(SupportUpgradeRarity rarity) => rarity switch
        {
            SupportUpgradeRarity.Rare => new Color32(101, 199, 255, 255),
            SupportUpgradeRarity.Epic => new Color32(215, 145, 255, 255),
            SupportUpgradeRarity.Legendary => new Color32(255, 213, 108, 255),
            _ => Cream
        };
    }
}
