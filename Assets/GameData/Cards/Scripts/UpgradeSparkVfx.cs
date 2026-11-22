using System.Collections.Generic;
using GameData.Player.Script;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    public sealed class UpgradeSparkVfx : MonoBehaviour
    {
        private static readonly int WipePhase = Shader.PropertyToID("_UpgradeCardWipePhase");
        private const string OverlayName = "Sparkling Linear Wipe";
        private readonly List<RawImage> visibleWipes = new(3);

        public void Refresh(Button[] buttons, IReadOnlyList<SurvivorUpgradeData.Offer> offers, Material material)
        {
            foreach (var wipe in visibleWipes)
                if (wipe) wipe.gameObject.SetActive(false);
            visibleWipes.Clear();

            for (int i = 0; i < buttons.Length && i < offers.Count; i++)
            {
                if (!buttons[i] || !offers[i].shiny) continue;
                var card = (RectTransform)buttons[i].transform;
                var overlay = card.Find(OverlayName) as RectTransform;
                if (!overlay)
                {
                    overlay = new GameObject(OverlayName, typeof(RectTransform), typeof(RawImage))
                        .GetComponent<RectTransform>();
                    overlay.SetParent(card, false);
                    overlay.anchorMin = Vector2.zero;
                    overlay.anchorMax = Vector2.one;
                    overlay.offsetMin = Vector2.zero;
                    overlay.offsetMax = Vector2.zero;
                }

                overlay.SetAsLastSibling();
                var wipe = overlay.GetComponent<RawImage>();
                wipe.material = material;
                wipe.raycastTarget = false;
                wipe.maskable = false;
                overlay.gameObject.SetActive(true);
                visibleWipes.Add(wipe);
            }
        }

        private void LateUpdate()
        {
            foreach (var wipe in visibleWipes)
            {
                if (!wipe || !wipe.gameObject.activeInHierarchy) continue;
                // Choosing cards pauses gameplay time, so animate from the unscaled clock.
                Shader.SetGlobalFloat(WipePhase, Mathf.Repeat(Time.unscaledTime * .36f, 1f));
                break;
            }
        }
    }
}
