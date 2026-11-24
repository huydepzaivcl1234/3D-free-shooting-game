using TMPro;
using UnityEngine;

namespace MiningSimulator.Ores
{
    [CreateAssetMenu(menuName = "Survivor/UI Style")]
    public sealed class UIStyle : ScriptableObject
    {
        public Color panel = new Color32(23, 20, 18, 255);
        public Color tray = new Color32(16, 14, 12, 255);
        public Color card = new Color32(32, 28, 25, 255);
        public Color slot = new Color32(44, 39, 35, 255);
        public Color edge = new Color32(74, 66, 58, 255);
        public Color ink = new Color32(241, 231, 208, 255);
        public Color muted = new Color32(184, 175, 163, 255);
        public Color accent = new Color32(61, 230, 210, 255);
        public Font pixelFont;
        public TMP_FontAsset bodyFont;
        public TMP_FontAsset headingFont;
        [SerializeField] private TMP_FontAsset pixelAsset;
        internal static UIStyle Shared => Resources.Load<UIStyle>("Capsule Bonk UI Style");
        private TMP_FontAsset generatedFont;
        internal TMP_FontAsset Pixel => pixelAsset ? pixelAsset : pixelFont
            ? generatedFont ? generatedFont : generatedFont = TMP_FontAsset.CreateFontAsset(pixelFont)
            : bodyFont;
    }
}
