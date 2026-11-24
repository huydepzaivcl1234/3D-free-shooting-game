using UnityEngine;
namespace Survivor.CrowdPrototype
{
    [CreateAssetMenu(menuName="Survivor/Experimental/GPU Crowd")]
    public sealed class Crowd10KData : ScriptableObject
    {
        public Mesh[] meshes;
        public Texture2D positions;
        public Texture2D normals;
        public Material material;
        public int framesPerClip=32;
        public float[] clipLengths;
        [Min(1)] public int count=10000;
        [Min(.1f)] public float movementSpeed=2;
        [Min(.1f)] public float separationRadius=.43f;
        public float[] lodDistances={14,30,55};
        public float mapHalfSize=65;
        public bool simulateOffscreen=true;
        public bool useIndirectRendering = true;
        public bool useSpeciesCatalog = true;
        [Min(.25f)] public float groundCellSize = 1f;
        [Min(0)] public int groundSamplesPerFrame = 96;
        public LayerMask groundLayers = 1 << 3;
        [Min(0f)] public float maximumClimbHeight = 4f;
    }
}
