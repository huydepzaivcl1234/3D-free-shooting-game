using UnityEngine;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class MapAtmosphere : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private Transform player;
        [SerializeField] private ParticleSystem dust;
        [SerializeField] private Color horizon = new Color32(53, 184, 232, 255);
        [SerializeField] private float fogStart = 18f;
        [SerializeField] private float fogEnd = 56f;

        private void OnEnable() => Apply();

        public void Apply()
        {
            if (view)
            {
                view.clearFlags = CameraClearFlags.SolidColor;
                view.backgroundColor = horizon;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        private void LateUpdate()
        {
            if (player && dust) dust.transform.position = player.position + Vector3.up * 3.5f;
        }
    }
}
