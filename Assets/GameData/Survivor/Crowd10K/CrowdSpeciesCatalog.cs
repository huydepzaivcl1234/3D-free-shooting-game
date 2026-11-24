using System;
using UnityEngine;

namespace Survivor.CrowdPrototype
{
    [CreateAssetMenu(menuName = "Survivor/Crowd Species Catalog")]
    public sealed class CrowdSpeciesCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Species
        {
            public UnityEngine.Object gameData;
            public string prefabName;
            public Crowd10KData visuals;
            public string[] rendererPaths;
        }

        public Species[] species = Array.Empty<Species>();
    }
}
