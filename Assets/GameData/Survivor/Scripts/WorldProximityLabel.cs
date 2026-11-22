using TMPro;
using UnityEngine;

namespace MiningSimulator.Ores
{
    internal sealed class WorldProximityLabel
    {
        private readonly Canvas canvas;
        private readonly TMP_Text label;
        private Transform target;
        private float height, shownAt;
        private const float ShowSeconds = .24f;

        internal WorldProximityLabel(Transform parent, string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * .009f;
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(245, 52);

            label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            label.transform.SetParent(root.transform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8, 2);
            label.rectTransform.offsetMax = new Vector2(-8, -2);
            label.font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.fontSizeMin = 19;
            label.fontSizeMax = 36;
            label.enableAutoSizing = true;
            label.raycastTarget = false;
            label.outlineColor = new Color32(20, 16, 13, 255);
            label.outlineWidth = .16f;
            Hide();
        }

        internal void Show(Transform anchor, Camera camera, string text, Color color)
        {
            if (target != anchor)
            {
                target = anchor;
                shownAt = Time.unscaledTime;
                float top = anchor.position.y + .7f;
                foreach (var renderer in anchor.GetComponentsInChildren<Renderer>())
                    if (renderer.enabled && renderer is (MeshRenderer or SkinnedMeshRenderer))
                        top = Mathf.Max(top, renderer.bounds.max.y);
                height = top - anchor.position.y + .45f;
            }

            if (label.text != text) label.text = text;
            float progress = Mathf.Clamp01((Time.unscaledTime - shownAt) / ShowSeconds);
            float eased = 1f - (1f - progress) * (1f - progress);
            color.a *= eased;
            if (label.color != color) label.color = color;
            canvas.worldCamera = camera;
            float lift = height - .25f * (1f - eased);
            canvas.transform.SetPositionAndRotation(anchor.position + Vector3.up * lift, camera.transform.rotation);
            if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            if (canvas.gameObject.activeSelf) canvas.gameObject.SetActive(false);
            target = null;
        }
    }
}
