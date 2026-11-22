using System.Collections;
using UnityEngine;

namespace MiningSimulator.Ores
{
    public sealed partial class MiningAudioManager
    {
        private AudioClip runeAmbience;
        private bool runeAmbienceActive;
        private AudioSource overtimeSource;
        private bool overtimePaused;
        private float overtimeFadeElapsed;
        private bool OvertimeIntroActive => overtimeSource && overtimeSource.clip;
        public void BeginRuneAmbience(AudioClip clip)
        {
            runeAmbience = clip;
            runeAmbienceActive = clip != null;
            if (!runeAmbienceActive || musicMuted || ambienceSource == null) return;
            StopAmbiencePlaylist();
            StopWorldAmbienceSwitch();
            PlayAmbienceClip(clip, true);
            ambienceSource.loop = true;
        }
        public void EndRuneAmbience()
        {
            if (!runeAmbienceActive) return;
            runeAmbienceActive = false;
            runeAmbience = null;
            if (!mainMenuMusicActive && !shopThemeActive) PlayWorldAmbience(true);
        }
        public void PlayWorldAmbience()
        {
            PlayWorldAmbience(true);
        }

        internal void PlayOvertimeIntro(AudioClip clip)
        {
            if (!audioData || !ambienceSource || OvertimeIntroActive || !EnsureClipLoaded(clip)) return;
            StopAmbiencePlaylist();
            StopWorldAmbienceSwitch();

            overtimeSource = ResolveSource(overtimeSource, ambienceSource.transform.parent, "Overtime Music Source");
            overtimeSource.playOnAwake = false;
            overtimeSource.loop = false;
            overtimeSource.spatialBlend = 0f;
            overtimeSource.outputAudioMixerGroup = audioData.MusicMixerGroup;
            overtimeSource.mute = musicMuted;
            overtimeSource.clip = clip;
            overtimeFadeElapsed = 0f;
            overtimePaused = false;
            overtimeSource.volume = audioData.AmbienceFadeDuration > 0f ? 0f : GetMusicVolume();
            overtimeSource.Play();
            FadeAmbienceTo(0f, true);
        }

        private void TickOvertimeIntro()
        {
            // Listener pause already freezes the source. Handle menu and time-scale pauses separately.
            if (!OvertimeIntroActive || AudioListener.pause) return;
            bool shouldPause = Time.timeScale <= 0f || mainMenuMusicActive || shopThemeActive ||
                runeAmbienceActive || upgradeThemeActive;
            if (shouldPause)
            {
                if (!overtimePaused) overtimeSource.Pause();
                overtimePaused = true;
                return;
            }
            if (overtimePaused)
            {
                overtimeSource.UnPause();
                overtimePaused = false;
                return;
            }

            if (!overtimeSource.isPlaying)
            {
                StopOvertimeIntro();
                PlayWorldAmbience(true);
                return;
            }

            overtimeFadeElapsed += Time.unscaledDeltaTime;
            float duration = audioData.AmbienceFadeDuration;
            float progress = duration > 0f ? Mathf.Clamp01(overtimeFadeElapsed / duration) : 1f;
            overtimeSource.volume = GetMusicVolume() * (.5f - .5f * Mathf.Cos(progress * Mathf.PI));
        }

        private void StopOvertimeIntro()
        {
            if (!overtimeSource) return;
            overtimeSource.Stop();
            overtimeSource.clip = null;
            overtimePaused = false;
        }

        private void PlayWorldAmbience(bool fadeIn)
        {
            if (runeAmbienceActive) { BeginRuneAmbience(runeAmbience); return; }
            mainMenuMusicActive = false;
            shopThemeActive = false;
            if (!upgradeThemeActive && musicSource != null && musicSource.isPlaying)
            {
                FadeMusicTo(0f, true);
            }

            if (OvertimeIntroActive || audioData == null || ambienceSource == null || musicMuted)
            {
                return;
            }

            bool isNight = dayNightSystem != null &&
                dayNightSystem.CurrentPeriod == MiningTimePeriod.Night;
            StopAmbiencePlaylist();

            AudioClip ambience = isNight
                ? audioData.GetRandomNightAmbience(currentPlaylistAmbience)
                : audioData.GetRandomMorningAmbience(currentPlaylistAmbience);
            ambience ??= audioData.BackgroundMusic;
            if (!EnsureClipLoaded(ambience))
            {
                return;
            }

            PlayAmbienceClip(ambience, fadeIn);
            currentPlaylistAmbience = ambience;
            ambiencePlaylist = StartCoroutine(PlayAmbiencePlaylist(isNight));
        }

        private void PlayAmbienceClip(AudioClip clip, bool fadeIn)
        {
            PlayAmbienceClip(clip, fadeIn, GetAmbienceVolume());
        }

        private void PlayAmbienceClip(AudioClip clip, bool fadeIn, float targetVolume)
        {
            ConfigureSources();
            StopAmbienceFade();
            ambienceSource.loop = false;
            ambienceSource.clip = clip;
            if (fadeIn && audioData.AmbienceFadeDuration > 0f)
            {
                ambienceSource.volume = 0f;
            }

            ambienceSource.Play();
            if (fadeIn)
            {
                FadeAmbienceTo(targetVolume, false);
            }
            else
            {
                ambienceSource.volume = targetVolume;
            }
        }

        private IEnumerator PlayAmbiencePlaylist(bool isNight)
        {
            while (audioData != null && ambienceSource != null && !musicMuted &&
                   !mainMenuMusicActive && !shopThemeActive && dayNightSystem != null &&
                   (dayNightSystem.CurrentPeriod == MiningTimePeriod.Night) == isNight)
            {
                // The card panel pauses the listener, which also makes isPlaying false.
                while (AudioListener.pause || ambienceSource.isPlaying)
                {
                    yield return null;
                }

                float pause = isNight
                    ? audioData.GetRandomNightAmbiencePause()
                    : audioData.GetRandomMorningAmbiencePause();
                if (pause > 0f)
                {
                    yield return new WaitForSecondsRealtime(pause);
                }

                while (AudioListener.pause)
                {
                    yield return null;
                }

                if (mainMenuMusicActive || shopThemeActive || dayNightSystem == null ||
                    (dayNightSystem.CurrentPeriod == MiningTimePeriod.Night) != isNight)
                {
                    yield break;
                }

                AudioClip nextClip = isNight
                    ? audioData.GetRandomNightAmbience(currentPlaylistAmbience)
                    : audioData.GetRandomMorningAmbience(currentPlaylistAmbience);
                if (!EnsureClipLoaded(nextClip))
                {
                    yield break;
                }

                currentPlaylistAmbience = nextClip;
                PlayAmbienceClip(nextClip, true);
            }

            ambiencePlaylist = null;
        }

        private void StopAmbiencePlaylist()
        {
            if (ambiencePlaylist != null)
            {
                StopCoroutine(ambiencePlaylist);
                ambiencePlaylist = null;
            }
        }

    }
}
