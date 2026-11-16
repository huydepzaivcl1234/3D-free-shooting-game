using UnityEngine;

namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class MegabonkPlayerMovement : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maxSlideSpeed = 22f;
        [SerializeField, Min(0f)] private float slopeSlideBoost = 40f;
        [SerializeField, Min(0f)] private float slideFriction = 4f;
        [SerializeField, Min(0f)] private float slideStartBoost = 2f;
        [SerializeField, Min(.5f)] private float slideColliderHeight = 1f;
        private readonly Collider[] overhead = new Collider[16];
        private Rigidbody body;
        private CapsuleCollider capsule;
        private float standingHeight;
        private Vector3 standingCenter;
        private float previousDamping;
        private bool lowered, preserveMomentum;

        public bool IsSliding { get; private set; }
        internal bool KeepsAirMomentum => preserveMomentum;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
        }

        // PlayerController owns the physics tick and the jump/land events.
        internal void UpdatePosture(bool held, bool grounded, Vector3 direction, float moveSpeed)
        {
            if (!grounded)
            {
                if (!held) TryStand();
                return;
            }

            if (!held)
            {
                IsSliding = false;
                EndMomentum();
                TryStand();
                return;
            }

            if (preserveMomentum)
            {
                IsSliding = true;
                EndMomentum();
                LowerCapsule();
            }
            else if (!IsSliding && direction.sqrMagnitude > .01f && moveSpeed > .01f)
            {
                IsSliding = true;
                LowerCapsule();
                Vector3 velocity = body.linearVelocity;
                Vector3 flatVelocity = new(velocity.x, 0f, velocity.z);
                Vector3 forward = flatVelocity.sqrMagnitude > 1f ? flatVelocity.normalized : direction.normalized;
                float speed = Mathf.Min(maxSlideSpeed, Mathf.Max(flatVelocity.magnitude, moveSpeed) + slideStartBoost);
                body.linearVelocity = forward * speed + Vector3.up * velocity.y;
            }
        }

        internal Vector3 CalculateSlideVelocity(Vector3 input, Vector3 normal, float dt)
        {
            Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, normal);
            if (input.sqrMagnitude > .01f && velocity.sqrMagnitude > .04f)
            {
                Vector3 steer = Vector3.ProjectOnPlane(input, normal).normalized;
                velocity = Vector3.RotateTowards(velocity, steer * velocity.magnitude, 2f * dt, 0f);
            }

            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, normal);
            velocity += downhill * (slopeSlideBoost * dt);
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, slideFriction * dt);
            return Vector3.ClampMagnitude(velocity, maxSlideSpeed);
        }

        internal void LeaveGround()
        {
            if (!IsSliding) return;
            IsSliding = false;
            previousDamping = body.linearDamping;
            body.linearDamping = 0f;
            preserveMomentum = true;
        }

        internal void ResetSlide()
        {
            IsSliding = false;
            EndMomentum();
            TryStand();
        }

        private void OnDisable() => ResetSlide();

        private void EndMomentum()
        {
            if (!preserveMomentum) return;
            body.linearDamping = previousDamping;
            preserveMomentum = false;
        }

        private void LowerCapsule()
        {
            if (lowered) return;
            standingCenter = capsule.center;
            standingHeight = capsule.height;
            float lowerHeight = Mathf.Min(standingHeight, Mathf.Max(slideColliderHeight, capsule.radius * 2f));
            if (lowerHeight >= standingHeight) return;
            capsule.height = lowerHeight;
            capsule.center = standingCenter - Vector3.up * ((standingHeight - lowerHeight) * .5f);
            lowered = true;
        }

        private void TryStand()
        {
            if (!lowered || !capsule) return;
            Vector3 scale = transform.lossyScale;
            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            Vector3 axis = transform.up;
            Vector3 loweredTop = transform.TransformPoint(capsule.center) +
                axis * Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 standingTop = transform.TransformPoint(standingCenter) +
                axis * Mathf.Max(0f, standingHeight * Mathf.Abs(scale.y) * .5f - radius);
            int count = Physics.OverlapCapsuleNonAlloc(loweredTop, standingTop, Mathf.Max(.01f, radius - .025f),
                overhead, ~0, QueryTriggerInteraction.Ignore);
            if (count == overhead.Length) return;
            for (int i = 0; i < count; i++)
            {
                Collider hit = overhead[i];
                if (hit.attachedRigidbody == body || hit.transform.IsChildOf(transform) ||
                    Physics.GetIgnoreLayerCollision(gameObject.layer, hit.gameObject.layer) ||
                    Physics.GetIgnoreCollision(capsule, hit)) continue;
                return;
            }
            capsule.height = standingHeight;
            capsule.center = standingCenter;
            lowered = false;
        }
    }
}
