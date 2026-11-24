using UnityEngine;

namespace MiningSimulator.Ores
{
    public enum GhostTier { White, Black, Purple, Red }

    public sealed partial class MushroomMonster
    {
        [SerializeField] private Transform ghostVisual;
        private GhostTier ghostTier;

        internal bool IsOvertimeGhost => ghostVisual != null;
        private MonsterSpawnSettings.GhostSettings GhostTuning => zone ? zone.GhostTuning : MonsterSpawnZone.DefaultGhostSettings;
        internal float GhostControlCap => zone && !IsSupportAlly ? zone.GhostControlCap : GhostTuning.controlCapSeconds;
        internal float GhostControlMultiplier => GhostTuning.controlCapSeconds > 0f
            ? Mathf.Clamp01(GhostControlCap / GhostTuning.controlCapSeconds) : 0f;
        internal float GhostFlightSpeedMultiplier => zone ? zone.GhostSurgeSpeed : 1f;
        internal float GhostTierDamage => ghostTier switch
        {
            GhostTier.Black => GhostTuning.blackDamage,
            GhostTier.Purple => GhostTuning.purpleDamage,
            GhostTier.Red => GhostTuning.redDamage,
            _ => 1f
        };

        internal Color GhostCrowdTint => ghostTier switch
        {
            GhostTier.Black => GhostTuning.blackColor,
            GhostTier.Purple => GhostTuning.purpleColor,
            GhostTier.Red => GhostTuning.redColor,
            _ => GhostTuning.whiteColor
        };

        internal void SetGhostTier(GhostTier tier)
        {
            if (!IsOvertimeGhost) return;
            ghostTier = tier;
            var color = GhostCrowdTint;

            var properties = new MaterialPropertyBlock();
            foreach (var renderer in ghostVisual.GetComponentsInChildren<Renderer>())
            {
                properties.Clear();
                renderer.GetPropertyBlock(properties, 0);
                properties.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(properties, 0);
            }
        }

        private void TickGhostFlight(float dt)
        {
            if (dt <= 0f) return;
            if (IsSupportAlly) UpdateSupportTarget();
            else if (!TrySelectCloserAlly()) SetCombatTarget(playerTarget);

            bool attack = target && target.IsTargetable;
            if (!attack && !IsSupportAlly) return;
            Vector3 goal = attack ? target.transform.position : SupportFollowPoint();
            if (!attack && (goal - transform.position).sqrMagnitude < 1f) return;
            goal.y += GhostTuning.hoverHeight;
            Vector3 offset = goal - transform.position;
            Vector3 facing = new Vector3(offset.x, 0f, offset.z);
            if (facing.sqrMagnitude > .001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), GhostTuning.turnDegreesPerSecond * dt);

            bool touching = attack && IsGhostTouchingTarget();
            if (!touching)
            {
                // Direct flight lets the ghost traverse walls and ledges without a ground path.
                float speed = moveSpeed * (IsSupportAlly ? 1f : GhostFlightSpeedMultiplier);
                transform.position = Vector3.MoveTowards(transform.position, goal, speed * dt);
                touching = attack && IsGhostTouchingTarget();
            }

            if (touching && Time.time >= nextAttack)
            {
                nextAttack = Time.time + EffectiveAttackCooldown;
                target.DealDamage(scaledDamage,
                    rewards ? rewards.attackDamageType : CombatDamageType.Physical, gameObject);
            }
        }

        private bool IsGhostTouchingTarget()
        {
            Vector3 axis = transform.TransformPoint(motor.center);
            float radius = motor.radius * HitScale;
            float halfSegment = Mathf.Max(0f, motor.height * Mathf.Abs(transform.lossyScale.y) * .5f - radius);
            float contactRadius = radius + Mathf.Max(0f, GhostTuning.contactPadding);
            // Player and monster controllers have upright capsule bodies. Compare their world-space
            // segments directly so controller colliders and newly moved targets register contact.
            if (target.TryGetComponent<CapsuleCollider>(out var capsule) && capsule.enabled &&
                !capsule.isTrigger && capsule.direction == 1 && Mathf.Abs(capsule.transform.up.y) > .99f)
                return OverlapsGhostCapsule(axis, halfSegment, contactRadius, capsule.transform, capsule.center, capsule.radius, capsule.height);
            if (target.TryGetComponent<CharacterController>(out var body) && body.enabled)
                return OverlapsGhostCapsule(axis, halfSegment, contactRadius, body.transform, body.center, body.radius, body.height);
            if (!targetCollider || !targetCollider.enabled) return false;
            axis.y = Mathf.Clamp(targetCollider.bounds.center.y, axis.y - halfSegment, axis.y + halfSegment);
            return (targetCollider.ClosestPoint(axis) - axis).sqrMagnitude <= contactRadius * contactRadius;
        }

        private static bool OverlapsGhostCapsule(Vector3 axis, float halfSegment, float radius,
            Transform body, Vector3 center, float bodyRadius, float bodyHeight)
        {
            var scale = body.lossyScale;
            bodyRadius *= Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float bodyHalfSegment = Mathf.Max(0f, bodyHeight * Mathf.Abs(scale.y) * .5f - bodyRadius);
            Vector3 delta = body.TransformPoint(center) - axis;
            delta.y = Mathf.Max(0f, Mathf.Abs(delta.y) - halfSegment - bodyHalfSegment);
            float reach = radius + bodyRadius;
            return delta.sqrMagnitude <= reach * reach;
        }
    }
}
