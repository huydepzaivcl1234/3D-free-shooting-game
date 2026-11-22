using GameData.Player.Script;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Unity.VectorGraphics;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    internal sealed class RustUpgradeLayout : MonoBehaviour
    {
        private const float Width = 1440f, Height = 810f;
        internal static readonly Color Teal = C(0x3DE6D2);
        private static readonly Color Back = C(0x0E0C0B), Panel = C(0x171412), Tray = C(0x100E0C);
        private static readonly Color Card = C(0x201C19), Well = C(0x2C2723), Line = C(0x3B3029);
        private static readonly Color Ink = C(0xF1E3CB), Muted = C(0xB8AFA3), Shadow = C(0x090706);
        private static readonly Color ButtonFill = C(0x2C2520), ButtonEdge = C(0x584A3E);
        private static readonly Color[] Rarity = { C(0x8A97A6), C(0x4FA36E), C(0x4C86D6), C(0x9B6BD1), C(0xD9A441) };
        private static readonly int[] RarityTagWidths = { 73, 94, 49, 47, 91 };

        private RectTransform design;
        private TMP_FontAsset body, pixel;
        private Texture2D dither, hazard;
        private readonly UpgradeCardView[] cards = new UpgradeCardView[3];
        private bool descriptionsDirty;

        internal void Build(UpgradePanelView view, TMP_FontAsset fallback, Material backdrop)
        {
            if (design) return;
            body = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            if (!body) body = fallback;
            var font = Resources.Load<Font>("SurvivorPixelify");
            if (font) pixel = TMP_FontAsset.CreateFontAsset(font);

            var root = (RectTransform)transform;
            foreach (Transform child in root) child.gameObject.SetActive(false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            if (TryGetComponent<Image>(out var oldImage)) oldImage.enabled = false;
            if (TryGetComponent<Outline>(out var oldOutline)) oldOutline.enabled = false;

            var dimmer = Flat(root, "Live backdrop", 0, 0, 0, 0, new Color(Back.r, Back.g, Back.b, .65f));
            Stretch(dimmer.rectTransform);
            dimmer.raycastTarget = true;
            if (backdrop && backdrop.shader.isSupported &&
                UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                    is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset { supportsCameraOpaqueTexture: true })
            {
                dimmer.material = backdrop;
                dimmer.color = Color.white;
            }

            design = Rect(root, "Rust and Charcoal", 0, 0, Width, Height);
            design.anchorMin = design.anchorMax = design.pivot = new Vector2(.5f, .5f);
            design.anchoredPosition = Vector2.zero;
            Fit();
            Surface(design, "Window shadow", 24, 24, 1398, 768, Shadow, Shadow, Shadow, 6, 0);
            Surface(design, "Window outer frame", 21, 21, 1398, 768, Panel, C(0x0B0807), C(0x0B0807), 6, 2);
            Surface(design, "Window inner frame", 25, 25, 1390, 760, Panel, C(0x4A423A), C(0x4A423A), 5, 1);
            Flat(design, "Header light", 33, 27, 1374, 10, C(0x231D18));
            Flat(design, "Bottom shade", 33, 774, 1374, 8, C(0x120D0B));
            dither = Tile("Panel dither", 4, 4, (x, y) => (x / 2 + y / 2) % 2 == 0 ? new Color(.42f,.36f,.30f,.025f) : Color.clear);
            Pattern(design, "Dither", 29, 29, 1382, 752, dither);
            hazard = Tile("Teal hazard rail", 10, 4, (x, y) => (x + 3 - y) % 10 < 4 ? Teal : C(0x241C16));
            Pattern(design, "Hazard rail", 55, 177, 240, 4, hazard);
            foreach (float x in new[]{29f, 1408f})
                foreach (float y in new[]{29f, 778f}) Flat(design, "Rivet", x, y, 3, 3, C(0x77695E));

            Surface(design, "Recessed card tray", 38, 184, 1356, 532, Tray, C(0x44382F), C(0x0A0706), 5, 2, true);
            Label(design, "Mode", "SURVIVOR / THE SANCTUARY", 55, 50, 400, 22, 14, Muted, bold:true);
            view.title = Label(design, "Title", "", 55, 79, 820, 69, 52, Ink, heading:true);
            Label(design, "Subtitle", "One choice. A stronger run.", 55, 145, 460, 26, 18, Muted);
            view.queueCount = Label(design, "Queued cards", "", 1140, 145, 236, 28, 14, Muted, TextAlignmentOptions.MidlineRight, bold:true);
            view.close = Control(design, "Close", 1354, 47, 34, 32, "×", out _);
            BuildOwned(view);

            view.cardsRoot = Rect(design, "Offers", 0, 0, Width, Height);
            view.slots = new UpgradePanelView.OfferSlot[3];
            for (int i = 0; i < view.slots.Length; i++)
            {
                cards[i] = BuildCard(view.cardsRoot, i);
                view.slots[i] = cards[i].bindings;
            }
            Flat(design, "Footer divider", 50, 726, 1338, 1, Line);
            view.later = Control(design, "Later", 53, 748, 164, 33, "LATER", out var later);
            later.alignment = TextAlignmentOptions.MidlineLeft;
            later.margin = new Vector4(15, 0, 0, 0);
            Label((RectTransform)view.later.transform, "Esc", "Esc", 104, 0, 43, 33, 13, Muted, TextAlignmentOptions.MidlineRight);
            Label(design, "Keys", "1–3", 420, 748, 42, 33, 17, Teal, bold:true);
            Label(design, "Hint", "Choose an offer", 463, 748, 250, 33, 15, Ink);
            view.transparent = null;
            view.transparentLabel = null;
            view.sceneryCover = null;
        }

        internal Button BuildReroll(out TMP_Text label)
        {
            return Control(design, "Lootbox reroll", 1190, 748, 187, 33, "", out label);
        }

        private void BuildOwned(UpgradePanelView view)
        {
            Raised(design, "Your build", 48, 192, 341, 512, C(0x1C1714), C(0x57493D), 4);
            Raised(design, "Level badge", 65, 209, 39, 34, ButtonFill, ButtonEdge, 3);
            view.playerLevel = Label(design, "Player level", "", 65, 209, 39, 34, 20, Ink, TextAlignmentOptions.Center, bold:true);
            Label(design, "Build title", "YOUR BUILD", 116, 208, 230, 24, 18, Ink, heading:true);
            Label(design, "Combat mode", "SURVIVOR / AUTOMATIC ATTACKS", 116, 232, 245, 16, 11, Muted, bold:true);
            Flat(design, "Stats divider", 67, 263, 303, 1, Line);
            Label(design, "Current stats", "CURRENT STATS", 67, 278, 220, 20, 13, Muted, bold:true);
            view.damage = Stat("Damage", 309);
            view.attackSpeed = Stat("Attack speed", 340);
            view.attackRange = Stat("Attack range", 371);
            view.projectileSpeed = Stat("Projectile speed", 402);
            Flat(design, "Owned divider", 67, 450, 303, 1, Line);
            Label(design, "Owned cards", "OWNED CARDS", 67, 473, 190, 25, 17, Ink, heading:true);
            view.ownedCount = Label(design, "Owned count", "", 284, 475, 81, 20, 12, Muted, TextAlignmentOptions.MidlineRight, bold:true);
            var scroll = Rect(design, "Owned scroll", 66, 503, 302, 174).gameObject.AddComponent<ScrollRect>();
            Flat(scroll.transform, "Scroll target", 0, 0, 302, 174, Color.clear).raycastTarget = true;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 42;
            scroll.viewport = Rect(scroll.transform, "Viewport", 0, 0, 302, 174);
            scroll.viewport.gameObject.AddComponent<RectMask2D>();
            view.ownedContent = Rect(scroll.viewport, "Owned list", 0, 0, 302, 0);
            scroll.content = view.ownedContent;
            var group = view.ownedContent.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = 2;
            group.childControlHeight = false;
            group.childControlWidth = false;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;
            view.ownedContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.ownedEmpty = Label(view.ownedContent, "Empty", "Your journey begins here.", 0, 0, 302, 36, 15, Muted);
            var row = view.ownedRowTemplate = Rect(view.ownedContent, "Owned row template", 0, 0, 302, 41);
            Flat(row, "Icon slot", 0, 3, 32, 32, Well);
            Icons(row, 2, 5, 28, out _, out _);
            Label(row, "Name", "", 44, 0, 171, 38, 17, Ink);
            Label(row, "Level", "", 216, 0, 84, 38, 14, Muted, TextAlignmentOptions.MidlineRight);
            Flat(row, "Divider", 44, 38, 256, 1, Line);
            row.gameObject.SetActive(false);
        }

        private TMP_Text Stat(string name, float y)
        {
            Label(design, name, name, 69, y, 190, 25, 16, Muted);
            return Label(design, name + " value", "", 247, y, 117, 25, 17, Ink, TextAlignmentOptions.MidlineRight, bold:true);
        }

        private UpgradeCardView BuildCard(RectTransform parent, int index)
        {
            const float x = 414, y = 192, width = 312, height = 512;
            var root = Rect(parent, "Offer " + (index + 1), x + index * 329, y, width, height);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = new Vector2(x + index * 329 + width * .5f, -y - height * .5f);
            var row = root.gameObject.AddComponent<UpgradeCardView>();
            var slot = row.bindings;
            slot.button = root.gameObject.AddComponent<Button>();
            slot.button.transition = Selectable.Transition.None;
            var frame = Raised(root, "Card border", 0, 0, 312, 512, Card, Rarity[0], 4);
            slot.button.targetGraphic = frame;
            frame.raycastTarget = true;
            root.gameObject.AddComponent<UpgradeChoiceFx>();
            var rail = Flat(root, "Top rarity rail", 13, 4, 286, 2, Color.clear);
            var tag = Flat(root, "Rarity tag", 22, 22, 75, 24, Color.clear);
            slot.rarity = Label(root, "Rarity", "", 30, 22, 178, 24, 12, Muted, heading:true);
            slot.number = Label(root, "Shortcut", "", 260, 22, 28, 24, 14, Teal, TextAlignmentOptions.MidlineRight, bold:true);

            Raised(root, "Icon slot", 80, 54, 152, 152, Well, C(0x665548), 4);
            Icons(root, 92, 66, 128, out slot.rasterIcon, out slot.vectorIcon);
            Flat(root, "Header divider", 22, 220, 268, 1, Line);
            slot.name = Label(root, "Title", "", 24, 235, 264, 45, 29, Ink, heading:true);
            slot.name.enableAutoSizing = true;
            slot.name.fontSizeMin = 18;
            slot.name.fontSizeMax = 29;
            var description = Rect(root, "Description scroll", 24, 280, 264, 65);
            var scroll = description.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22;
            var viewport = Rect(description, "Description viewport", 0, 0, 256, 65);
            viewport.gameObject.AddComponent<RectMask2D>();
            Flat(viewport, "Description scroll target", 0, 0, 256, 65, Color.clear).raycastTarget = true;
            slot.description = Label(viewport, "Description", "", 0, 0, 256, 65, 15, Muted);
            slot.description.textWrappingMode = TextWrappingModes.Normal;
            slot.description.overflowMode = TextOverflowModes.Overflow;
            scroll.viewport = viewport;
            scroll.content = slot.description.rectTransform;
            var track = Flat(description, "Description scroll track", 260, 0, 4, 65, Line);
            var handle = Flat(track.transform, "Handle", 0, 0, 4, 65, ButtonEdge);
            Stretch(handle.rectTransform);
            handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.targetGraphic = handle;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            Flat(root, "Stat preview", 22, 351, 268, 61, Well);
            slot.previewCaption = Label(root, "Metric", "", 34, 360, 242, 16, 11, Muted, bold:true);
            slot.previewValue = Label(root, "Value", "", 34, 378, 242, 24, 20, Ink, bold:true);
            slot.previewValue.enableAutoSizing = true;
            slot.previewValue.fontSizeMin = 12;
            slot.previewValue.fontSizeMax = 20;
            slot.level = Label(root, "Level", "", 24, 422, 264, 23, 14, Muted, bold:true);
            var progress = new Image[10];
            for (int i = 0; i < progress.Length; i++)
            {
                Flat(root, "Track " + (i + 1), 24 + i * 27, 449, 21, 8, C(0x39302A));
                progress[i] = Flat(root, "Progress " + (i + 1), 24 + i * 27, 449, 21, 8, Color.clear);
            }
            var action = Raised(root, "Select upgrade", 22, 473, 268, 27, ButtonFill, ButtonEdge, 3);
            slot.actionLabel = Label(root, "Action", "SELECT UPGRADE", 22, 473, 268, 27, 14, Ink, TextAlignmentOptions.Center, bold:true);
            row.RustStyle = new CardStyle(frame, action, tag, rail, progress, slot.rarity, slot.actionLabel, scroll, slot.description);
            return row;
        }

        internal sealed class CardStyle
        {
            private readonly UpgradePixelSurface frame, action;
            private readonly Image tag, rail;
            private readonly Image[] cells;
            private readonly TMP_Text rarityLabel, actionLabel;
            private readonly ScrollRect descriptionScroll;
            private readonly TMP_Text description;
            private Color accent;
            internal CardStyle(UpgradePixelSurface frame, UpgradePixelSurface action, Image tag, Image rail,
                Image[] cells, TMP_Text rarityLabel, TMP_Text actionLabel, ScrollRect descriptionScroll, TMP_Text description)
            {
                this.frame = frame; this.action = action; this.tag = tag; this.rail = rail;
                this.cells = cells; this.rarityLabel = rarityLabel; this.actionLabel = actionLabel;
                this.descriptionScroll = descriptionScroll; this.description = description;
            }

            internal void UpdateOffer(SurvivorUpgradeData.Offer offer, int level)
            {
                int rarity = Mathf.Clamp(offer.rarity, 0, Rarity.Length - 1);
                accent = Rarity[rarity];
                var bg = accent;
                bg.a = rarity == 4 ? 1f : .15f;
                tag.color = bg;
                rarityLabel.color = rarity == 4 ? C(0x171206) : accent;
                tag.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    RarityTagWidths[rarity] + (offer.shiny ? 65 : 0));
                rail.color = rarity is 1 or 4 ? accent : Color.clear;
                rail.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rarity == 4 ? 3 : 2);
                float progress = level * cells.Length / (float)SurvivorUpgradeData.LevelCap;
                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i].color = accent;
                    cells[i].rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Round(21 * Mathf.Clamp01(progress - i)));
                }
                PaintFocus(false, false);
            }

            internal void FitDescription()
            {
                const float width = 256, viewportHeight = 65;
                description.fontSize = 15;
                if (description.GetPreferredValues(description.text, width, Mathf.Infinity).y > viewportHeight)
                    description.fontSize = 13;
                float height = Mathf.Ceil(description.GetPreferredValues(description.text, width, Mathf.Infinity).y) + 2;
                descriptionScroll.vertical = height > viewportHeight;
                description.alignment = descriptionScroll.vertical ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
                description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(viewportHeight, height));
                descriptionScroll.StopMovement();
                description.rectTransform.anchoredPosition = Vector2.zero;
            }

            internal void PaintFocus(bool focused, bool acquired)
            {
                var color = focused ? Teal : accent;
                Paint(frame, Card, color, 4);
                Paint(action, focused ? C(0x20352D) : ButtonFill, focused ? Teal : ButtonEdge, 3);
                actionLabel.text = acquired ? "ACQUIRED" : "SELECT UPGRADE";
                actionLabel.color = focused ? Teal : Ink;
            }
        }

        private Button Control(RectTransform parent, string name, float x, float y, float w, float h, string text, out TMP_Text label)
        {
            var root = Rect(parent, name, x, y, w, h);
            var graphic = Raised(root, "Face", 0, 0, w, h, ButtonFill, ButtonEdge, 3);
            graphic.raycastTarget = true;
            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            label = Label(root, "Label", text, 0, 0, w, h, 14, Ink, TextAlignmentOptions.Center, bold:true);
            return button;
        }

        private static void Icons(RectTransform p, float x, float y, float size, out Image raster, out SVGImage vector)
        {
            raster = Flat(p, "Raster icon", x, y, size, size, Color.white);
            raster.preserveAspect = true;
            vector = Rect(p, "Vector icon", x, y, size, size).gameObject.AddComponent<SVGImage>();
            vector.preserveAspect = true;
            vector.raycastTarget = false;
        }

        private TMP_Text Label(Transform parent, string name, string value, float x, float y, float w, float h, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool heading = false, bool bold = false)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (heading && pixel) text.font = pixel;
            else if (body) text.font = body;
            text.fontSize = size;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.raycastTarget = false;
            text.alignment = alignment;
            text.color = color;
            text.text = value;
            return text;
        }

        private static Color C(uint hex) => new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Flat(Transform p, string name, float x, float y, float w, float h, Color color)
        {
            var image = Rect(p, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static UpgradePixelSurface Raised(Transform p, string name, float x, float y, float w, float h, Color face, Color edge, float notch)
        {
            Surface(p, name + " shadow", x + 3, y + 3, w, h, Shadow, Shadow, Shadow, notch, 0);
            return Surface(p, name, x, y, w, h, face, Light(edge), Dark(edge), notch, 2);
        }

        private static void Paint(UpgradePixelSurface surface, Color face, Color edge, float notch) =>
            surface.Paint(face, Light(edge), Dark(edge), notch, 2);
        private static Color Light(Color edge) => Color.Lerp(edge, Ink, .22f);
        private static Color Dark(Color edge) => Color.Lerp(edge, Back, .36f);

        private static UpgradePixelSurface Surface(Transform p, string name, float x, float y, float w, float h,
            Color face, Color light, Color dark, float notch, float line, bool recessed = false)
        {
            var g = Rect(p, name, x, y, w, h).gameObject.AddComponent<UpgradePixelSurface>();
            g.raycastTarget = false;
            g.Paint(face, light, dark, notch, line, recessed);
            return g;
        }

        private static Texture2D Tile(string name, int width, int height, System.Func<int, int, Color> pixelColor)
        {
            var tile = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) tile.SetPixel(x, y, pixelColor(x, y));
            tile.Apply(false, true);
            return tile;
        }

        private static void Pattern(Transform parent, string name, float x, float y, float w, float h, Texture2D texture)
        {
            var graphic = Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            graphic.texture = texture;
            graphic.uvRect = new Rect(0, 0, w / texture.width, h / texture.height);
            graphic.raycastTarget = false;
        }

        private void Fit()
        {
            if (!design) return;
            var area = ((RectTransform)transform).rect;
            if (area.width > 0 && area.height > 0)
                design.localScale = Vector3.one * Mathf.Min(area.width / Width, area.height / Height);
        }

        private void OnRectTransformDimensionsChange() => Fit();

        internal void RefreshDescriptions() => descriptionsDirty = true;

        private void LateUpdate()
        {
            if (!descriptionsDirty) return;
            descriptionsDirty = false;
            // TMP measures the text after the coordinator activates the card panel.
            foreach (var card in cards)
                if (card && card.gameObject.activeInHierarchy) card.RustStyle.FitDescription();
        }

        private void OnDestroy()
        {
            if (dither) Destroy(dither);
            if (hazard) Destroy(hazard);
            if (!pixel) return;
            foreach (var atlas in pixel.atlasTextures) if (atlas) Destroy(atlas);
            if (pixel.material) Destroy(pixel.material);
            Destroy(pixel);
        }
    }
}

