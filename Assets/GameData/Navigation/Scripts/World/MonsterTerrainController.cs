using Unity.Mathematics;
using UnityEngine;

namespace MiningSimulator.Ores
{
    // Shared pursuit and traversal rules. Crowd10K evaluates them in Burst without PhysX calls per agent.
    internal static class MonsterTerrainRules
    {
        internal const float ClimbSpeedFactor = .75f;
        internal const float WallNormalY = .7071f;
        internal const float WallReach = 1.2f;
        internal const float DropThreshold = .5f;
        internal const float DropGravity = 25f;
        internal const float MinimumDropSpeed = 3f;
        internal const float MaximumDropSpeed = 5f;
        internal const float PursuitAcceleration = 22f;
        internal const float StandardImpactSpeed = 2.6f;

        internal static float SpeedVariation(uint id) =>
            .9f + .2f * ((math.hash(new uint2(id, 57u)) & 255u) / 255f);

        internal static float2 Accelerate(float2 current, float2 target, float maxChange)
        {
            float2 delta = target - current;
            return current + delta * math.min(1f, maxChange / math.max(.0001f, math.length(delta)));
        }

        internal static bool ShouldReleaseGrip(float2 forward, float2 outward, float goalFloor, float ledge) =>
            goalFloor < ledge - DropThreshold && math.dot(forward, outward) > .15f;

        internal static float2 PursuitDirection(uint id, float2 toward, float stopDistance)
        {
            float distance = math.length(toward);
            if (distance < .01f) return float2.zero;
            float2 forward = toward / distance;
            float2 side = new(-forward.y, forward.x);
            float spread = (math.hash(new uint2(id, 19u)) & 255u) / 127.5f - 1f;
            float offset = spread * 2.5f * math.saturate((distance - stopDistance - .5f) / 4f);
            return math.normalizesafe(toward + side * offset);
        }
    }

    internal sealed class MonsterTerrainController
    {
        private static TerrainGenerator activeArena;
        private static int nextLookupFrame;
        private static TerrainGenerator goalMap;
        private static int goalFrame = -1, goalSeed;
        private static Vector2 goalPoint;
        private static Vector3 goalSupport;
        private static bool goalSupported;
        private static readonly int groundMask = LayerMask.GetMask("Ground");
        private static readonly int propsMask = LayerMask.GetMask("MapObstacle");
        private static readonly int enemyLayer = LayerMask.NameToLayer("Enemy");

        private readonly CharacterController motor;
        private readonly uint id;
        private readonly float stepOffset;
        private enum Traversal { Walking, Climbing, Falling }
        private Traversal traversal;
        private TerrainGenerator arena;
        private Vector3 grip, landing, wallOutward, fallVelocity;
        private float2 drive, impact;
        private float verticalSpeed, climbSpeed, blockedTime, cliffRetry, climbStall;
        private int arenaSeed;
        private bool reducedStep;
        private Vector3 walkNormal = Vector3.up, contactNormal = Vector3.up;
        private int contactFrame = -1;

        internal float CrowdClimbHeight { get; private set; }

        internal bool HasVisualSupport => motor.enabled && motor.isGrounded && !IsTraversing;
        internal Vector3 VisualGroundNormal => arena ? walkNormal :
            contactFrame == Time.frameCount ? contactNormal : Vector3.up;

        internal void RecordGroundContact(ControllerColliderHit hit)
        {
            if (arena || IsTraversing || hit.normal.y < Mathf.Cos(Mathf.Min(60f, motor.slopeLimit) * Mathf.Deg2Rad) ||
                hit.collider.attachedRigidbody || hit.collider is CharacterController) return;
            if (contactFrame != Time.frameCount || hit.normal.y > contactNormal.y)
                contactNormal = hit.normal;
            contactFrame = Time.frameCount;
        }

        internal bool IsTraversing => traversal != Traversal.Walking;
        internal bool IsKnockedBack => math.lengthsq(impact) > .0025f;
        internal string State => traversal == Traversal.Climbing ? "Climbing wall" :
            traversal == Traversal.Falling ? "Dropping from cliff" : "Direct terrain pursuit";

        internal MonsterTerrainController(CharacterController motor)
        {
            this.motor = motor;
            if (enemyLayer >= 0) motor.gameObject.layer = enemyLayer;
            stepOffset = motor.stepOffset;
            id = unchecked((uint)motor.GetHashCode());
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetArena()
        {
            nextLookupFrame = 0;
            activeArena = null;
            goalMap = null;
            goalFrame = -1;
        }

        private static TerrainGenerator BlockArena()
        {
            if ((!activeArena || !activeArena.isActiveAndEnabled) && Time.frameCount >= nextLookupFrame)
            {
                activeArena = Object.FindAnyObjectByType<TerrainGenerator>();
                nextLookupFrame = Time.frameCount + 30;
            }
            return activeArena && activeArena.isActiveAndEnabled && activeArena.TryGetBlockLayout(out _, out _)
                ? activeArena : null;
        }

        internal bool TryPursue(Vector3 goal, float reach, bool spread, out Vector3 direction)
        {
            direction = Vector3.zero;
            var map = BlockArena();
            if (!map || !map.TrySample(motor.transform.position, out var floor, out _) ||
                !TrySampleGoal(map, goal, out var goalFloor)) return false;
            Vector3 toward = goal - motor.transform.position;
            var delta = new float2(toward.x, toward.z);
            bool sameElevation = Mathf.Abs(goalFloor.y - floor.y) <= MonsterTerrainRules.DropThreshold;
            float2 pursuit = spread && sameElevation
                ? MonsterTerrainRules.PursuitDirection(id, delta, reach) : math.normalizesafe(delta);
            direction = new Vector3(pursuit.x, 0f, pursuit.y);
            return true;
        }

        // Most monsters chase the same target. Reuse its terrain sample across this frame.
        private static bool TrySampleGoal(TerrainGenerator map, Vector3 goal, out Vector3 floor)
        {
            if (goalFrame != Time.frameCount || goalMap != map || goalSeed != map.Seed ||
                goalPoint.x != goal.x || goalPoint.y != goal.z)
            {
                goalFrame = Time.frameCount;
                goalMap = map;
                goalSeed = map.Seed;
                goalPoint = new Vector2(goal.x, goal.z);
                goalSupported = map.TrySample(goal, out goalSupport, out _);
            }
            floor = goalSupport;
            return goalSupported;
        }

        internal bool DifferentTier(Vector3 target)
        {
            var map = BlockArena();
            return map && TrySampleGoal(map, target, out var targetFloor) &&
                map.TrySample(motor.transform.position, out var footFloor, out _) &&
                Mathf.Abs(targetFloor.y - footFloor.y) > 1.2f;
        }

        internal void Reset()
        {
            traversal = Traversal.Walking;
            contactFrame = -1;
            walkNormal = Vector3.up;
            verticalSpeed = blockedTime = cliffRetry = 0f;
            CrowdClimbHeight = 0f;
            drive = impact = float2.zero;
            SetAirborne(false);
        }

        internal void AddImpact(Vector3 source)
        {
            if (!BlockArena() || IsTraversing) return;
            Vector3 delta = motor.transform.position - source;
            impact = math.normalizesafe(new float2(delta.x, delta.z)) * MonsterTerrainRules.StandardImpactSpeed;
        }

        internal void AddSupportImpact(Vector3 velocity)
        {
            if (traversal != Traversal.Climbing)
                impact += new float2(velocity.x, velocity.z);
        }

        internal Vector3 ConsumeImpact(float dt)
        {
            var velocity = new Vector3(impact.x, 0f, impact.y);
            impact = MonsterTerrainRules.Accelerate(impact, float2.zero, 14f * dt);
            return velocity;
        }

        internal bool TryMove(Vector3 velocity, float dt, Vector3? goal = null, float crowdSupport = 0f,
            float pursuitSpeed = 0f)
        {
            var map = BlockArena();
            if (map != arena || map && map.Seed != arenaSeed)
            {
                var pendingImpact = impact;
                Reset();
                impact = pendingImpact;
                arena = map;
                arenaSeed = map ? map.Seed : 0;
            }
            if (!arena || !motor.enabled || dt <= 0f) return false;
            walkNormal = Vector3.up;
            var shape = MonsterPathFollower.ProfileFor(motor);
            Vector3 foot = shape.Foot(motor.transform.position);
            if (!arena.TrySample(foot, out var floor, out var normal))
            {
                var pendingImpact = impact;
                Reset();
                impact = pendingImpact;
                return false;
            }

            bool grounded = Supported(foot.y - CrowdClimbHeight, floor.y, normal);
            if (traversal == Traversal.Climbing)
            {
                drive = impact = float2.zero;
                if (goal.HasValue && ShouldDrop(foot, goal.Value)) return true;
                Climb(foot, shape, dt);
                return true;
            }
            if (traversal == Traversal.Falling)
            {
                drive = float2.zero;
                if (!grounded || verticalSpeed == 0f)
                {
                    verticalSpeed = Mathf.Max(-30f, verticalSpeed - MonsterTerrainRules.DropGravity * dt);
                    Vector3 drift = (fallVelocity + ConsumeImpact(dt)) * dt;
                    if (!arena.TrySample(foot + drift, out _, out _)) drift = Vector3.zero;
                    motor.Move(drift + Vector3.up * (verticalSpeed * dt));
                    return true;
                }
                traversal = Traversal.Walking;
            }

            cliffRetry = Mathf.Max(0f, cliffRetry - dt);
            velocity.y = 0f;
            if (goal.HasValue)
            {
                float variation = MonsterTerrainRules.SpeedVariation(id);
                velocity *= variation;
                pursuitSpeed *= variation;
            }
            float speed = velocity.magnitude;
            float wallSpeed = Mathf.Max(speed, pursuitSpeed);
            if (grounded && wallSpeed > .01f)
            {
                Vector3 direction = speed > .01f ? velocity / speed : Vector3.zero;
                Vector3 towardWall = direction;
                bool mayClimb = true;
                if (goal.HasValue && TrySampleGoal(arena, goal.Value, out var goalFloor))
                {
                    mayClimb = goalFloor.y >= floor.y - MonsterTerrainRules.DropThreshold;
                    if (goalFloor.y > floor.y + MonsterTerrainRules.DropThreshold)
                        towardWall = Vector3.ProjectOnPlane(goal.Value - foot, Vector3.up).normalized;
                }
                if (cliffRetry <= 0f && mayClimb && towardWall.sqrMagnitude > .01f &&
                    TryGrip(foot, floor.y, towardWall, shape, wallSpeed))
                {
                    CrowdClimbHeight = 0f;
                    drive = impact = float2.zero;
                    Climb(foot, shape, dt);
                    return true;
                }
                if (blockedTime > .15f && speed > .01f) velocity = AvoidProp(foot, shape, direction) * speed;
            }

            var desired = new float2(velocity.x, velocity.z);
            drive = speed > .01f ? MonsterTerrainRules.Accelerate(drive, desired,
                MonsterTerrainRules.PursuitAcceleration * dt) : float2.zero;
            velocity = new Vector3(drive.x, 0f, drive.y) + ConsumeImpact(dt);
            if (grounded && velocity.sqrMagnitude > .0001f)
            {
                if (!arena.TrySample(foot + velocity * dt, out var next, out _)) velocity = Vector3.zero;
                else if (floor.y - next.y > MonsterTerrainRules.DropThreshold)
                {
                    traversal = Traversal.Falling;
                    CrowdClimbHeight = 0f;
                    fallVelocity = velocity.normalized * Mathf.Clamp(speed, MonsterTerrainRules.MinimumDropSpeed,
                        MonsterTerrainRules.MaximumDropSpeed);
                    verticalSpeed = 0f;
                    SetAirborne(true);
                    motor.Move(velocity * dt);
                    return true;
                }
            }

            float oldLift = CrowdClimbHeight;
            if (!grounded || IsKnockedBack) crowdSupport = 0f;
            float lift = Mathf.MoveTowards(oldLift, crowdSupport, dt * (crowdSupport > oldLift ? 3.6f : 2.8f));
            bool stacking = grounded && (oldLift > 0f || lift > 0f);
            SetAirborne(!grounded || stacking);
            verticalSpeed = grounded ? -2f : Mathf.Max(-30f, verticalSpeed + Physics.gravity.y * dt);
            float slopeSpeed = grounded && normal.y > MonsterTerrainRules.WallNormalY
                ? -Vector3.Dot(velocity, normal) / normal.y : 0f;
            float vertical = stacking ? lift - oldLift : verticalSpeed * dt;
            motor.Move(velocity * dt + Vector3.up * (vertical + slopeSpeed * dt));
            CrowdClimbHeight = stacking && lift > .005f &&
                arena.TrySample(shape.Foot(motor.transform.position), out var support, out _)
                ? Mathf.Clamp(shape.Foot(motor.transform.position).y - support.y, 0f, lift) : 0f;
            if (grounded) walkNormal = normal;
            Vector3 traveled = shape.Foot(motor.transform.position) - foot;
            traveled.y = 0f;
            blockedTime = speed > .1f && traveled.sqrMagnitude < speed * speed * dt * dt * .1f
                ? blockedTime + dt : 0f;
            return true;
        }

        private bool TryGrip(Vector3 foot, float floor, Vector3 direction, NavigationProfile shape, float speed)
        {
            float reach = shape.Radius + MonsterTerrainRules.WallReach;
            Vector3 x = direction.x >= 0f ? Vector3.right : Vector3.left;
            Vector3 z = direction.z >= 0f ? Vector3.forward : Vector3.back;
            float xContact = BlockContact(foot, x, Mathf.Abs(direction.x), shape.Radius, reach, out var xFace);
            float zContact = BlockContact(foot, z, Mathf.Abs(direction.z), shape.Radius, reach, out var zFace);
            bool xFirst = xContact <= zContact;
            if (xFirst && xContact < float.PositiveInfinity && TryBeginGrip(xFace, -x, floor, shape, speed)) return true;
            if (zContact < float.PositiveInfinity && TryBeginGrip(zFace, -z, floor, shape, speed)) return true;
            if (!xFirst && xContact < float.PositiveInfinity && TryBeginGrip(xFace, -x, floor, shape, speed)) return true;

            if (!arena.TrySample(foot + direction * reach, out var ahead, out _) || ahead.y - floor < .65f)
                return false;
            Vector3 chest = foot + Vector3.up * Mathf.Min(.65f, shape.Height * .45f);
            bool found = Physics.Raycast(chest, direction, out var hit, reach, groundMask, QueryTriggerInteraction.Ignore);
            if (!found) found = Physics.SphereCast(chest, Mathf.Min(.24f, shape.Radius * .45f), direction,
                out hit, reach, groundMask, QueryTriggerInteraction.Ignore);
            if (!found ||
                Mathf.Abs(hit.normal.y) >= MonsterTerrainRules.WallNormalY ||
                !hit.collider.transform.IsChildOf(arena.transform)) return false;

            Vector3 outward = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            Vector3 face = foot + outward * Vector3.Dot(hit.point - foot, outward);
            return TryBeginGrip(face, outward, floor, shape, speed);
        }

        private float BlockContact(Vector3 foot, Vector3 inward, float approach, float radius, float reach,
            out Vector3 face)
        {
            face = foot;
            if (approach < .035f || !arena.TryGetRisingBlockFace(foot, inward, reach, out face))
                return float.PositiveInfinity;
            return Mathf.Max(0f, Vector3.Dot(face - foot, inward) - radius) / approach;
        }

        private bool TryBeginGrip(Vector3 face, Vector3 outward, float floor, NavigationProfile shape, float speed)
        {
            float gap = shape.Radius + .08f + motor.skinWidth * Mathf.Abs(motor.transform.lossyScale.x);
            Vector3 top = face - outward * (gap + .35f);
            if (!TryLanding(top, shape, out var surface) || surface.y - floor < .65f) return false;
            grip = face + outward * gap;
            wallOutward = outward;
            landing = surface + Vector3.up * .1f;
            climbSpeed = speed * MonsterTerrainRules.ClimbSpeedFactor;
            climbStall = 0f;
            traversal = Traversal.Climbing;
            return true;
        }

        private bool ShouldDrop(Vector3 foot, Vector3 goal)
        {
            Vector3 forward = goal - foot;
            forward.y = 0f;
            forward.Normalize();
            if (TrySampleGoal(arena, goal, out var targetFloor) &&
                targetFloor.y < foot.y - MonsterTerrainRules.DropThreshold &&
                MonsterTerrainRules.ShouldReleaseGrip(new float2(forward.x, forward.z),
                    new float2(wallOutward.x, wallOutward.z), targetFloor.y, landing.y - .1f))
            {
                BeginFall(forward);
                return true;
            }

            return false;
        }

        private void BeginFall(Vector3 direction)
        {
            traversal = Traversal.Falling;
            verticalSpeed = 0f;
            fallVelocity = direction * Mathf.Clamp(climbSpeed / MonsterTerrainRules.ClimbSpeedFactor,
                MonsterTerrainRules.MinimumDropSpeed, MonsterTerrainRules.MaximumDropSpeed);
            cliffRetry = .5f;
        }

        private bool TryLanding(Vector3 top, NavigationProfile shape, out Vector3 surface)
        {
            if (!arena.TrySample(top, out surface, out var normal) ||
                normal.y < Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad)) return false;
            Vector3 lower = surface + Vector3.up * (shape.Radius + .12f);
            Vector3 upper = surface + Vector3.up * (shape.Height - shape.Radius + .12f);
            return !Physics.CheckCapsule(lower, upper, shape.Radius, groundMask | propsMask,
                QueryTriggerInteraction.Ignore);
        }

        private void Climb(Vector3 foot, NavigationProfile shape, float dt)
        {
            SetAirborne(true);
            Vector3 lateral = new(foot.x - grip.x, 0f, foot.z - grip.z);
            var inward = -wallOutward;
            bool crossingFace = Vector3.Dot(lateral, inward) > .12f;
            bool atLedge = crossingFace || foot.y >= landing.y - .06f;
            Vector3 goal = atLedge ? landing : new Vector3(grip.x, landing.y, grip.z);
            // A ramp can rise into the exit path before the capsule reaches the stored landing.
            if (atLedge && arena.TrySample(foot + inward * (shape.Radius + .1f), out var lip, out var lipNormal) &&
                lipNormal.y >= Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad))
                goal.y = Mathf.Max(goal.y, lip.y + .1f);
            Vector3 step = Vector3.MoveTowards(foot, goal, climbSpeed * dt) - foot;
            motor.Move(step);
            Vector3 next = shape.Foot(motor.transform.position);
            // A large capsule can stand on the upper surface before its center reaches the chosen landing point.
            var fromLanding = Vector3.ProjectOnPlane(next - landing, Vector3.up);
            if (atLedge && Vector3.Dot(next - grip, inward) > shape.Radius + .08f &&
                arena.TrySample(next, out var support, out var normal) && Supported(next.y, support.y, normal) &&
                Mathf.Abs(support.y + .1f - landing.y) <= .3f +
                    fromLanding.magnitude * Mathf.Tan(motor.slopeLimit * Mathf.Deg2Rad))
            {
                traversal = Traversal.Walking;
                verticalSpeed = -2f;
                cliffRetry = .25f;
                return;
            }
            climbStall = (next - goal).sqrMagnitude >= (foot - goal).sqrMagnitude - step.sqrMagnitude * .1f
                ? climbStall + dt : 0f;
            if (climbStall > .65f)
            {
                // Clear the face so the rounded foot cannot balance on the lip during the fall.
                BeginFall(wallOutward);
            }
        }

        private bool Supported(float footHeight, float floorHeight, Vector3 normal)
        {
            if (normal.y < Mathf.Cos(motor.slopeLimit * Mathf.Deg2Rad)) return false;
            float gap = footHeight - floorHeight;
            float tolerance = Mathf.Max(.14f, motor.skinWidth * Mathf.Abs(motor.transform.lossyScale.y) * 1.5f);
            return gap >= -tolerance && gap <= (motor.isGrounded ? Mathf.Max(.35f, tolerance) : tolerance);
        }

        private Vector3 AvoidProp(Vector3 foot, NavigationProfile shape, Vector3 direction)
        {
            Vector3 origin = foot + Vector3.up * (shape.Radius + .1f);
            float radius = shape.Radius * .85f;
            if (!Physics.SphereCast(origin, radius, direction, out var hit, .65f, propsMask,
                QueryTriggerInteraction.Ignore)) return direction;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            if (Vector3.Dot(hit.normal, side) < 0f || (Mathf.Abs(Vector3.Dot(hit.normal, side)) < .01f && (id & 1u) != 0))
                side = -side;
            return (side + direction * .2f).normalized;
        }

        private void SetAirborne(bool airborne)
        {
            if (reducedStep == airborne || !motor) return;
            motor.stepOffset = airborne ? 0f : Mathf.Min(stepOffset, motor.height);
            reducedStep = airborne;
        }
    }
}
