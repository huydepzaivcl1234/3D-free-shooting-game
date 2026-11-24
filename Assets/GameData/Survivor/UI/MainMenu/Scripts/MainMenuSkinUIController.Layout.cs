using System.Collections.Generic;
using System.Globalization;
using Lean.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    public sealed partial class MainMenuSkinUIController
    {
        private static readonly Color PlayFill = new Color32(165, 221, 68, 255);
        private static readonly Color Amber = new Color32(218, 165, 73, 255);
        private static readonly Color Stone = new Color32(86, 72, 76, 255);
        private static readonly Color PlayInk = new Color32(27, 48, 23, 255);
        private readonly List<UnityEngine.Object> generatedArt = new();
        [System.Serializable]
        private sealed class SkinCard
        {
            public CharacterSkinData skin;
            public Button button;
            public UnityEngine.UI.Image edge, portrait, locked;
            public TMP_Text status;
        }
        [SerializeField] private List<SkinCard> cards = new();
        [SerializeField] private RectTransform frame;
        [SerializeField] private CanvasGroup home, selector, confirmation, currencies;
        [SerializeField] private Button playButton, skinsButton, backButton, actionButton, cancelButton, confirmButton;
        [SerializeField] private Button shopButton, questButton, settingsButton, quitButton, rotateLeft, rotateRight;
        [SerializeField] private TMP_Text actionLabel, skinName, skinDescription, skinStatus, purchaseText, goldText, gemText;
        [SerializeField] private UnityEngine.UI.Image costIcon;
        [SerializeField] private Sprite goldIcon, gemIcon;
        [SerializeField] private UnityEngine.UI.Image selectedSwatch;
        private Sprite bevel, focusOutline;

        private void BindLayout(GameMainMenu menu)
        {
            playButton.onClick.AddListener(menu.Play);
            skinsButton.onClick.AddListener(EnterSkins);
            shopButton.onClick.AddListener(() => menu.OpenShopFor(shopButton));
            questButton.onClick.AddListener(() => menu.OpenQuestFor(questButton));
            settingsButton.onClick.AddListener(menu.OpenSettings);
            quitButton.onClick.AddListener(menu.ExitGame);
            backButton.onClick.AddListener(ExitSkins);
            rotateLeft.onClick.AddListener(() => rotator.Rotate(-35f));
            rotateRight.onClick.AddListener(() => rotator.Rotate(35f));
            actionButton.onClick.AddListener(OnSkinAction);
            confirmButton.onClick.AddListener(ConfirmPurchase);
            cancelButton.onClick.AddListener(CloseConfirmation);
            foreach (var entry in cards)
                entry.button.onClick.AddListener(() => Choose(entry.skin));
        }

#if UNITY_EDITOR
        public bool HasEditableLayout => frame && home && selector;
        public bool IsEditableSkinPageOpen => selector && selector.gameObject.activeSelf;
        public void ApplyEditableHomeDesign() => ApplyHomeDesign();

        public IReadOnlyList<UnityEngine.Object> BuildEditableLayout(GameMainMenu menu)
        {
            if (!frame) BuildLayout(menu);
            ShowPage(false, true);
            return generatedArt;
        }

        public void RebuildEditableLayout(GameMainMenu menu)
        {
            if (frame) UnityEditor.Undo.DestroyObjectImmediate(frame.gameObject);
            UnityEditor.Undo.RecordObject(this, "Bind Just Survival menu");
            cards.Clear();
            generatedArt.Clear();
            BuildLayout(menu);
            UnityEditor.Undo.RegisterCreatedObjectUndo(frame.gameObject, "Build Just Survival menu");
            ShowPage(false, true);
        }

        public void ShowEditablePage(bool showSkins, CharacterSkinData sample = null)
        {
            if (!frame || Application.isPlaying) return;
            ShowPage(showSkins, true);
            if (!showSkins || skins.Length == 0) return;
            if (!sample) sample = skins[0];
            if (!sample) return;
            DisplaySkin(sample, sample.unlockedByDefault, sample.unlockedByDefault);
            actionButton.interactable = false;
            foreach (var card in cards)
                DisplayCard(card, sample, card.skin.unlockedByDefault, card.skin.unlockedByDefault);
        }
#endif

        private void BuildLayout(GameMainMenu menu)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas && canvas.TryGetComponent<CanvasScaler>(out var scaler))
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = .5f;
            }
            frame = Box((RectTransform)transform, "Just Survival UI", 0, 0, 1920, 1080);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, .5f);
            frame.anchoredPosition = Vector2.zero;
            bevel = MakeSprite("Stepped menu frame", 32, 32, (x, y) =>
            {
                int dx = Mathf.Min(x, 31 - x), dy = Mathf.Min(y, 31 - y);
                if (dx < 4 && dy < 4 || dx < 8 && dy < 2 || dx < 2 && dy < 8) return Color.clear;
                if (dx < 6 || dy < 6 || dx < 9 && dy < 9) return style.tray;
                return Color.white;
            }, new Vector4(10, 10, 10, 10));
            focusOutline = MakeSprite("Teal focus outline", 16, 16, (x, y) =>
            {
                int dx = Mathf.Min(x, 15 - x), dy = Mathf.Min(y, 15 - y);
                return dx + dy >= 3 && (dx < 2 || dy < 2 || dx + dy < 6) ? Color.white : Color.clear;
            }, new Vector4(6, 6, 6, 6));

            home = Group(frame, "Main menu");
            selector = Group(frame, "Skin selection");
            currencies = Group(frame, "Wallet");
            confirmation = Group(frame, "Purchase confirmation");

            AddWordmark("MENU_TITLE_FIRST", "JUST", 39);
            AddWordmark("MENU_TITLE_SECOND", "SURVIVAL", 157);
            playButton = NavigationButton("MENU_PLAY", "PLAY", "IconPlay", 0, PlayFill, PlayInk);
            Flat(playButton.targetGraphic.transform, "Play accent", 9, 19, 5, 43, style.accent);
            skinsButton = NavigationButton("MENU_SKINS", "SKINS", "IconSkins", 1, Amber, new Color32(48, 29, 23, 255), 290);
            var badge = Flat(skinsButton.targetGraphic.transform, "New skins badge", 401, 24, 83, 34,
                new Color32(57, 37, 46, 255)).rectTransform;
            Localize(Label(badge, "", 0, 0, 83, 34, 21, style.ink, true), "MENU_BADGE_NEW", "NEW")
                .alignment = TextAlignmentOptions.Center;
            shopButton = NavigationButton("MENU_SHOP", "SHOP", "IconShop", 2, style.card, style.ink);
            questButton = NavigationButton("MENU_QUEST", "QUEST", "IconQuest", 3, style.card, style.ink);
            settingsButton = NavigationButton("MENU_SETTINGS", "SETTINGS", "IconSettings", 4, style.card, style.ink);
            quitButton = NavigationButton("MENU_QUIT", "QUIT", "IconQuit", 5, Stone, style.ink);
            goldIcon = LoadIcon("IconCoin");
            gemIcon = LoadIcon("IconGem");
            goldText = WalletPill(1320, "Gold", goldIcon);
            gemText = WalletPill(1588, "Gems", gemIcon);
            ApplyHomeDesign();
            var lockIcon = LoadIcon("IconLock");

            var drag = Flat(selector.transform, "Turntable drag area", 595, 174, 730, 678, Color.clear);
            drag.raycastTarget = true;
            rotator = drag.gameObject.AddComponent<CharacterTurntableRotator>();
            backButton = Action(selector.transform, "Back", 89, 49, 251, 77, style.card, null, 31);
            Decorate(backButton, "MENU_BACK", "BACK", "IconBack", 23, 20, 33, 71, 171, style.ink);
            var library = Surface(selector.transform, "Skin catalog", 89, 184, 472, 701, style.panel);
            Localize(Label(library, "", 26, 16, 423, 64, 48, style.ink, true), "MENU_SKINS", "SKINS");
            Flat(library, "Divider", 26, 88, 420, 2, style.edge);

            var scrollRoot = Box(library, "Skin scroll", 26, 111, 420, 556);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            var viewport = Flat(scrollRoot, "Viewport", 0, 0, 420, 556, Color.clear).rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var grid = Box(viewport, "Skin cards", 0, 0, 420, Mathf.Max(556, Mathf.CeilToInt(skins.Length / 2f) * 184 - 11));
            scroll.viewport = viewport;
            scroll.content = grid;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36;
            for (int i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                if (!skin) continue;
                var cardButton = Action(grid, string.Empty, i % 2 * 218, i / 2 * 184, 202, 166,
                    new Color32(43, 58, 92, 255), null);
                cardButton.name = skin.displayName;
                var card = cardButton.targetGraphic.rectTransform;
                var edge = Outline(card, 202, 166, style.accent);
                var portrait = Flat(card, "Portrait slot", 18, 16, 166, 85, style.slot).rectTransform;
                var picture = Flat(portrait, "Skin icon", 51, 6, 65, 70, Color.white);
                picture.sprite = skin.cardIcon ? skin.cardIcon : Portrait(skin);
                picture.preserveAspect = true;
                var locked = Flat(portrait, "Locked icon", 135, 5, 19, 19, style.muted);
                locked.sprite = lockIcon;
                Localize(Label(card, "", 5, 103, 192, 29, 23, style.ink, true),
                    SkinKey(skin, "NAME"), skin.displayName).alignment = TextAlignmentOptions.Center;
                var status = Label(card, string.Empty, 5, 135, 192, 21, 17, style.muted, true);
                status.alignment = TextAlignmentOptions.Center;
                cards.Add(new SkinCard { skin = skin, button = cardButton, edge = edge, portrait = picture, locked = locked, status = status });
            }

            rotateLeft = Action(selector.transform, "Rotate left", 784, 937, 63, 62, style.panel, null);
            rotateRight = Action(selector.transform, "Rotate right", 1078, 937, 63, 62, style.panel, null);
            SetIcon(rotateLeft.targetGraphic.transform, "IconLeft", 17, 15, 28);
            SetIcon(rotateRight.targetGraphic.transform, "IconRight", 17, 15, 28);
            var hint = Localize(Label(selector.transform, "", 855, 937, 215, 62, 19, style.ink, true),
                "MENU_SKIN_ROTATE", "DRAG TO ROTATE");
            hint.alignment = TextAlignmentOptions.Center;
            hint.outlineColor = style.tray;
            hint.outlineWidth = .24f;
            hint.fontMaterial.EnableKeyword("OUTLINE_ON");
            generatedArt.Add(hint.fontMaterial);

            var details = Surface(selector.transform, "Skin details", 1350, 264, 480, 507, style.panel);
            selectedSwatch = Flat(details, "Skin swatch", 31, 39, 17, 40, Amber);
            skinStatus = Label(details, "", 63, 34, 383, 48, 22, style.muted, true);
            skinName = Label(details, "", 31, 106, 418, 79, 57, style.ink, true);
            skinName.font = style.headingFont;
            Flat(details, "Divider", 31, 195, 416, 2, style.edge);
            skinDescription = Label(details, "", 31, 221, 413, 112, 28, style.muted, false);
            skinDescription.alignment = TextAlignmentOptions.TopLeft;
            actionButton = Action(details, "", 30, 382, 420, 78, PlayFill, null, 29);
            actionLabel = actionButton.GetComponentInChildren<TMP_Text>(true);
            actionLabel.color = PlayInk;
            costIcon = Flat(actionButton.targetGraphic.transform, "Unlock currency", 22, 24, 30, 30, Color.white);
            costIcon.enabled = false;

            Flat(confirmation.transform, "Modal backdrop", 0, 0, 1920, 1080, new Color(0f, 0f, 0f, .72f)).raycastTarget = true;
            var dialog = Surface(confirmation.transform, "Unlock dialog", 650, 380, 620, 320, style.panel);
            Localize(Label(dialog, "", 30, 25, 560, 61, 42, style.ink, true), "MENU_SKIN_CONFIRM_TITLE", "UNLOCK SKIN?");
            purchaseText = Label(dialog, "", 30, 95, 560, 96, 28, style.muted, false);
            cancelButton = Action(dialog, "CANCEL", 30, 218, 254, 66, style.card, CloseConfirmation);
            confirmButton = Action(dialog, "CONFIRM", 304, 218, 286, 66, PlayFill, ConfirmPurchase);
            cancelButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirmButton };
            Localize(cancelButton.GetComponentInChildren<TMP_Text>(), "MENU_CANCEL", "CANCEL");
            var confirmLabel = confirmButton.GetComponentInChildren<TMP_Text>();
            Localize(confirmLabel, "MENU_CONFIRM", "CONFIRM");
            confirmLabel.color = PlayInk;
            confirmButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancelButton };
            confirmation.gameObject.SetActive(false);
        }

        internal void SetResumeLabel() => SetPlayLabel(true);

        private void SetPlayLabel(bool resume)
        {
            Localize(playButton.GetComponentInChildren<TMP_Text>(), resume ? "MENU_RESUME" : "MENU_PLAY",
                resume ? "RESUME" : "PLAY");
        }

        private static TMP_Text Localize(TMP_Text label, string key, string fallback)
        {
            var binding = label.GetComponent<LeanLocalizedTextMeshProUGUI>();
            if (!binding) binding = label.gameObject.AddComponent<LeanLocalizedTextMeshProUGUI>();
            binding.FallbackText = fallback;
            binding.TranslationName = key;
            binding.UpdateLocalization();
            return label;
        }

        private void ApplyHomeDesign()
        {
            var slate = new Color32(30, 42, 38, 255);
            var ink = new Color32(244, 239, 218, 255);
            var shade = new Color32(17, 23, 22, 255);
            var primary = new Color32(194, 218, 125, 255);
            foreach (var title in home.GetComponentsInChildren<TMP_Text>(true))
            {
                if (title.transform.parent != home.transform ||
                    !title.TryGetComponent<LeanLocalizedTextMeshProUGUI>(out var translation)) continue;
                bool second = translation.TranslationName == "MENU_TITLE_SECOND";
                bool shadow = title.name == "Wordmark shadow" ||
                    title.rectTransform.anchoredPosition.x == 76;
                bool middle = title.name == "Wordmark bevel" ||
                    title.rectTransform.anchoredPosition.x == 73;
                title.name = shadow ? "Wordmark shadow" : middle ? "Wordmark bevel" : "Wordmark ink";
                title.gameObject.SetActive(!middle);
                title.color = shadow ? shade : ink;
                title.outlineColor = shade;
                title.outlineWidth = .24f;
                title.fontSizeMax = second ? 122 : 114;
                title.fontSizeMin = 58;
                Position(title.rectTransform, 98 + (shadow ? 3 : 0),
                    (second ? 229 : 126) + (shadow ? 7 : 0), 660, 132);
            }

            HomeButton(playButton, 153, 430, 550, 98, primary, PlayInk, 48);
            HomeButton(skinsButton, 153, 548, 174, 134, slate, ink, 34,
                "000111000", "001111100", "011111110", "011111110", "010111010",
                "011111110", "010000010", "011111110", "001111100");
            HomeButton(shopButton, 341, 548, 174, 134, slate, ink, 34,
                "001111100", "010000010", "011111110", "011111110", "000000000",
                "011111110", "010000010", "011111110", "001111100");
            HomeButton(questButton, 529, 548, 174, 134, slate, ink, 34,
                "001111110", "001111100", "001111110", "001100000", "001100000",
                "001100000", "011110000", "111111000", "000000000");
            HomeButton(settingsButton, 53, 967, 228, 56, slate, ink, 26);
            HomeButton(quitButton, 295, 967, 162, 56, slate, ink, 26);

            foreach (var value in new[] { goldText, gemText })
            {
                var tile = value.transform.parent;
                tile.GetComponent<UnityEngine.UI.Image>().color = slate;
                var text = value.GetComponent<TMP_Text>();
                text.color = ink;
                tile.Find("Top bevel").GetComponent<UnityEngine.UI.Image>().color = Color.Lerp(slate, ink, .24f);
                tile.Find("Bottom bevel").gameObject.SetActive(false);
            }

            void HomeButton(Button button, float x, float y, float w, float h, Color fill, Color text,
                float fontSize, params string[] glyph)
            {
                var root = (RectTransform)button.transform;
                Position(root, x, y, w, h);
                var shadow = (RectTransform)home.transform.Find(button.name + " shadow");
                Position(shadow, x + 3, y + 5, w, h);
                shadow.GetComponent<UnityEngine.UI.Image>().color = shade;
                var face = button.targetGraphic.rectTransform;
                Position(face, 0, 0, w, h);
                var colors = button.colors;
                colors.normalColor = colors.selectedColor = fill;
                colors.highlightedColor = Color.Lerp(fill, ink, .12f);
                colors.pressedColor = Color.Lerp(fill, shade, .2f);
                button.colors = colors;
                button.targetGraphic.canvasRenderer.SetColor(fill);

                foreach (RectTransform piece in face)
                {
                    if (piece.name.StartsWith("Icon") || piece.name == "New skins badge" ||
                        piece.name == "Play accent" || piece.name == "Bottom bevel")
                        piece.gameObject.SetActive(false);
                    else if (piece.name == "Teal outline")
                        Position(piece, 0, 0, w, h);
                    else if (piece.name == "Top bevel")
                    {
                        Position(piece, 12, 8, w - 24, 2);
                        piece.GetComponent<UnityEngine.UI.Image>().color = Color.Lerp(fill, ink, .25f);
                    }
                    else if (piece.TryGetComponent<TMP_Text>(out var label))
                    {
                        Position(piece, 8, glyph.Length > 0 ? 70 : 0, w - 16, glyph.Length > 0 ? 49 : h);
                        label.alignment = TextAlignmentOptions.Center;
                        label.color = text;
                        label.fontSize = fontSize;
                        label.fontSizeMax = fontSize;
                        label.fontSizeMin = 17;
                    }
                }
                if (glyph.Length > 0 && !face.Find("Home symbol"))
                {
                    const int iconSize = 45;
                    var mark = Flat(face, "Home symbol", (w - iconSize) / 2, 20, iconSize, iconSize, ink);
                    mark.sprite = MakeSprite(button.name + " menu symbol", glyph[0].Length, glyph.Length,
                        (column, row) => glyph[glyph.Length - 1 - row][column] == '1' ? Color.white : Color.clear);
                }
                button.GetComponent<PixelMenuButtonFeedback>().UseMainMenuSounds();
            }

            static void Position(RectTransform rect, float x, float y, float width, float height)
            {
                if (!rect) return;
                rect.anchoredPosition = new Vector2(x, -y);
                rect.sizeDelta = new Vector2(width, height);
            }
        }

        private void AddWordmark(string key, string fallback, float top)
        {
            for (int layer = 2; layer >= 0; layer--)
            {
                var title = Localize(Label(home.transform, "", 70 + layer * 3, top + layer * 6,
                    665, 149, 142, layer == 0 ? new Color32(255, 230, 180, 255) :
                    layer == 1 ? new Color32(226, 165, 108, 255) : style.tray, true), key, fallback);
                title.font = style.headingFont;
                title.alignment = TextAlignmentOptions.Center;
                title.outlineColor = style.tray;
                title.extraPadding = true;
                title.outlineWidth = .34f;
                title.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, .21f);
                title.fontMaterial.EnableKeyword("OUTLINE_ON");
                generatedArt.Add(title.fontMaterial);
            }
        }

        private Button NavigationButton(string key, string fallback, string icon, int index, Color face, Color ink, float width = 386)
        {
            var button = Action(home.transform, fallback, 106, 363 + index * 99, 510, 82, face, null, 40);
            Decorate(button, key, fallback, icon, 29, 21, 40, 106, width, ink);
            return button;
        }

        private void Decorate(Button button, string key, string fallback, string icon,
            float x, float y, float size, float textX, float width, Color ink)
        {
            SetIcon(button.targetGraphic.transform, icon, x, y, size);
            var label = Localize(button.GetComponentInChildren<TMP_Text>(), key, fallback);
            label.color = ink;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.rectTransform.anchoredPosition = new Vector2(textX, 0);
            label.rectTransform.sizeDelta = new Vector2(width, label.rectTransform.sizeDelta.y);
        }

        private static Sprite LoadIcon(string name)
        {
#if UNITY_EDITOR
            string path = name switch
            {
                "IconCoin" => "Assets/Art/UI/Shell/Coin.png",
                "IconGem" => "Assets/Ores/Icons/Items/GemCurrencyIcon.png",
                _ => "Assets/GameData/Survivor/UI/MainMenu/Diorama/Approved/" + name + ".png"
            };
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
#else
            return null;
#endif
        }

        private static void SetIcon(Transform parent, string icon, float x, float y, float size)
        {
            var image = Flat(parent, icon, x, y, size, size, Color.white);
            image.sprite = LoadIcon(icon);
            image.preserveAspect = true;
        }

        private TMP_Text WalletPill(float x, string name, Sprite icon)
        {
            var pill = Surface(currencies.transform, name, x, 49, 244, 90, style.panel);
            var glyph = Flat(pill, name + " icon", 22, 22, 46, 46, Color.white);
            glyph.sprite = icon;
            glyph.preserveAspect = true;
            var number = Label(pill, "", 90, 0, 137, 90, 41, style.ink, true);
            number.text = MiningMoneyFormatter.Format(0);
            return number;
        }

        private Sprite Portrait(CharacterSkinData skin)
        {
            return MakeSprite(skin.displayName + " portrait", 24, 26, (x, y) =>
            {
                float distance = new Vector2(x - 11.5f, y - Mathf.Clamp(y, 7f, 17f)).magnitude;
                if (distance > 8.1f) return Color.clear;
                if (distance > 7.2f || (y >= 15 && y <= 17 && (x == 8 || x == 15)) ||
                    (y == 12 && x >= 9 && x <= 14) || (y == 13 && (x == 9 || x == 14))) return style.tray;
                return x >= 15 ? skin.bodyTint * new Color(.76f, .76f, .76f, 1f) : skin.bodyTint;
            });
        }

        private void RefreshWallet()
        {
            if (!goldText) return;
            goldText.text = MiningMoneyFormatter.Format(wallet.CurrentMoney);
            gemText.text = MiningMoneyFormatter.Format(wallet.CurrentGems);
        }

        private void Choose(CharacterSkinData skin)
        {
            selected = skin;
            collection.ApplySkin(skin, menuRenderers);
            diorama.Punch();
            RefreshSkin();
        }

        private void RefreshSkin()
        {
            if (!selected || !skinName || !selectedSwatch) return;
            foreach (var card in cards)
                DisplayCard(card, selected, collection.IsOwned(card.skin), collection.Equipped == card.skin);

            bool equipped = collection.Equipped == selected;
            bool owned = collection.IsOwned(selected);
            DisplaySkin(selected, owned, equipped);
            actionButton.interactable = !equipped && (owned || collection.CanBuy(selected));
            actionLabel.color = actionButton.interactable ? PlayInk : style.muted;
            if (confirmation.gameObject.activeSelf)
            {
                confirmButton.interactable = collection.CanBuy(selected);
                purchaseText.text = Format("MENU_SKIN_CONFIRM_DETAIL", "{0} / {1}", skinName.text, Cost(selected));
            }
        }

        private void DisplayCard(SkinCard card, CharacterSkinData highlighted, bool owned, bool equipped)
        {
            card.edge.enabled = card.skin == highlighted;
            card.locked.enabled = !owned;
            card.status.text = Status(owned, equipped);
            card.status.color = equipped ? style.accent : style.muted;
        }

        private void DisplaySkin(CharacterSkinData skin, bool owned, bool equipped)
        {
            skinName.text = MiningLocalization.TextKey(SkinKey(skin, "NAME"), skin.displayName);
            skinDescription.text = MiningLocalization.TextKey(SkinKey(skin, "DESCRIPTION"), skin.description);
            skinStatus.text = Status(owned, equipped);
            skinStatus.color = equipped ? style.accent : style.muted;
            selectedSwatch.color = skin.bodyTint;
            actionLabel.text = equipped ? Status(true, true) : owned
                ? MiningLocalization.TextKey("MENU_SKIN_EQUIP", "EQUIP")
                : Format("MENU_SKIN_UNLOCK", "UNLOCK · {0}", Cost(skin));
            SetCostIcon(skin, !owned);
        }

        private static string SkinKey(CharacterSkinData skin, string field) =>
            "MENU_SKIN_" + (skin.skinId ?? string.Empty).ToUpperInvariant() + "_" + field;

        private static string Status(bool owned, bool equipped) => equipped
            ? MiningLocalization.TextKey("MENU_SKIN_EQUIPPED", "EQUIPPED")
            : owned ? MiningLocalization.TextKey("MENU_SKIN_OWNED", "OWNED")
                : MiningLocalization.TextKey("MENU_SKIN_LOCKED", "LOCKED");

        private static string Format(string key, string fallback, params object[] values) =>
            string.Format(CultureInfo.CurrentCulture, MiningLocalization.TextKey(key, fallback), values);

        private void SetCostIcon(CharacterSkinData skin, bool locked)
        {
            costIcon.enabled = locked && (skin.unlockCostGold > 0 ^ skin.unlockCostGems > 0);
            costIcon.sprite = skin.unlockCostGold > 0 ? goldIcon : gemIcon;
            var labelRect = actionLabel.rectTransform;
            labelRect.anchoredPosition = new Vector2(costIcon.enabled ? 73 : 12, 0);
            labelRect.sizeDelta = new Vector2(costIcon.enabled ? 334 : 396, 78);
            actionLabel.alignment = costIcon.enabled ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        }

        private static string Cost(CharacterSkinData skin)
        {
            if (skin.unlockCostGold > 0 && skin.unlockCostGems > 0)
                return Format("MENU_COST_BOTH", "{0:N0} GOLD + {1:N0} GEMS", skin.unlockCostGold, skin.unlockCostGems);
            if (skin.unlockCostGold > 0) return Format("MENU_COST_GOLD", "{0:N0} GOLD", skin.unlockCostGold);
            if (skin.unlockCostGems > 0) return Format("MENU_COST_GEMS", "{0:N0} GEMS", skin.unlockCostGems);
            return MiningLocalization.TextKey("MENU_COST_FREE", "FREE");
        }

        private void OnSkinAction()
        {
            if (!selected || selected == collection.Equipped) return;
            if (collection.IsOwned(selected)) collection.Equip(selected, false);
            else if (collection.CanBuy(selected))
            {
                selector.interactable = false;
                selector.blocksRaycasts = false;
                confirmation.gameObject.SetActive(true);
                RefreshSkin();
                Focus(cancelButton);
            }
        }

        private void ConfirmPurchase()
        {
            if (!collection.Equip(selected, true)) { RefreshSkin(); return; }
            CloseConfirmation();
            Focus(backButton);
        }

        private void CloseConfirmation()
        {
            if (!confirmation || !confirmation.gameObject.activeSelf) return;
            confirmation.gameObject.SetActive(false);
            selector.interactable = selector.blocksRaycasts = true;
            if (page == Page.SkinSelect) Focus(actionButton.interactable ? actionButton : backButton);
        }

        private void ShowPage(bool isSkins, bool instant = false)
        {
            CanvasGroup shown = isSkins ? selector : home;
            CanvasGroup hidden = isSkins ? home : selector;
            shown.gameObject.SetActive(true);
            hidden.gameObject.SetActive(false);
            shown.interactable = shown.blocksRaycasts = true;
            shown.alpha = instant ? 1f : 0f;
        }

        private void FitAndFade()
        {
            var bounds = ((RectTransform)transform).rect;
            float scale = Mathf.Min(bounds.width / 1920f, bounds.height / 1080f);
            frame.localScale = Vector3.one * Mathf.Max(.01f, scale);
            var shown = page == Page.SkinSelect ? selector : page == Page.Quests ? questPage : home;
            shown.alpha = Mathf.MoveTowards(shown.alpha, 1f, Time.unscaledDeltaTime / .16f);
        }

        private CanvasGroup Group(Transform parent, string name)
        {
            return Box(parent, name, 0, 0, 1920, 1080).gameObject.AddComponent<CanvasGroup>();
        }

        private RectTransform Surface(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            AddShadow(parent, name, x, y, w, h);
            var image = Flat(parent, name, x, y, w, h, color);
            image.sprite = bevel;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            BevelEdges(image.transform, w, h, color);
            return image.rectTransform;
        }

        private void AddShadow(Transform parent, string name, float x, float y, float w, float h)
        {
            var shadow = Flat(parent, name + " shadow", x, y + 7, w, h, style.tray);
            shadow.sprite = bevel;
            shadow.type = UnityEngine.UI.Image.Type.Sliced;
        }

        private void BevelEdges(Transform parent, float w, float h, Color face)
        {
            Color top = face == PlayFill ? new Color32(228, 250, 137, 255) :
                face == Amber ? new Color32(255, 215, 133, 255) :
                face == Stone ? new Color32(139, 119, 121, 255) : style.edge;
            Color bottom = face == PlayFill ? new Color32(78, 138, 37, 255) :
                face == Amber ? new Color32(144, 96, 53, 255) :
                face == Stone ? new Color32(61, 50, 60, 255) : style.tray;
            Flat(parent, "Top bevel", 16, 6, w - 32, 4, top);
            Flat(parent, "Bottom bevel", 16, h - 10, w - 32, 4, bottom);
        }

        private UnityEngine.UI.Image Outline(Transform parent, float w, float h, Color color)
        {
            var outline = Flat(parent, "Teal outline", 0, 0, w, h, color);
            outline.sprite = focusOutline;
            outline.type = UnityEngine.UI.Image.Type.Sliced;
            outline.enabled = false;
            return outline;
        }

        private Button Action(Transform parent, string title, float x, float y, float w, float h, Color color,
            UnityEngine.Events.UnityAction invoke, float fontSize = 32)
        {
            string name = title.Length > 0 ? title : "Action";
            AddShadow(parent, name, x, y, w, h);
            var hit = Flat(parent, name, x, y, w, h, Color.clear);
            hit.raycastTarget = true;
            var image = Flat(hit.transform, "Button face", 0, 0, w, h, Color.white);
            image.sprite = bevel;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            BevelEdges(image.transform, w, h, color);
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, style.accent, .15f);
            colors.selectedColor = color;
            colors.pressedColor = Color.Lerp(color, style.accent, .18f);
            colors.disabledColor = style.tray;
            colors.fadeDuration = .12f;
            button.colors = colors;
            image.color = Color.white;
            var label = Label(image.transform, title, 12, 0, w - 24, h, fontSize, style.ink, true);
            label.fontSizeMin = Mathf.Min(fontSize, 24);
            label.alignment = TextAlignmentOptions.Center;
            var ring = Outline(image.transform, w, h, style.accent);
            hit.gameObject.AddComponent<PixelMenuButtonFeedback>().Configure(button, image.rectTransform, ring);
            return button;
        }

        private TMP_Text Label(Transform parent, string value, float x, float y, float w, float h,
            float size, Color color, bool pixel)
        {
            var label = Box(parent, "Label", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = (pixel ? style.Pixel : style.bodyFont) ?? TMP_Settings.defaultFontAsset;
            label.fontSize = size;
            label.fontSizeMin = size * .75f;
            label.fontSizeMax = size;
            label.enableAutoSizing = true;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.text = string.Empty;
            return label;
        }

        private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static UnityEngine.UI.Image Flat(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var image = Box(parent, name, x, y, w, h).gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Sprite MakeSprite(string name, int width, int height, System.Func<int, int, Color> pixel, Vector4 border = default)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                texture.SetPixel(column, row, pixel(column, row));
            texture.Apply(false, false);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), Vector2.one * .5f, 100f, 0,
                SpriteMeshType.FullRect, border);
            generatedArt.Add(texture);
            generatedArt.Add(sprite);
            return sprite;
        }

        private void ReleaseArt()
        {
            foreach (var asset in generatedArt) Destroy(asset);
        }
    }
}
