using UnityEngine;
using UnityEngine.Rendering;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class SupportUpgradePillar : MonoBehaviour
    {
        private const int RingSegments = 48;
        internal const float LegacySizeMultiplier = 1.5f;

        [SerializeField] private LanternPillarSettings settings;
        [SerializeField, Min(.5f)] private float activationRadius = 4f;
        [SerializeField, Min(.1f)] private float chargeSeconds = 3f;
        [SerializeField] private Material ringMaterial;
        [SerializeField] private MeshRenderer lantern;
        [SerializeField, Min(0)] private int emissiveMaterialIndex = 1;

        internal LanternPillarSettings Settings => settings;
        internal bool IsClaimed => claimed;
        internal float Radius => Mathf.Max(.5f, settings ? settings.activationRadius : activationRadius);
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly Color InactiveColor = new(.96f, .71f, .35f, .7f);
        private static readonly Color ReadyColor = new(1f, .83f, .35f);
        private static readonly Color ClaimedColor = new(.42f, .58f, .52f, .72f);

        private CharacterHealth player;
        private MonsterSpawnZone stage;
        private UiPanelCoordinator coordinator;
        private GameMainMenu menu;
        private PlayerSupportUpgrades upgrades;
        private string saveKey;
        private Camera view;
        private LineRenderer groundRing, chargeTrack, chargeFill;
        private readonly Vector3[] arc = new Vector3[RingSegments + 1];
        private float elapsed, chargeCenterHeight;
        private bool claimed, exhausted;

        internal void Initialize(IProceduralArena arena, int slot, CharacterHealth player,
            MonsterSpawnZone stage, UiPanelCoordinator coordinator, GameMainMenu menu)
        {
            this.player = player;
            this.stage = stage;
            this.coordinator = coordinator;
            this.menu = menu;
            saveKey = MakeSaveKey(arena.Seed, slot);
            if (!ringMaterial) return;

            groundRing = CreateLine("Activation radius", .09f, InactiveColor);
            groundRing.positionCount = RingSegments;
            groundRing.loop = true;
            groundRing.useWorldSpace = true;
            for (int i = 0; i < RingSegments; ++i)
            {
                float angle = i * (2f * Mathf.PI / RingSegments);
                Vector3 point = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius;
                if (arena.TrySample(point, out var ground, out _)) point.y = ground.y;
                groundRing.SetPosition(i, point + Vector3.up * .075f);
            }

            float diameter = settings ? Mathf.Max(.1f, settings.progressDiameter) : .88f * Mathf.Abs(transform.lossyScale.x);
            float heightFraction = settings ? Mathf.Clamp01(settings.progressHeight) : .52f;
            chargeCenterHeight = lantern
                ? Mathf.Lerp(lantern.bounds.min.y, lantern.bounds.max.y, heightFraction) - transform.position.y
                : 1.12f * Mathf.Abs(transform.lossyScale.y);

            chargeTrack = CreateLine("Charge track", diameter * .04f, new Color(.12f, .08f, .05f, .95f));
            chargeTrack.useWorldSpace = true;
            chargeTrack.loop = true;
            chargeTrack.positionCount = RingSegments;
            chargeFill = CreateLine("Charge progress", diameter * .065f, ReadyColor);
            chargeFill.useWorldSpace = true;
            for (int i = 0; i <= RingSegments; ++i)
            {
                float angle = i * (2f * Mathf.PI / RingSegments);
                arc[i] = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * (diameter * .5f);
            }
            ShowCharge(false);
        }

        internal static string MakeSaveKey(int seed, int slot) =>
            System.FormattableString.Invariant($"lantern:{seed}:{slot}");

        private LineRenderer CreateLine(string label, float width, Color color)
        {
            var line = new GameObject(label).AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.sharedMaterial = ringMaterial;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.startColor = line.endColor = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return line;
        }

        private void LateUpdate()
        {
            if (saveKey == null || !player) return;

            Vector3 offset = player.transform.position - transform.position;
            float horizontalDistance = offset.x * offset.x + offset.z * offset.z;
            bool near = horizontalDistance <= Radius * Radius && Mathf.Abs(offset.y) <= 2f;
            bool restoring = RunCheckpoint.IsRestoring || (stage && stage.IsRestoringWorld);
            bool live = !restoring && player.isActiveAndEnabled && player.Health > 0f && Time.deltaTime > 0f &&
                (!coordinator || !coordinator.BlocksGameplay) && (!menu || !menu.IsOpen);
            if (groundRing) groundRing.enabled = horizontalDistance <= 16f * 16f && Mathf.Abs(offset.y) <= 7f;

            if (restoring)
            {
                if (exhausted && groundRing) groundRing.startColor = groundRing.endColor = InactiveColor;
                exhausted = false;
                ResetCharge();
                return;
            }

            if (live)
            {
                if (!upgrades) upgrades = PlayerSupportUpgrades.For(player);
                if (upgrades) SetClaimed(upgrades.HasClaimedPillar(saveKey));
            }

            if (!near || player.Health <= 0f)
            {
                ResetCharge();
                return;
            }
            if (!live || !upgrades || claimed || exhausted) return;
            if (elapsed == 0f && System.Array.TrueForAll(PlayerSupportUpgrades.Definitions,
                definition => upgrades.Level(definition.Id) >= PlayerSupportUpgrades.LevelCap))
            {
                exhausted = true;
                if (groundRing) groundRing.startColor = groundRing.endColor = ClaimedColor;
                return;
            }

            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(.1f, settings ? settings.chargeSeconds : chargeSeconds));
            if (progress < 1f)
            {
                DrawCharge(progress);
                return;
            }

            bool granted = upgrades.TryGrantFromPillar(saveKey, out _);
            SetClaimed(granted || upgrades.HasClaimedPillar(saveKey));
            ResetCharge();
        }

        private void ResetCharge()
        {
            elapsed = 0f;
            ShowCharge(false);
        }

        private void SetClaimed(bool value)
        {
            if (claimed == value) return;
            claimed = value;
            if (groundRing) groundRing.startColor = groundRing.endColor = value ? ClaimedColor : InactiveColor;
            if (!lantern) return;

            var properties = new MaterialPropertyBlock();
            lantern.GetPropertyBlock(properties, emissiveMaterialIndex);
            properties.SetColor(EmissionColor, value ? new Color(4f, 1.63f, .36f) : Color.black);
            lantern.SetPropertyBlock(properties, emissiveMaterialIndex);
        }

        private void DrawCharge(float progress)
        {
            if (!chargeTrack) return;
            if (!view || !view.isActiveAndEnabled) view = Camera.main;
            if (!view) return;

            Vector3 center = transform.position + Vector3.up * chargeCenterHeight;
            Vector3 towardView = (view.transform.position - center).normalized;
            Vector3 position = center + towardView * (.53f * transform.lossyScale.x);
            Quaternion rotation = view.transform.rotation;
            for (int i = 0; i < RingSegments; i++)
                chargeTrack.SetPosition(i, position + rotation * arc[i]);

            // Offset the fill slightly toward the viewer so it draws above the track.
            Vector3 fillPosition = position + towardView * .015f;

            float steps = progress * RingSegments;
            int fullSteps = Mathf.Min(RingSegments - 1, Mathf.FloorToInt(steps));
            chargeFill.positionCount = fullSteps + 2;
            for (int i = 0; i <= fullSteps; ++i) chargeFill.SetPosition(i, fillPosition + rotation * arc[i]);
            Vector3 tip = Vector3.Lerp(arc[fullSteps], arc[fullSteps + 1], steps - fullSteps);
            chargeFill.SetPosition(fullSteps + 1, fillPosition + rotation * tip);
            ShowCharge(true);
        }

        private void ShowCharge(bool visible)
        {
            if (!chargeTrack) return;
            chargeTrack.enabled = visible;
            chargeFill.enabled = visible;
        }
    }
}
