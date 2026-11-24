using UnityEngine;
using UnityEngine.EventSystems;

namespace MiningSimulator.Ores
{
    public sealed class CharacterTurntableRotator : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Transform Turntable { get; set; }
        private float angle, goal, velocity, inertia;
        private bool dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            dragging = true;
            inertia = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging) return;
            float turn = -eventData.delta.x * (380f / Mathf.Max(1f, Screen.width));
            goal += turn;
            inertia = Mathf.Clamp(turn / Mathf.Max(.01f, Time.unscaledDeltaTime), -320f, 320f);
        }

        public void OnEndDrag(PointerEventData eventData) => dragging = false;
        internal void Rotate(float degrees) { goal += degrees; inertia = 0f; }
        internal void ResetTurn() { goal = 0f; inertia = 0f; dragging = false; }
        private void OnDisable() { dragging = false; inertia = 0f; }

        internal void Step()
        {
            if (!Turntable) return;
            float dt = Time.unscaledDeltaTime;
            if (!dragging)
            {
                goal += inertia * dt;
                inertia = Mathf.MoveTowards(inertia, 0f, 600f * dt);
            }
            angle = Mathf.SmoothDampAngle(angle, goal, ref velocity, .09f, Mathf.Infinity, dt);
            Turntable.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }
}
