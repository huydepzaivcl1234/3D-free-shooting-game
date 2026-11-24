using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    public sealed partial class MainMenuSkinUIController
    {
        private Coroutine startupTransition, startupPreparation;
        private bool startupPrepared;
        private MonsterSpawnZone startupStage;
        private Image startupCover;
        private bool gameplayViewRestored;
        private Vector2 homePosition, walletPosition;

        private IEnumerator PrepareStartupWorld()
        {
            yield return null;
            CreateStartupCover();
            var mapFeatures = terrain.GetComponent<MapFeaturePopulator>();
            startupStage = FindAnyObjectByType<MonsterSpawnZone>();
            while (!terrain.HasLayout || RunCheckpoint.IsRestoring ||
                startupStage && startupStage.IsRestoringWorld ||
                mapFeatures && mapFeatures.isActiveAndEnabled && !mapFeatures.PopulationReady ||
                WorldNavigationGrid.Instance && !WorldNavigationGrid.Instance.HasBaked)
                yield return null;

            var woodLoot = FindAnyObjectByType<GameData.Player.Script.SurvivorDailyLoot>();
            while (woodLoot && !woodLoot.PrepareStartupStep()) yield return null;
            if (woodLoot && woodLoot.TryGetComponent<GameData.Player.Script.SurvivorAbilityLoot>(out var blueLoot))
                while (!blueLoot.PrepareStartupStep()) yield return null;
            for (int attempt = 0; startupStage && attempt < 3 && !startupStage.TryPrepareOpeningWave(); attempt++)
                yield return null;
            startupPrepared = true;
            startupPreparation = null;
        }

        internal bool TryPlayStartupTransition(MainMenuData data, Action completed)
        {
            if (!IsPresentationReady || page != Page.MainMenu || !followWasEnabled || startupTransition != null) return false;

            var homeRect = (RectTransform)home.transform;
            var walletRect = (RectTransform)currencies.transform;
            homePosition = homeRect.anchoredPosition;
            walletPosition = walletRect.anchoredPosition;
            home.interactable = home.blocksRaycasts = false;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            ShowStartupCover();
            startupTransition = StartCoroutine(PlayStartupTransition(data, homeRect, walletRect, completed));
            return true;
        }

        private IEnumerator PlayStartupTransition(MainMenuData data, RectTransform homeRect,
            RectTransform walletRect, Action completed)
        {
            var menuCameraPosition = view.transform.position;
            var menuCameraRotation = view.transform.rotation;
            float menuFov = view.fieldOfView;
            float slideDistance = data.PlaySlideDistance;
            float zoomDuration = data.StartupZoomIn;
            float slideDuration = Mathf.Min(data.PlaySlideDuration, zoomDuration);
            float retreatStart = zoomDuration * .27f;
            float elapsed = 0f;
            bool retreating = false;
            // Yield the click frame so an Editor hitch cannot skip the opening shot.
            yield return null;
            while (elapsed < zoomDuration)
            {
                elapsed = Mathf.Min(zoomDuration, elapsed + Mathf.Min(Time.unscaledDeltaTime, .05f));
                float slide = Mathf.Clamp01(elapsed / slideDuration);
                slide *= slide;
                homeRect.anchoredPosition = homePosition - Vector2.right * (slide * slideDistance);
                walletRect.anchoredPosition = walletPosition + Vector2.right * (slide * slideDistance);
                float zoom = Mathf.SmoothStep(0f, 1f, elapsed / zoomDuration);
                float retreat = Mathf.InverseLerp(retreatStart, zoomDuration, elapsed);
                if (!retreating && elapsed >= retreatStart && data.StartupBackstepDistance > 0f)
                {
                    retreating = true;
                    diorama.BeginStartupBackstep();
                }

                diorama.Tick(Mathf.Lerp(5.8f, 3.15f, zoom));
                var subject = diorama.ApplyStartupBackstep(data.StartupBackstepDistance * retreat);
                var close = subject + Vector3.up * 1.04f - menuCameraRotation * Vector3.forward * 3.15f;
                view.transform.SetPositionAndRotation(Vector3.Lerp(menuCameraPosition, close, zoom), menuCameraRotation);
                view.fieldOfView = Mathf.Lerp(menuFov, 29f, zoom);
                SetStartupCover(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.43f, .98f, elapsed / zoomDuration)));
                yield return null;
            }

            // Switch cameras and lighting while the frame is opaque. The menu still owns pause.
            while (!startupPrepared) yield return null;
            frame.gameObject.SetActive(false);
            RestoreGameplayView(false);
            follow.SetStartupRevealPose(1f);
            yield return null;
            follow.SetStartupRevealPose(0f);
            yield return null;

            elapsed = 0f;
            float revealDuration = data.StartupZoomOut;
            while (elapsed < revealDuration)
            {
                elapsed = Mathf.Min(revealDuration, elapsed + Mathf.Min(Time.unscaledDeltaTime, .05f));
                float progress = elapsed / revealDuration;
                follow.SetStartupRevealPose(Mathf.SmoothStep(0f, 1f, progress));
                SetStartupCover(1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.04f, .7f, progress)));
                yield return null;
            }

            startupTransition = null;
            completed();
        }

        private void RestoreGameplayView(bool restoreCameraPose)
        {
            if (gameplayViewRestored) return;
            gameplayViewRestored = true;
            if (diorama) diorama.End();
            if (!view) return;
            view.cullingMask = oldMask;
            if (!restoreCameraPose) return;
            view.transform.SetPositionAndRotation(savedPosition, savedRotation);
            view.fieldOfView = oldFov;
        }

        private void ShowStartupCover()
        {
            CreateStartupCover();
            SetStartupCover(0f);
            startupCover.gameObject.SetActive(true);
        }

        private void CreateStartupCover()
        {
            if (!startupCover)
            {
                startupCover = Flat(transform, "Startup fade", 0, 0, 0, 0, Color.clear);
                var rect = startupCover.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var coverCanvas = startupCover.gameObject.AddComponent<Canvas>();
                coverCanvas.overrideSorting = true;
                coverCanvas.sortingOrder = short.MaxValue;
                startupCover.gameObject.SetActive(false);
            }
        }

        private void SetStartupCover(float opacity) => startupCover.color = new Color(0f, 0f, 0f, opacity);

        private void StopStartupTransition()
        {
            if (!startupCover || !startupCover.gameObject.activeSelf) return;
            if (startupTransition != null) StopCoroutine(startupTransition);
            startupTransition = null;
            startupCover.gameObject.SetActive(false);
            ((RectTransform)home.transform).anchoredPosition = homePosition;
            ((RectTransform)currencies.transform).anchoredPosition = walletPosition;
            frame.gameObject.SetActive(true);
        }
    }
}
