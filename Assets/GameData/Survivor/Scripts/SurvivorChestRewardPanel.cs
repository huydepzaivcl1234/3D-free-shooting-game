using MiningSimulator.Ores;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GameData.Player.Script
{
    public sealed class SurvivorChestRewardPanel : MonoBehaviour
    {
        private const int RayCount = 16;
        private const float RevealSeconds = 0.65f;
        private static readonly Color Ink = new(0.025f, 0.12f, 0.17f);
        private static readonly Color Cream = new(0.97f, 0.94f, 0.86f);
        private static SurvivorChestRewardPanel instance;
        private TMP_FontAsset font;

        private UiPanelCoordinator coordinator;
        private SurvivorDailyLoot loot;
        private SurvivorLootBox box;
        private RectTransform panel;
        private RectTransform raysRoot;
        private RectTransform chest;
        private CanvasGroup reveal;
        private Image[] rays;
        private Image icon;
        private Button action;
        private Button back;
        private TMP_Text actionLabel;
        private TMP_Text backLabel;
        private TMP_Text heading;
        private TMP_Text rewardName;
        private TMP_Text description;
        private TMP_Text price;
        private SupportUpgradeDefinition reward;
        private CursorLockMode previousLock;
        private bool previousCursor;
        private bool committed;
        private bool failed;
        private bool closing;
        private float elapsed;
        private float closeAt;

        public static bool Show(SurvivorDailyLoot loot, SurvivorLootBox box)
        {
            if (!loot || !box || !box.ReadyToOpen || Time.timeScale <= 0f || RunCheckpoint.IsRestoring)
                return false;

            var coordinator = FindFirstObjectByType<UiPanelCoordinator>();
            if (!coordinator || !coordinator.isActiveAndEnabled || coordinator.BlocksGameplay)
                return false;

            if (!instance)
            {
                var canvas = new GameObject("Survivor Chest Modal", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas.SetActive(false);
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.GetComponent<Canvas>().sortingOrder = 180;
                var scaler = canvas.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280, 800);
                // Fit the full panel even on a narrow or short viewport.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                instance = canvas.AddComponent<SurvivorChestRewardPanel>();
                instance.Build();
            }

            instance.Begin(coordinator, loot, box);
            return true;
        }

        private void Begin(UiPanelCoordinator coordinator, SurvivorDailyLoot loot, SurvivorLootBox box)
        {
            this.coordinator = coordinator;
            this.loot = loot;
            this.box = box;
            reward = null;
            committed = failed = closing = false;
            elapsed = 0f;
            chest.anchoredPosition = new Vector2(0f, -68f);
            chest.localScale = Vector3.one;
            reveal.gameObject.SetActive(false);
            raysRoot.gameObject.SetActive(false);
            back.gameObject.SetActive(true);
            back.interactable = true;
            price.gameObject.SetActive(true);
            RefreshText();

            previousLock = Cursor.lockState;
            previousCursor = Cursor.visible;
            gameObject.SetActive(true);
            coordinator.NotifyExternalPanelOpened(panel);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MiningLocalization.LanguageChanged += RefreshText;
            FocusAction();
        }

        private void Update()
        {
            if (!coordinator || !coordinator.IsOpenPanel(panel))
            {
                gameObject.SetActive(false);
                return;
            }

            if (closing)
            {
                if (Time.unscaledTime >= closeAt)
                    gameObject.SetActive(false);
                return;
            }

            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
                Gamepad.current?.buttonEast.wasPressedThisFrame == true || (!committed && (!box || !loot)))
            {
                Close();
                return;
            }

            if (!committed) return;
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / RevealSeconds);
            float rise = Mathf.SmoothStep(0f, 1f, t);
            chest.anchoredPosition = new Vector2(0f, -68f - 74f * rise + 21f * Mathf.Sin(t * Mathf.PI));
            chest.localScale = Vector3.one * (1f - 0.24f * rise + 0.1f * Mathf.Sin(t * Mathf.PI));
            reveal.alpha = rise;
            icon.rectTransform.localScale = Vector3.one * (0.45f + 0.55f * rise + 0.2f * Mathf.Sin(t * Mathf.PI));
            raysRoot.localRotation = Quaternion.Euler(0f, 0f, elapsed * 15f % 360f);
            raysRoot.localScale = Vector3.one * Mathf.Lerp(0.48f, 1f, rise);

            if (t >= 1f && !action.interactable)
            {
                action.interactable = true;
                FocusAction();
            }
        }

        private void PressAction()
        {
            if (closing || !action.interactable) return;
            if (committed)
            {
                Close();
                return;
            }

            if (!loot || !box || !loot.TryOpenFromPanel(box, panel, out SupportUpgradeId id))
            {
                failed = true;
                RefreshText();
                return;
            }

            RevealReward(id);
        }

        private void RevealReward(SupportUpgradeId id)
        {
            committed = true;
            var definitions = PlayerSupportUpgrades.Definitions;
            if ((uint)id < (uint)definitions.Length)
                reward = definitions[(int)id];

            icon.sprite = reward?.Icon;
            icon.enabled = icon.sprite;
            Color tint = reward?.Rarity switch
            {
                SupportUpgradeRarity.Rare => new Color32(101, 199, 255, 255),
                SupportUpgradeRarity.Epic => new Color32(215, 145, 255, 255),
                SupportUpgradeRarity.Legendary => new Color32(255, 213, 108, 255),
                _ => Cream
            };
            heading.color = tint;
            for (int i = 0; i < rays.Length; i++)
            {
                tint.a = i % 2 == 0 ? 0.18f : 0.08f;
                rays[i].color = tint;
            }

            elapsed = 0f;
            reveal.alpha = 0f;
            reveal.gameObject.SetActive(true);
            raysRoot.gameObject.SetActive(true);
            back.gameObject.SetActive(false);
            price.gameObject.SetActive(false);
            RefreshText();
        }

        private void RefreshText()
        {
            backLabel.text = MiningLocalization.Text("Back", "Quay lại");
            if (committed)
            {
                heading.text = reward?.RarityLabel ?? MiningLocalization.Text("Reward", "Phần thưởng");
                rewardName.text = reward?.Name ?? MiningLocalization.Text("Support upgrade received", "Đã nhận nâng cấp bổ trợ");
                description.text = reward?.Description ?? string.Empty;
                actionLabel.text = MiningLocalization.Text("Take", "Nhận").ToUpperInvariant();
                action.interactable = elapsed >= RevealSeconds && !closing;
                return;
            }

            heading.color = Cream;
            heading.text = MiningLocalization.Text("Treasure Chest", "Rương Kho Báu");
            string cost = loot ? loot.PriceLabel + " " + MiningLocalization.Text("gold", "vàng") : string.Empty;
            bool affordable = loot && loot.CanAffordNext;
            price.color = affordable && !failed ? Cream : new Color32(255, 168, 133, 255);
            price.text = !affordable
                ? MiningLocalization.Text("Not enough gold", "Không đủ vàng") + "  ·  " + cost
                : failed ? MiningLocalization.Text("Chest is unavailable", "Rương hiện không mở được") : string.Empty;
            actionLabel.text = MiningLocalization.Text("Open", "Mở").ToUpperInvariant() + "  ·  " + cost;
            action.interactable = affordable && !closing;
        }

        private void Close()
        {
            closing = true;
            action.interactable = false;
            back.interactable = false;
            // Keep the modal registered through the Escape frame so gameplay does not open its pause menu.
            closeAt = Time.unscaledTime + 0.08f;
        }

        private void OnDisable()
        {
            MiningLocalization.LanguageChanged -= RefreshText;
            var events = EventSystem.current;
            if (events && events.currentSelectedGameObject &&
                events.currentSelectedGameObject.transform.IsChildOf(transform))
                events.SetSelectedGameObject(null);

            if (coordinator && coordinator.IsOpenPanel(panel))
            {
                coordinator.NotifyExternalPanelClosed(panel);
                if (!coordinator.BlocksGameplay)
                {
                    Cursor.lockState = previousLock;
                    Cursor.visible = previousCursor;
                }
            }
            coordinator = null;
            loot = null;
            box = null;
        }

        private void FocusAction()
        {
            if (EventSystem.current)
                EventSystem.current.SetSelectedGameObject(action.interactable ? action.gameObject : back.gameObject);
        }

        private void Build()
        {
            font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            var backdrop = MakeImage(transform, "Input Blocker", Vector2.zero, Vector2.zero,
                new Color(0.005f, 0.025f, 0.05f, 0.58f), true);
            backdrop.rectTransform.anchorMin = Vector2.zero;
            backdrop.rectTransform.anchorMax = Vector2.one;
            panel = MakeImage(transform, "Chest Reward", Vector2.zero, new Vector2(600, 638),
                new Color32(181, 169, 148, 255)).rectTransform;
            var body = MakeImage(panel, "Inset", Vector2.zero, new Vector2(590, 628), Ink).rectTransform;
            var art = MakeImage(body, "Reward Stage", new Vector2(0, 39), new Vector2(590, 550), Ink);
            art.gameObject.AddComponent<RectMask2D>();

            raysRoot = MakeRect(art.transform, "Rarity Rays", new Vector2(0, -72), Vector2.zero);
            rays = new Image[RayCount];
            for (int i = 0; i < RayCount; i++)
            {
                var spoke = MakeRect(raysRoot, "Ray Pivot", Vector2.zero, Vector2.zero);
                spoke.localRotation = Quaternion.Euler(0f, 0f, i * 360f / RayCount);
                rays[i] = MakeImage(spoke, "Ray", new Vector2(0, 235), new Vector2(i % 2 == 0 ? 36 : 15, 430), Cream);
            }

            var preview = MakeRect(art.transform, "Chest Model Preview", new Vector2(0, -68), new Vector2(265, 218));
            var previewImage = preview.gameObject.AddComponent<RawImage>();
            previewImage.texture = Resources.Load<Texture2D>("SurvivorChestPreview");
            previewImage.raycastTarget = false;
            chest = preview;
            heading = MakeLabel(art.transform, "Rarity or Title", new Vector2(0, 216), new Vector2(540, 42), 31);
            var rewardRect = MakeRect(art.transform, "Reward Reveal", Vector2.zero, Vector2.zero);
            reveal = rewardRect.gameObject.AddComponent<CanvasGroup>();
            rewardName = MakeLabel(rewardRect, "Reward Name", new Vector2(0, 170), new Vector2(526, 44), 27);
            description = MakeLabel(rewardRect, "Reward Description", new Vector2(0, 117), new Vector2(494, 78), 18);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Truncate;
            description.fontStyle = FontStyles.Normal;
            icon = MakeImage(rewardRect, "Reward Icon", new Vector2(0, -13), new Vector2(153, 124), Color.white);
            icon.preserveAspect = true;
            price = MakeLabel(art.transform, "Price or Status", new Vector2(0, -221), new Vector2(520, 40), 21);

            MakeImage(body, "Footer Divider", new Vector2(0, -237), new Vector2(590, 2), new Color32(181, 169, 148, 255));
            MakeImage(body, "Footer", new Vector2(0, -276), new Vector2(590, 75), new Color32(17, 26, 34, 255));
            action = MakeButton(body, "Chest Action", new Vector2(0, -276), new Vector2(246, 49),
                new Color32(21, 104, 154, 255), out actionLabel);
            back = MakeButton(body, "Back", new Vector2(-225, -276), new Vector2(110, 40),
                new Color32(46, 58, 70, 255), out backLabel);
            action.onClick.AddListener(PressAction);
            back.onClick.AddListener(Close);
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image MakeImage(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, bool raycast = false)
        {
            var image = MakeRect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private TMP_Text MakeLabel(Transform parent, string name, Vector2 position, Vector2 size, float fontSize)
        {
            var label = MakeRect(parent, name, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            if (font) label.font = font;
            label.fontSize = fontSize;
            label.color = Cream;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = fontSize * 0.78f;
            label.fontSizeMax = fontSize;
            label.raycastTarget = false;
            return label;
        }

        private Button MakeButton(Transform parent, string name, Vector2 position, Vector2 size,
            Color color, out TMP_Text label)
        {
            var surface = MakeImage(parent, name, position, size, color, true);
            var button = surface.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            label = MakeLabel(surface.transform, "Label", Vector2.zero, size - new Vector2(8, 4), 21);
            return button;
        }
    }
}

