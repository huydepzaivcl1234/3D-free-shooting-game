using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiningSimulator.Ores
{
    // TopDownSurvivor moves the camera in LateUpdate at order 10000.
    [DefaultExecutionOrder(10001), DisallowMultipleComponent, RequireComponent(typeof(PlayerStats))]
    public sealed class XPGemManager : MonoBehaviour
    {
        internal static event Action<float> XpCollected;

        internal const int Capacity = 300;
        private const float Hover = .42f;
        private const float PickupRadius = .8f;
        private const float MagnetRadius = 5.5f;
        private const float FallDuration = .55f;

        [Serializable]
        public struct Gem
        {
            public Vector3 position;
            public float xp, pullSpeed;
            public int merges;
            public bool falling;
            public float fallTime, fallStartY, groundY;
            [NonSerialized] public Vector3 trailNear, trailFar;
            [NonSerialized] public bool hasTrail;
        }

        private readonly Gem[] gems = new Gem[Capacity];
        private readonly Matrix4x4[] matrices = new Matrix4x4[Capacity];
        private readonly Matrix4x4[] nearTrail = new Matrix4x4[Capacity];
        private readonly Matrix4x4[] farTrail = new Matrix4x4[Capacity];
        private int count;
        private PlayerStats stats;
        private CharacterHealth health;
        private TerrainGenerator terrain;
        private Mesh mesh;
        private Material material, nearMaterial, farMaterial;
        private Camera view;

        internal static XPGemManager For(PlayerStats player) =>
            player.TryGetComponent<XPGemManager>(out var manager) ? manager : player.gameObject.AddComponent<XPGemManager>();

        private void Awake()
        {
            stats = GetComponent<PlayerStats>();
            health = GetComponent<CharacterHealth>();
            terrain = FindAnyObjectByType<TerrainGenerator>();
        }

        internal void Drop(Vector3 position, float xp)
        {
            if (xp <= 0f || !float.IsFinite(xp)) return;
            float groundY = terrain && terrain.TrySample(position, out var floor, out _)
                ? floor.y + Hover : position.y;
            position.y = Mathf.Max(position.y + .35f, groundY + .85f);
            Store(new Gem { position = position, xp = xp, falling = true, fallStartY = position.y, groundY = groundY });
        }

        private void Store(Gem gem)
        {
            if (count < Capacity)
            {
                gems[count++] = gem;
                return;
            }

            int closest = 0;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float distance = (gems[i].position - gem.position).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                closest = i;
            }
            gems[closest].xp += gem.xp;
            gems[closest].merges += gem.merges + 1;
        }

        public void MagnetizeAllGems()
        {
            for (int i = 0; i < count; i++)
                gems[i].pullSpeed = Mathf.Max(gems[i].pullSpeed, 1.1f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (count == 0 || dt <= 0f || RunCheckpoint.IsRestoring || (health && !health.IsTargetable)) return;
            Vector3 center = transform.position + Vector3.up * .8f;
            float collected = 0f;
            for (int i = count - 1; i >= 0; i--)
            {
                ref Gem gem = ref gems[i];
                if (gem.falling)
                {
                    gem.fallTime = Mathf.Min(FallDuration, gem.fallTime + dt);
                    float t = gem.fallTime / FallDuration;
                    Vector3 previous = gem.position;
                    gem.position.y = Mathf.Lerp(gem.fallStartY, gem.groundY, t * t);
                    UpdateTrail(ref gem, previous, dt);
                    if (t < 1f) continue;
                    gem.falling = false;
                    gem.hasTrail = false;
                }
                float distance = (gem.position - center).sqrMagnitude;
                if (distance > PickupRadius * PickupRadius)
                {
                    if (gem.pullSpeed <= 0f)
                    {
                        if (distance > MagnetRadius * MagnetRadius) continue;
                        gem.pullSpeed = .9f;
                    }
                    Vector3 previous = gem.position;
                    gem.pullSpeed = Mathf.Min(18f, gem.pullSpeed + 14f * dt);
                    gem.position = Vector3.MoveTowards(gem.position, center, gem.pullSpeed * dt);
                    UpdateTrail(ref gem, previous, dt);
                    if ((gem.position - center).sqrMagnitude > PickupRadius * PickupRadius) continue;
                }
                collected += gem.xp;
                gem = gems[--count];
            }
            if (collected > 0f)
            {
                stats.AddExperience(collected);
                XpCollected?.Invoke(collected);
            }
        }

        private static void UpdateTrail(ref Gem gem, Vector3 previous, float dt)
        {
            if (!gem.hasTrail) gem.trailNear = gem.trailFar = previous;
            gem.hasTrail = true;
            gem.trailFar = Vector3.Lerp(gem.trailFar, gem.trailNear, Mathf.Min(1f, dt * 13f));
            gem.trailNear = Vector3.Lerp(gem.trailNear, previous, Mathf.Min(1f, dt * 22f));
        }

        private void LateUpdate()
        {
            if (count == 0) return;
            if (!mesh && !material) CreateVisual();
            if (!view || !view.isActiveAndEnabled) view = Camera.main;
            if (!material || !mesh || !view) return;
            float bob = Mathf.Sin(Time.time * 3f) * .12f;
            Quaternion align = Quaternion.Euler(90f, 0f, 0f);
            int trailCount = 0;
            Vector3 min = gems[0].position, max = min;
            for (int i = 0; i < count; i++)
            {
                ref Gem gem = ref gems[i];
                min = Vector3.Min(min, gem.position);
                max = Vector3.Max(max, gem.position);
                bool moving = gem.falling || gem.pullSpeed > 0f;
                Vector3 position = gem.position + Vector3.up * (moving ? 0f : bob);
                Vector3 cameraDirection = view.transform.position - position;
                var facing = Quaternion.LookRotation(cameraDirection.sqrMagnitude > .0001f ? cameraDirection : -view.transform.forward,
                    view.transform.up);
                float angle = gem.falling ? gem.fallTime / FallDuration * 360f : 0f;
                var rotation = facing * Quaternion.AngleAxis(angle, Vector3.forward) * align;
                float scale = .85f * Mathf.Min(2.2f, 1f + gem.merges * .08f);
                matrices[i] = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
                if (!moving || !gem.hasTrail || (position - gem.trailNear).sqrMagnitude < .0016f) continue;
                nearTrail[trailCount] = Matrix4x4.TRS(gem.trailNear, rotation, Vector3.one * (scale * .73f));
                farTrail[trailCount] = Matrix4x4.TRS(gem.trailFar, rotation, Vector3.one * (scale * .48f));
                min = Vector3.Min(min, gem.trailFar);
                max = Vector3.Max(max, gem.trailFar);
                trailCount++;
            }
            var parameters = new RenderParams(material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                camera = view,
                worldBounds = new Bounds((min + max) * .5f, max - min + Vector3.one * 2f)
            };
            Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count);
            if (trailCount > 0 && nearMaterial && farMaterial)
            {
                parameters.material = farMaterial;
                Graphics.RenderMeshInstanced(parameters, mesh, 0, farTrail, trailCount);
                parameters.material = nearMaterial;
                Graphics.RenderMeshInstanced(parameters, mesh, 0, nearTrail, trailCount);
            }
        }

        private void CreateVisual()
        {
            var shard = Resources.Load<GameObject>("Megabonk_XP_Shard_2D");
            if (!shard) return;
            mesh = shard.GetComponentInChildren<MeshFilter>().sharedMesh;
            var source = shard.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            material = new Material(source) { name = "XP shard billboard", enableInstancing = true };
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit) material.shader = unlit;
            material.SetFloat("_Cull", (float)CullMode.Off);
            nearMaterial = CreateTrailMaterial(.27f);
            farMaterial = CreateTrailMaterial(.12f);
        }

        private Material CreateTrailMaterial(float alpha)
        {
            var result = new Material(material) { name = "XP shard trail", enableInstancing = true };
            result.SetOverrideTag("RenderType", "Transparent");
            result.SetFloat("_Surface", 1f);
            result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            result.SetFloat("_DstBlend", (float)BlendMode.One);
            result.SetFloat("_ZWrite", 0f);
            result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            result.renderQueue = (int)RenderQueue.Transparent;
            var color = result.GetColor("_BaseColor");
            color.a = alpha;
            result.SetColor("_BaseColor", color);
            return result;
        }

        internal List<Gem> Capture()
        {
            var result = new List<Gem>(count);
            for (int i = 0; i < count; i++) result.Add(gems[i]);
            return result;
        }

        internal void Restore(List<Gem> saved)
        {
            count = 0;
            if (saved == null) return;
            foreach (var gem in saved)
                if (gem.xp > 0f && float.IsFinite(gem.xp)) Store(gem);
        }

        internal static void Reground(List<Gem> saved, TerrainGenerator terrain)
        {
            if (saved == null) return;
            for (int i = 0; i < saved.Count; i++)
            {
                var gem = saved[i];
                if (!terrain.TrySample(gem.position, out var floor, out _)) continue;
                gem.position = floor + Vector3.up * Hover;
                gem.falling = false;
                saved[i] = gem;
            }
        }

        private void OnDestroy()
        {
            if (material) Destroy(material);
            if (nearMaterial) Destroy(nearMaterial);
            if (farMaterial) Destroy(farMaterial);
        }
    }
}
