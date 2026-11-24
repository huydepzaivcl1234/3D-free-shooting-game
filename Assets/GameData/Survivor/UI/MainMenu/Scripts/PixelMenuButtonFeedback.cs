using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    internal sealed class PixelMenuButtonFeedback : MonoBehaviour, ISelectHandler, IDeselectHandler,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, ISubmitHandler
    {
        [SerializeField] private RectTransform art;
        [SerializeField] private UnityEngine.UI.Image ring;
        [SerializeField] private Button button;
        [SerializeField] private bool useMainMenuSounds;
        private bool pressed, hovered;
        private float scale = 1f;
        private MiningButtonSfx sound;

        private void Awake()
        {
            if (!TryGetComponent(out sound)) sound = gameObject.AddComponent<MiningButtonSfx>();
            sound.UseMainMenuSounds = useMainMenuSounds;
        }
        private float submitUntil;

        internal void UseMainMenuSounds()
        {
            useMainMenuSounds = true;
            if (sound) sound.UseMainMenuSounds = true;
        }

        internal void Configure(Button control, RectTransform visual, UnityEngine.UI.Image focus)
        {
            button = control;
            art = visual;
            ring = focus;
            ring.enabled = false;
        }

        public void OnSelect(BaseEventData eventData) => ring.enabled = button.IsInteractable();
        public void OnDeselect(BaseEventData eventData) => ring.enabled = false;
        public void OnPointerDown(PointerEventData eventData) =>
            pressed = eventData.button == PointerEventData.InputButton.Left && button.IsInteractable();
        public void OnPointerUp(PointerEventData eventData) => pressed = false;
        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (button && button.IsInteractable()) sound?.PlayHover();
        }

        public void OnPointerExit(PointerEventData eventData) => hovered = pressed = false;
        public void OnSubmit(BaseEventData eventData)
        {
            if (button.IsInteractable()) submitUntil = Time.unscaledTime + .10f;
        }

        private void LateUpdate()
        {
            if (!art) return;
            float target = hovered && button.IsInteractable() ? 1.035f : 1f;
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-21f * Time.unscaledDeltaTime));
            if (Mathf.Abs(scale - target) < .0005f) scale = target;
            art.localScale = Vector3.one * scale;
            Vector2 center = Vector2.Scale(art.rect.size, Vector2.one * .5f - art.pivot);
            art.anchoredPosition = center * (1f - scale);
            if (pressed || Time.unscaledTime < submitUntil) art.anchoredPosition += Vector2.down * 2f;
            ring.enabled = button.IsInteractable() && EventSystem.current &&
                EventSystem.current.currentSelectedGameObject == gameObject;
        }

        private void OnDisable()
        {
            pressed = hovered = false;
            submitUntil = 0f;
            if (art)
            {
                art.anchoredPosition = Vector2.zero;
                art.localScale = Vector3.one * (scale = 1f);
            }
            if (ring) ring.enabled = false;
        }
    }
}
