using UnityEngine;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class StagePortalVisual : MonoBehaviour
    {
        [SerializeField] private Transform surface;
        [SerializeField, Min(.05f)] private float openingSeconds = .7f;
        private Vector3 fullScale;
        private float elapsed;
        public bool IsOpeningOrOpen { get; private set; }

        private void Awake()
        {
            if (!surface) return;
            fullScale = surface.localScale;
            surface.gameObject.SetActive(false);
            enabled = false;
        }

        internal void SetOpen(bool open)
        {
            if (!surface || IsOpeningOrOpen == open) return;
            IsOpeningOrOpen = open;
            elapsed = 0f;
            surface.localScale = open ? new Vector3(.01f, .01f, fullScale.z) : fullScale;
            surface.gameObject.SetActive(open);
            enabled = open;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / openingSeconds);
            float scale = Mathf.SmoothStep(.01f, 1f, progress);
            surface.localScale = new Vector3(fullScale.x * scale, fullScale.y * scale, fullScale.z);
            if (progress >= 1f) enabled = false;
        }
    }
}
