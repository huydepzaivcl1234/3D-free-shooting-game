using System;
using System.Collections.Generic;
using UnityEngine;

namespace Survivor.CrowdPrototype
{
    internal sealed class CrowdSpeciesBatch : IDisposable
    {
        private sealed class Batch
        {
            public CrowdIndirectRenderer renderer;
            public int count;
        }

        private readonly Dictionary<Crowd10KData, Batch> batches = new();
        private readonly int initialCapacity;
        internal int ActiveSpecies { get; private set; }

        internal CrowdSpeciesBatch(int initialCapacity) => this.initialCapacity = initialCapacity;

        internal void Begin()
        {
            ActiveSpecies = 0;
            foreach (var batch in batches.Values) batch.count = 0;
        }

        internal bool Add(Crowd10KData data, Matrix4x4 matrix, int pose, int phase, Color tint)
        {
            if (!data || !data.material || data.meshes.Length != 4) return false;
            if (!batches.TryGetValue(data, out var batch))
            {
                batch = new Batch { renderer = CrowdIndirectRenderer.Create(data, initialCapacity) };
                batches.Add(data, batch);
            }
            if (batch.renderer == null) return false;
            if (batch.count == batch.renderer.Input.Length)
            {
                var expanded = CrowdIndirectRenderer.Create(data, batch.count * 2);
                if (expanded == null) return false;
                Array.Copy(batch.renderer.Input, expanded.Input, batch.count);
                batch.renderer.Dispose();
                batch.renderer = expanded;
            }

            if (batch.count == 0) ActiveSpecies++;
            var bounds = data.meshes[0].bounds;
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            float scale = Mathf.Max(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
            batch.renderer.Input[batch.count++] = new CrowdIndirectRenderer.Instance
            {
                transform = matrix,
                animation = new Vector4(pose * data.framesPerClip, data.framesPerClip, (phase & 7) / 8f,
                    1f / data.clipLengths[pose]),
                tint = tint,
                sphere = new Vector4(center.x, center.y, center.z, bounds.extents.magnitude * scale)
            };
            return true;
        }

        internal void Draw(Camera camera, Bounds bounds, int layer, bool shadows)
        {
            foreach (var batch in batches.Values)
                if (batch.count > 0) batch.renderer.Draw(batch.count, camera, bounds, layer, shadows);
        }

        public void Dispose()
        {
            foreach (var batch in batches.Values) batch.renderer?.Dispose();
            batches.Clear();
        }
    }
}
