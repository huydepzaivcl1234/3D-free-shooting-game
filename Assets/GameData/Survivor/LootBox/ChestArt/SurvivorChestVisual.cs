using UnityEngine;

namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    public sealed class SurvivorChestVisual : MonoBehaviour
    {
        [SerializeField] private Animation openingVisual;
        [SerializeField] private ParticleSystem openingBurst;

        public void PlayOpening()
        {
            if (!openingVisual || openingVisual.transform.parent != transform)
                return;

            openingVisual.transform.SetParent(null, true);
            openingVisual.Play();
            if (openingBurst)
                openingBurst.Play();

            Destroy(openingVisual.gameObject, 0.8f);
        }
    }
}
