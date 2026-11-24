using System.Collections.Generic;
using MiningSimulator.Ores;
using UnityEngine;

namespace Survivor.CrowdPrototype
{
    internal sealed class LiveCrowdSpeciesVisual
    {
        private static CrowdSpeciesBatch batches;
        private static readonly Dictionary<UnityEngine.Object, CrowdSpeciesCatalog.Species> entries = new();
        private static bool catalogChecked;
        private readonly MushroomMonster monster;
        private readonly GodBossController god;
        private readonly Crowd10KData data;
        private readonly Renderer[] renderers;
        private readonly bool[] previousForceOff;

        private LiveCrowdSpeciesVisual(MushroomMonster monster, GodBossController god,
            CrowdSpeciesCatalog.Species entry, Renderer[] renderers)
        {
            this.monster = monster;
            this.god = god;
            data = entry.visuals;
            this.renderers = renderers;
            previousForceOff = new bool[renderers.Length];
        }

        internal static LiveCrowdSpeciesVisual Bind(GameObject owner)
        {
            owner.TryGetComponent(out MushroomMonster monster);
            owner.TryGetComponent(out GodBossController god);
            if (!monster && !god) return null;
            LoadCatalog();
            UnityEngine.Object key = monster ? monster.SpeciesData : god.Data;
            if (!key || !entries.TryGetValue(key, out var entry)) return null;

            var renderers = new Renderer[entry.rendererPaths.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                string path = entry.rendererPaths[i];
                var child = string.IsNullOrEmpty(path) ? owner.transform : owner.transform.Find(path);
                if (!child || !child.TryGetComponent(out renderers[i])) return null;
            }
            return new LiveCrowdSpeciesVisual(monster, god, entry, renderers);
        }

        private static void LoadCatalog()
        {
            if (catalogChecked) return;
            catalogChecked = true;
            var catalog = Resources.Load<CrowdSpeciesCatalog>("CrowdSpeciesCatalog");
            if (!catalog) return;
            foreach (var entry in catalog.species)
                if (entry.gameData && entry.visuals) entries[entry.gameData] = entry;
        }

        internal static bool Begin()
        {
            LoadCatalog();
            if (entries.Count == 0) return false;
            batches ??= new CrowdSpeciesBatch(32);
            batches.Begin();
            return true;
        }

        internal static void Draw(Camera camera)
        {
            var bounds = new Bounds(camera.transform.position, Vector3.one * (2f * camera.farClipPlane + 10f));
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            batches.Draw(camera, bounds, Mathf.Max(0, enemyLayer), false);
        }

        internal static void Reset()
        {
            DisposeBatches();
            entries.Clear();
            catalogChecked = false;
        }

        internal static void DisposeBatches()
        {
            batches?.Dispose();
            batches = null;
        }

        internal bool Submit(Camera camera, float distance, bool alreadyBatched)
        {
            Transform actor = monster ? monster.transform : god.transform;
            float limit = alreadyBatched ? distance * .9f : distance;
            if ((actor.position - camera.transform.position).sqrMagnitude < limit * limit) return false;

            int pose;
            Color tint = Color.clear;
            if (monster)
            {
                if (!monster.TryGetCrowdPose(out pose)) return false;
                if (monster.IsOvertimeGhost) tint = monster.GhostCrowdTint;
            }
            else
            {
                if (god.CurrentAction != GodBossController.Action.Chase || !god.Health || god.Health.Health <= 0f)
                    return false;
                pose = 1;
            }

            for (int i = 0; i < renderers.Length; i++)
                if (!renderers[i] || !renderers[i].enabled || !renderers[i].gameObject.activeInHierarchy ||
                    (alreadyBatched ? previousForceOff[i] : renderers[i].forceRenderingOff))
                    return false;

            return batches.Add(data, actor.localToWorldMatrix, pose, actor.GetHashCode(), tint);
        }

        internal void SetHidden(bool value)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer) continue;
                if (value) previousForceOff[i] = renderer.forceRenderingOff;
                renderer.forceRenderingOff = value || previousForceOff[i];
            }
        }
    }
}
