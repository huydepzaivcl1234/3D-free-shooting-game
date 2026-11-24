using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    /// <summary>Plays the configured shared UI sound when this button is clicked.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class MiningButtonSfx : MonoBehaviour
    {
        [SerializeField] private MiningAudioManager audioManager;

        private Button button;
        internal bool UseMainMenuSounds { get; set; }

        private void Awake()
        {
            button = GetComponent<Button>();
            ResolveAudioManager();
        }

        private void OnEnable()
        {
            ResolveAudioManager();
            RefreshBinding();
        }

        /// <summary>Restores the listener after a runtime-authored Button replaces its callbacks.</summary>
        public void RefreshBinding()
        {
            button ??= GetComponent<Button>();
            button.onClick.RemoveListener(Play);
            button.onClick.AddListener(Play);
        }

        private void OnDisable()
        {
            button?.onClick.RemoveListener(Play);
        }

        internal void PlayHover()
        {
            var clip = UseMainMenuSounds && audioManager && audioManager.AudioData
                ? audioManager.AudioData.MainMenuHoverSfx : null;
            if (clip) audioManager.PlaySfx(clip, .62f);
            else audioManager?.PlayCardHoverSfx();
        }

        private void Play()
        {
            var clip = UseMainMenuSounds && audioManager && audioManager.AudioData
                ? audioManager.AudioData.MainMenuClickSfx : null;
            if (clip) audioManager.PlaySfx(clip, .8f);
            else audioManager?.PlayButtonSfx();
        }

        /// <summary>Allows runtime-authored buttons to use the shared SFX source immediately.</summary>
        public void Configure(MiningAudioManager manager)
        {
            audioManager = manager;
            if (isActiveAndEnabled) RefreshBinding();
        }

        private void ResolveAudioManager()
        {
            audioManager ??= FindFirstObjectByType<MiningAudioManager>(FindObjectsInactive.Include);
        }
    }
}
