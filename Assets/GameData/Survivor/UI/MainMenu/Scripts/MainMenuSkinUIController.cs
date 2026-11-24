using GameData.Player.Script;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

namespace MiningSimulator.Ores
{
    [DefaultExecutionOrder(80)]
    public sealed partial class MainMenuSkinUIController : MonoBehaviour
    {
        private enum Page { MainMenu, SkinSelect, Quests, Settings, Gameplay }

        [SerializeField] private UIStyle style;
        [SerializeField] private CharacterSkinCatalog skinCatalog;
        private CharacterSkinData[] skins => skinCatalog ? skinCatalog.skins : System.Array.Empty<CharacterSkinData>();
        [SerializeField] private Material mainMaterial;
        [SerializeField] private Camera view;
        [SerializeField] private PlayerWallet wallet;
        [SerializeField] private TopDownSurvivor follow;
        [SerializeField] private TerrainGenerator terrain;
        [SerializeField] private RectTransform statsShortcut;

        private Page page = Page.Gameplay;
        private CharacterSkinManager collection;
        private CharacterSkinData selected;
        private MenuDiorama diorama;
        private Renderer[] menuRenderers;
        private AsyncOperation sceneLoad;
        private Coroutine stageBinding;
        internal bool IsPresentationReady => diorama && diorama.gameObject.activeInHierarchy;
        private Renderer[] playerRenderers;
        private PlayerInput input;
        private Vector3 savedPosition, cameraVelocity;
        private Quaternion savedRotation;
        private float oldFov;
        private int oldMask;
        private bool followWasEnabled, inputWasActive;
        private bool ready, snapCamera, restoreFocus, statsWasVisible;
        [SerializeField] private CharacterTurntableRotator rotator;

        internal bool Initialize(GameMainMenu menu)
        {
            if (ready) return true;
            if (!style || skins == null || skins.Length == 0 || !mainMaterial ||
                !view || !wallet || !follow || !terrain || !frame)
            {
                Debug.LogWarning("Capsule Bonk menu is missing scene references. Run Survivor > Menu > Auto-Bind Scene References.", this);
                return false;
            }

            input = follow.GetComponent<PlayerInput>();
            playerRenderers = follow.GetComponentsInChildren<Renderer>(true);
            collection = new CharacterSkinManager(skins, wallet, mainMaterial);
            collection.ApplySkin(collection.Equipped, playerRenderers);
            BindLayout(menu);
            BindMenuPages(menu.SharedSettings);
            ready = true;
            return true;
        }

        private void OnEnable()
        {
            if (ready) BeginMenu();
        }

        private void Start()
        {
            if (ready && page == Page.Gameplay) BeginMenu();
        }

        private void BeginMenu()
        {
            page = Page.MainMenu;
            gameplayViewRestored = false;
            selected = collection.Equipped;
            wallet.MoneyChanged += WalletChanged;
            wallet.GemsChanged += WalletChanged;
            MiningLocalization.LanguageChanged += RefreshSkin;
            collection.OnEquippedChanged += ApplyEquipped;
            savedPosition = view.transform.position;
            savedRotation = view.transform.rotation;
            oldFov = view.fieldOfView;
            oldMask = view.cullingMask;
            followWasEnabled = follow.enabled;
            inputWasActive = input && input.inputIsActive;
            follow.enabled = false;
            if (inputWasActive) input.DeactivateInput();
            statsWasVisible = statsShortcut && statsShortcut.gameObject.activeSelf;
            if (statsWasVisible) statsShortcut.gameObject.SetActive(false);
            view.cullingMask = LayerMask.GetMask(MenuDiorama.LayerName, "UI");
            stageBinding = StartCoroutine(BindStage());
            ShowPage(false, true);
            SetPlayLabel(false);
            RefreshWallet();
            restoreFocus = true;
            snapCamera = true;
            if (!startupPrepared) startupPreparation = StartCoroutine(PrepareStartupWorld());
        }

        private IEnumerator BindStage()
        {
            var scene = SceneManager.GetSceneByPath(MenuDiorama.ScenePath);
            if (!scene.isLoaded)
            {
                sceneLoad ??= SceneManager.LoadSceneAsync(MenuDiorama.ScenePath, LoadSceneMode.Additive);
                yield return sceneLoad;
                scene = SceneManager.GetSceneByPath(MenuDiorama.ScenePath);
            }

            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out diorama)) break;

            if (!diorama || !diorama.IsBound || !diorama.Avatar)
            {
                Debug.LogError("MainMenu scene is missing its menu stage or CapsuleBoy rig.", this);
                yield break;
            }

            menuRenderers = diorama.Avatar.GetComponentsInChildren<Renderer>(true);
            collection.ApplySkin(selected, menuRenderers);
            diorama.Begin(view);
            rotator.Turntable = diorama.Turntable;
            snapCamera = true;
        }

        private void OnDisable()
        {
            if (!ready || page == Page.Gameplay) return;
            if (stageBinding != null) StopCoroutine(stageBinding);
            stageBinding = null;
            StopStartupTransition();
            if (startupPreparation != null) StopCoroutine(startupPreparation);
            startupPreparation = null;
            CloseMenuPages();
            page = Page.Gameplay;
            wallet.MoneyChanged -= WalletChanged;
            wallet.GemsChanged -= WalletChanged;
            MiningLocalization.LanguageChanged -= RefreshSkin;
            collection.OnEquippedChanged -= ApplyEquipped;
            collection.ApplySkin(collection.Equipped, playerRenderers);
            RestoreGameplayView(true);
            if (follow) follow.enabled = followWasEnabled;
            if (inputWasActive && input) input.ActivateInput();
            if (statsWasVisible && statsShortcut) statsShortcut.gameObject.SetActive(true);
            CloseConfirmation();
        }

        private void OnDestroy()
        {
            ReleaseArt();
        }

        internal bool HandleEscape()
        {
            if (page == Page.Quests) { ExitMenuPage(questButton); return true; }
            if (page == Page.Settings) { menuSettings.CloseFromMenu(); return true; }
            if (page != Page.SkinSelect) return false;
            if (confirmation.gameObject.activeSelf) CloseConfirmation();
            else ExitSkins();
            return true;
        }

        private void EnterSkins()
        {
            if (!IsPresentationReady) return;
            page = Page.SkinSelect;
            Choose(collection.Equipped);
            ShowPage(true);
            Focus(backButton);
        }

        private void ExitSkins()
        {
            page = Page.MainMenu;
            collection.ApplySkin(collection.Equipped, menuRenderers);
            rotator.ResetTurn();
            ShowPage(false);
            Focus(skinsButton);
        }

        private void LateUpdate()
        {
            if (!ready || page == Page.Gameplay || startupTransition != null) return;
            FitAndFade();
            if (page == Page.SkinSelect) rotator.Step();
            if (page == Page.Quests) TickQuestPage();
            if (restoreFocus) { restoreFocus = false; Focus(playButton); }

            if (!IsPresentationReady) return;
            bool isSkins = page == Page.SkinSelect;
            var bounds = ((RectTransform)transform).rect;
            float scale = bounds.height / Mathf.Max(1f, 1080f * frame.localScale.y);
            float shift = page is Page.MainMenu or Page.Settings ? 430f * frame.localScale.x / Mathf.Max(1f, bounds.width) : 0f;
            diorama.CameraPose(isSkins, view.aspect, scale, shift,
                out var goal, out var rotation, out float fov, out float distance);
            float drift = Mathf.Sin(Time.unscaledTime * .31f);
            rotation *= Quaternion.Euler(drift * .22f, drift * .35f, 0f);
            float dt = Time.unscaledDeltaTime;
            view.fieldOfView = fov;
            view.transform.SetPositionAndRotation(
                snapCamera ? goal : Vector3.SmoothDamp(view.transform.position, goal, ref cameraVelocity, .12f, Mathf.Infinity, dt),
                snapCamera ? rotation : Quaternion.Slerp(view.transform.rotation, rotation, 1f - Mathf.Exp(-16f * dt)));
            snapCamera = false;
            diorama.Tick(distance);
        }

        private void ApplyEquipped(CharacterSkinData skin)
        {
            collection.ApplySkin(skin, playerRenderers);
            collection.ApplySkin(skin, menuRenderers);
            RefreshSkin();
        }

        private void WalletChanged(float _) { RefreshWallet(); RefreshSkin(); }

        private static void Focus(Button button)
        {
            if (button && button.isActiveAndEnabled && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(button.gameObject);
        }
    }
}
