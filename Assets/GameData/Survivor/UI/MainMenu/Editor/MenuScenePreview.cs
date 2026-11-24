using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MiningSimulator.Ores.Editor
{
    internal sealed class MenuScenePreview : EditorWindow
    {
        private MainMenuSkinUIController menu;
        private MenuDiorama stage;
        private Camera view;
        private Transform player;
        private CharacterSkinCatalog catalog;
        private CharacterSkinData selected;
        private SkinnedMeshRenderer body;
        private MaterialPropertyBlock originalTint, previewTint;
        private List<GameObject> hiddenHud;
        private Vector3 cameraPosition, playerPosition, playerScale;
        private Quaternion cameraRotation, playerRotation;
        private float oldFov, yaw;
        private int oldMask;
        private bool oldPost, active, skins, oldSkins, alignScene;
        private bool oldGizmos, oldSceneEffects, oldSceneFog, oldSceneLighting, oldSceneFx, oldSceneOrtho;
        private float sceneAspect, oldSceneLens, oldSceneSize;
        private Vector3 oldScenePivot;
        private Quaternion oldSceneRotation;
        private int lastSkinState;
        private SceneView sceneView;

        internal static void Open(bool skinPage)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var window = GetWindow<MenuScenePreview>("Menu Preview");
            window.skins = skinPage;
            window.yaw = 0;
            if (!window.active) window.Begin();
            window.alignScene = true;
            window.UpdatePreview();
        }

        private void OnEnable()
        {
            EditorApplication.update += UpdatePreview;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorSceneManager.sceneSaving += SceneSaving;
            EditorSceneManager.sceneClosing += SceneClosing;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        private void OnDisable()
        {
            Stop();
            EditorApplication.update -= UpdatePreview;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorSceneManager.sceneSaving -= SceneSaving;
            EditorSceneManager.sceneClosing -= SceneClosing;
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
        }

        private void Begin()
        {
            if (active || EditorApplication.isPlayingOrWillChangePlaymode) return;
            menu = FindAnyObjectByType<MainMenuSkinUIController>(FindObjectsInactive.Include);
            if (!menu || !menu.HasEditableLayout) return;
            var stageScene = SceneManager.GetSceneByPath(MenuDiorama.ScenePath);
            if (!stageScene.isLoaded)
                stageScene = EditorSceneManager.OpenScene(MenuDiorama.ScenePath, OpenSceneMode.Additive);
            stage = stageScene.GetRootGameObjects().Select(root => root.GetComponent<MenuDiorama>()).FirstOrDefault(item => item);
            if (!stage || !stage.Avatar) return;
            var settings = new SerializedObject(menu);
            view = settings.FindProperty("view").objectReferenceValue as Camera;
            var follow = settings.FindProperty("follow").objectReferenceValue as Component;
            catalog = settings.FindProperty("skinCatalog").objectReferenceValue as CharacterSkinCatalog;
            if (!view || !follow || !catalog || catalog.skins == null || catalog.skins.Length == 0) return;
            player = stage.Avatar;
            var original = settings.FindProperty("mainMaterial").objectReferenceValue as Material;
            body = player.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial == original);
            if (!body) return;
            originalTint ??= new MaterialPropertyBlock();
            previewTint ??= new MaterialPropertyBlock();
            hiddenHud ??= new List<GameObject>();
            selected = catalog.skins.FirstOrDefault(s => s && s.unlockedByDefault) ?? catalog.skins.FirstOrDefault(s => s);
            oldSkins = menu.IsEditableSkinPageOpen;
            sceneView = SceneView.lastActiveSceneView;
            if (sceneView)
            {
                oldGizmos = sceneView.drawGizmos;
                oldSceneEffects = sceneView.sceneViewState.showImageEffects;
                oldSceneFog = sceneView.sceneViewState.showFog;
                oldSceneLighting = sceneView.sceneLighting;
                oldSceneFx = sceneView.sceneViewState.fxEnabled;
                oldSceneOrtho = sceneView.orthographic;
                oldSceneLens = sceneView.cameraSettings.fieldOfView;
                oldScenePivot = sceneView.pivot;
                oldSceneRotation = sceneView.rotation;
                oldSceneSize = sceneView.size;
            }

            cameraPosition = view.transform.position;
            cameraRotation = view.transform.rotation;
            oldFov = view.fieldOfView;
            oldMask = view.cullingMask;
            var cameraData = view.GetUniversalAdditionalCameraData();
            oldPost = cameraData.renderPostProcessing;
            playerPosition = player.position;
            playerRotation = player.rotation;
            playerScale = player.localScale;
            body.GetPropertyBlock(originalTint, 0);

            active = true;
            try
            {
                if (sceneView)
                {
                    sceneView.sceneViewState.fxEnabled = true;
                    sceneView.drawGizmos = false;
                    sceneView.sceneViewState.showImageEffects = true;
                    sceneView.sceneViewState.showFog = true;
                    sceneView.sceneLighting = true;
                }
                cameraData.renderPostProcessing = true;
                view.cullingMask = LayerMask.GetMask(MenuDiorama.LayerName, "UI");
                foreach (Transform sibling in menu.transform.parent)
                {
                    if (sibling == menu.transform || !sibling.gameObject.activeSelf) continue;
                    hiddenHud.Add(sibling.gameObject);
                    sibling.gameObject.SetActive(false);
                }
                stage.Begin(view);
            }
            catch
            {
                Stop();
                throw;
            }
            lastSkinState = int.MinValue;
            alignScene = true;
        }

        private void UpdatePreview()
        {
            if (!active || !menu || !stage || !view || !player || !body) return;
            var turntable = stage.transform.Find("Turntable");
            if (!turntable) return;
            var positionOnStage = turntable.position + Vector3.up * .007f;
            var pose = turntable.rotation * Quaternion.Euler(-1.5f, 13f + yaw, 2.5f);
            bool moved = player.position != positionOnStage || Quaternion.Angle(player.rotation, pose) > .01f;
            if (moved) player.SetPositionAndRotation(positionOnStage, pose);
            if (player.localScale != Vector3.one * .72f) player.localScale = Vector3.one * .72f;
            int skinState = selected ? System.HashCode.Combine(skins, selected, selected.bodyTint,
                selected.displayName, selected.description, selected.unlockCostGold, selected.unlockCostGems, selected.unlockedByDefault)
                : skins.GetHashCode();
            bool skinChanged = skinState != lastSkinState;
            if (skinChanged) menu.ShowEditablePage(skins, selected);
            if (skinChanged && selected)
            {
                body.GetPropertyBlock(previewTint, 0);
                previewTint.SetColor("_BaseColor", selected.bodyTint);
                body.SetPropertyBlock(previewTint, 0);
            }
            lastSkinState = skinState;
            float aspect = view.aspect;
            stage.CameraPose(skins, aspect, Mathf.Max(1f, (16f / 9f) / aspect),
                skins ? 0f : 430f / 1920f * Mathf.Min(1f, (16f / 9f) / aspect),
                out var position, out var rotation, out float fov, out float distance);
            bool changed = (view.transform.position - position).sqrMagnitude > .000001f ||
                Quaternion.Angle(view.transform.rotation, rotation) > .01f || !Mathf.Approximately(fov, view.fieldOfView);
            if (changed)
            {
                view.fieldOfView = fov;
                view.transform.SetPositionAndRotation(position, rotation);
                stage.SetFocus(distance);
            }
            if (sceneView && (changed || alignScene || !Mathf.Approximately(sceneAspect, sceneView.camera.aspect)))
            {
                sceneAspect = sceneView.camera.aspect;
                sceneView.orthographic = false;
                sceneView.cameraSettings.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(fov * .5f * Mathf.Deg2Rad) *
                    Mathf.Min(1f, sceneAspect)) * Mathf.Rad2Deg;
                sceneView.AlignViewToObject(view.transform);
                alignScene = false;
            }
            if (changed || moved || skinChanged)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
        }

        private void Stop()
        {
            if (!active) return;
            active = false;
            if (body && originalTint != null) body.SetPropertyBlock(originalTint, 0);
            if (stage) stage.End();
            if (player)
            {
                player.SetPositionAndRotation(playerPosition, playerRotation);
                player.localScale = playerScale;
            }
            if (view)
            {
                view.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                view.fieldOfView = oldFov;
                view.cullingMask = oldMask;
                view.GetUniversalAdditionalCameraData().renderPostProcessing = oldPost;
            }
            if (menu) menu.ShowEditablePage(oldSkins);
            if (sceneView)
            {
                sceneView.drawGizmos = oldGizmos;
                sceneView.sceneViewState.showImageEffects = oldSceneEffects;
                sceneView.sceneViewState.showFog = oldSceneFog;
                sceneView.sceneLighting = oldSceneLighting;
                sceneView.sceneViewState.fxEnabled = oldSceneFx;
                sceneView.orthographic = oldSceneOrtho;
                sceneView.cameraSettings.fieldOfView = oldSceneLens;
                sceneView.pivot = oldScenePivot;
                sceneView.rotation = oldSceneRotation;
                sceneView.size = oldSceneSize;
            }
            foreach (var item in hiddenHud)
                if (item) item.SetActive(true);
            hiddenHud.Clear();
            Repaint();
        }

        private void OnGUI()
        {
            if (!active)
            {
                if (GUILayout.Button("Start Preview")) Begin();
                return;
            }
            skins = GUILayout.Toolbar(skins ? 1 : 0, new[] { "Main Menu", "Skins" }) == 1;
            if (skins)
            {
                var entries = catalog.skins.Where(s => s).ToArray();
                if (entries.Length > 0)
                {
                    int index = Mathf.Max(0, System.Array.IndexOf(entries, selected));
                    index = EditorGUILayout.Popup("Skin", index, entries.Select(s => s.displayName).ToArray());
                    selected = entries[Mathf.Clamp(index, 0, entries.Length - 1)];
                }
                yaw = EditorGUILayout.Slider("Rotation", yaw, -180, 180);
            }
            if (GUILayout.Button("Frame Scene View"))
            {
                alignScene = true;
            }
            if (GUILayout.Button("Edit Skin Catalog")) Selection.activeObject = catalog;
            if (GUILayout.Button("Stop Preview")) Stop();
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) Stop();
        }
        private void SceneSaving(Scene scene, string path) => Stop();
        private void SceneClosing(Scene scene, bool removingScene) => Stop();
    }
}
