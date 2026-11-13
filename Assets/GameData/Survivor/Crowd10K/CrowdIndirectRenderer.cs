using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Survivor.CrowdPrototype
{
    internal sealed class CrowdIndirectRenderer : IDisposable
    {
        internal struct Instance
        {
            public Matrix4x4 transform;
            public Vector4 animation, tint, sphere;
        }

        private readonly ComputeShader shader;
        private readonly Material material;
        private readonly ComputeBuffer instances;
        private readonly ComputeBuffer[] visible = new ComputeBuffer[4];
        private readonly ComputeBuffer[] arguments = new ComputeBuffer[4];
        private readonly MaterialPropertyBlock[] properties = new MaterialPropertyBlock[4];
        private readonly Vector4[] planes = new Vector4[6];
        private readonly Vector4[] occluderMin = new Vector4[32], occluderMax = new Vector4[32];
        private readonly Plane[] frustum = new Plane[6];
        private readonly int kernel;
        private readonly Crowd10KData data;
        internal readonly Instance[] Input;

        internal static CrowdIndirectRenderer Create(Crowd10KData data, int capacity)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsInstancing ||
                !SystemInfo.supportsIndirectArgumentsBuffer) return null;
            var source = Resources.Load<ComputeShader>("SurvivorCrowdVisibility");
            if (!source || !source.HasKernel("Cull") || !source.IsSupported(source.FindKernel("Cull")) || !data.material.shader.isSupported) return null;
            return new CrowdIndirectRenderer(data, source, capacity);
        }

        private CrowdIndirectRenderer(Crowd10KData data, ComputeShader source, int capacity)
        {
            this.data = data;
            shader = UnityEngine.Object.Instantiate(source);
            kernel = shader.FindKernel("Cull");
            material = new Material(data.material) { enableInstancing = true };
            material.EnableKeyword("CROWD_INDIRECT");
            Input = new Instance[capacity];
            instances = new ComputeBuffer(capacity, 112);
            for (int lod = 0; lod < 4; lod++)
            {
                var mesh = data.meshes[lod];
                visible[lod] = new ComputeBuffer(capacity, 8, ComputeBufferType.Append);
                arguments[lod] = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
                arguments[lod].SetData(new[] { mesh.GetIndexCount(0), 0u, mesh.GetIndexStart(0), (uint)mesh.GetBaseVertex(0), 0u });
                properties[lod] = new MaterialPropertyBlock();
                properties[lod].SetBuffer("_CrowdInstances", instances);
                properties[lod].SetBuffer("_CrowdVisible", visible[lod]);
                shader.SetBuffer(kernel, "_Visible" + lod, visible[lod]);
            }
            shader.SetBuffer(kernel, "_CrowdInstances", instances);
        }

        internal void Draw(int count, Camera camera, Bounds bounds, int layer, bool shadows)
        {
            if (count == 0) return;
            instances.SetData(Input, 0, 0, count);
            GeometryUtility.CalculateFrustumPlanes(camera, frustum);
            for (int i = 0; i < frustum.Length; i++)
            {
                var normal = frustum[i].normal;
                planes[i] = new Vector4(normal.x, normal.y, normal.z, frustum[i].distance);
            }
            shader.SetVectorArray("_Planes", planes);
            shader.SetInt("_Count", count);
            shader.SetVector("_Eye", camera.transform.position);
            shader.SetVector("_Forward", camera.transform.forward);
            shader.SetInt("_Orthographic", camera.orthographic ? 1 : 0);
            shader.SetVector("_LodDistances", new Vector4(data.lodDistances[0], data.lodDistances[1], data.lodDistances[2], 0));
            int occluderCount = CrowdOccluder.CopyVisible(frustum, occluderMin, occluderMax);
            shader.SetInt("_OccluderCount", occluderCount);
            if (occluderCount != 0)
            {
                shader.SetVectorArray("_OccluderMin", occluderMin);
                shader.SetVectorArray("_OccluderMax", occluderMax);
            }

            foreach (var buffer in visible) buffer.SetCounterValue(0);
            shader.Dispatch(kernel, (count + 127) / 128, 1, 1);
            for (int lod = 0; lod < 4; lod++)
            {
                ComputeBuffer.CopyCount(visible[lod], arguments[lod], sizeof(uint));
                Graphics.DrawMeshInstancedIndirect(data.meshes[lod], 0, material, bounds, arguments[lod], 0,
                    properties[lod], shadows && lod < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    true, layer, camera);
            }
        }

        public void Dispose()
        {
            instances.Release();
            foreach (var buffer in visible) buffer.Release();
            foreach (var buffer in arguments) buffer.Release();
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(shader);
        }
    }
}
