using System.Collections.Generic;
using UnityEngine;

namespace Survivor.CrowdPrototype
{
    [AddComponentMenu("Survivor/Crowd Occluder"), DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
    public sealed class CrowdOccluder : MonoBehaviour
    {
        // Author a box entirely inside opaque solid geometry. A smaller box only reduces culling.
        private static readonly List<CrowdOccluder> Active = new();
        private BoxCollider volume;

        private void OnEnable()
        {
            volume = GetComponent<BoxCollider>();
            Active.Add(this);
        }

        private void OnDisable() => Active.Remove(this);

        internal static int CopyVisible(Plane[] planes, Vector4[] minimum, Vector4[] maximum)
        {
            int count = 0;
            foreach (var item in Active)
            {
                // BoxCollider.bounds is exact for axis-aligned boxes only.
                if (!item.volume.enabled || item.transform.rotation != Quaternion.identity) continue;
                var bounds = item.volume.bounds;
                if (bounds.size.sqrMagnitude == 0 || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                var near = planes[4];
                var normal = near.normal;
                var projectedExtents = Vector3.Dot(bounds.extents,
                    new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)));
                if (near.GetDistanceToPoint(bounds.center) <= projectedExtents) continue;
                minimum[count] = bounds.min;
                maximum[count++] = bounds.max;
                if (count == minimum.Length) break;
            }
            return count;
        }
    }
}
