using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiningSimulator.Ores
{
    internal sealed class SlopeVisualLean
    {
        private const float TurnSpeed = 280f;
        private static readonly List<SlopeVisualLean> active = new();

        private struct Branch
        {
            public Transform Transform;
            public Pose Original;
        }

        private readonly Transform actor;
        private readonly Branch[] branches;
        private Quaternion tilt = Quaternion.identity;
        private int index = -1;
        private bool applied;

        internal SlopeVisualLean(Transform actor)
        {
            this.actor = actor;
            var roots = new HashSet<Transform>();
            foreach (var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bone = skin.rootBone;
                if (!bone || bone == actor || !bone.IsChildOf(actor)) continue;
                while (bone.parent != actor) bone = bone.parent;
                roots.Add(bone);

                // Some rigs keep the skin renderers beside the skeleton rather than under it.
                var mesh = skin.transform;
                if (mesh == actor) continue;
                while (mesh.parent != actor) mesh = mesh.parent;
                roots.Add(mesh);
            }

            if (roots.Count == 0)
            {
                var animator = actor.GetComponentInChildren<Animator>();
                if (animator && animator.transform != actor) roots.Add(animator.transform);
            }

            branches = new Branch[roots.Count];
            int i = 0;
            foreach (var root in roots) branches[i++].Transform = root;
        }

        internal void Update(Vector3 normal)
        {
            if (branches.Length == 0) return;
            bool isSlope = normal.y is >= .5f and < .9999f;
            if (!isSlope && index < 0) return;
            var target = isSlope ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
            tilt = Quaternion.RotateTowards(tilt, target, TurnSpeed * Time.deltaTime);
            if (!isSlope && tilt == Quaternion.identity)
            {
                Reset();
                return;
            }

            if (index >= 0 || tilt == Quaternion.identity) return;
            index = active.Count;
            active.Add(this);
        }

        internal void Reset()
        {
            if (index < 0) return;
            Restore();
            int last = active.Count - 1;
            active[index] = active[last];
            active[index].index = index;
            active.RemoveAt(last);
            index = -1;
            tilt = Quaternion.identity;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterRenderCallbacks()
        {
            while (active.Count > 0) active[active.Count - 1].Reset();
            RenderPipelineManager.beginContextRendering -= ApplyForRendering;
            RenderPipelineManager.endContextRendering -= RestoreAfterRendering;
            RenderPipelineManager.beginContextRendering += ApplyForRendering;
            RenderPipelineManager.endContextRendering += RestoreAfterRendering;
        }

        // Animation, aiming and ragdolls read the unmodified rig outside rendering.
        private static void ApplyForRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var lean in active) lean.Apply();
        }

        private static void RestoreAfterRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            foreach (var lean in active) lean.Restore();
        }

        private void Apply()
        {
            if (applied || !actor) return;
            Vector3 pivot = actor.position;
            for (int i = 0; i < branches.Length; i++)
            {
                ref var branch = ref branches[i];
                var model = branch.Transform;
                if (!model) continue;
                branch.Original = new Pose(model.localPosition, model.localRotation);
                model.SetPositionAndRotation(pivot + tilt * (model.position - pivot),
                    tilt * model.rotation);
            }
            applied = true;
        }

        private void Restore()
        {
            if (!applied) return;
            foreach (var branch in branches)
                if (branch.Transform)
                    branch.Transform.SetLocalPositionAndRotation(branch.Original.position, branch.Original.rotation);
            applied = false;
        }
    }
}
