using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class UpgradePixelSurface : MaskableGraphic
    {
        private readonly Vector2[] outer = new Vector2[12];
        private readonly Vector2[] inner = new Vector2[12];
        private Color fill, high, low;
        private float notch, stroke;
        private bool recessed;

        internal void Paint(Color face, Color lightEdge, Color darkEdge, float corner, float width, bool inset = false)
        {
            fill = face;
            high = lightEdge;
            low = darkEdge;
            notch = corner;
            stroke = width;
            recessed = inset;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            Corners(rect, notch, outer);
            mesh.AddVert(rect.center, Tint(fill), Vector2.zero);
            for (int i = 0; i < outer.Length; i++) mesh.AddVert(outer[i], Tint(fill), Vector2.zero);
            for (int i = 0; i < outer.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % outer.Length + 1);

            if (stroke <= 0) return;
            rect = Rect.MinMaxRect(rect.xMin + stroke, rect.yMin + stroke, rect.xMax - stroke, rect.yMax - stroke);
            if (rect.width <= 0 || rect.height <= 0) return;
            Corners(rect, Mathf.Max(0, notch - stroke), inner);
            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length;
                int first = mesh.currentVertCount;
                bool topOrLeft = i <= 1 || i >= 8;
                Color32 edge = Tint(topOrLeft != recessed ? high : low);
                mesh.AddVert(outer[i], edge, Vector2.zero);
                mesh.AddVert(outer[next], edge, Vector2.zero);
                mesh.AddVert(inner[next], edge, Vector2.zero);
                mesh.AddVert(inner[i], edge, Vector2.zero);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first, first + 2, first + 3);
            }
        }

        private Color32 Tint(Color value) => value * color;

        private static void Corners(Rect r, float corner, Vector2[] p)
        {
            float step = Mathf.Min(corner, Mathf.Min(r.width, r.height) * .25f);
            float l = r.xMin, t = r.yMax, right = r.xMax, b = r.yMin;
            p[0] = new(l + step, t);
            p[1] = new(right - step, t);
            p[2] = new(right - step, t - step);
            p[3] = new(right, t - step);
            p[4] = new(right, b + step);
            p[5] = new(right - step, b + step);
            p[6] = new(right - step, b);
            p[7] = new(l + step, b);
            p[8] = new(l + step, b + step);
            p[9] = new(l, b + step);
            p[10] = new(l, t - step);
            p[11] = new(l + step, t - step);
        }
    }
}

