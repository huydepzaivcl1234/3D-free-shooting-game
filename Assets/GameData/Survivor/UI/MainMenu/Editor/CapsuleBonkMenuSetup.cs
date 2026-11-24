using System.IO;
using System.Linq;
using GameData.Player.Script;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiningSimulator.Ores.Editor
{
    public static class CapsuleBonkMenuSetup
    {
        private const string Root = "Assets/GameData/Survivor/UI/MainMenu";
        private const string Model = "Assets/GameData/Player/Models/CapsuleBoy/";
        private const string CatalogPath = "Assets/GameData/Player/CapsuleBoy Skin Catalog.asset";

        [MenuItem("Survivor/Menu/Preview Main Menu in Scene")]
        private static void PreviewMain() => MenuScenePreview.Open(false);

        [MenuItem("Survivor/Menu/Preview Skins in Scene")]
        private static void PreviewSkins() => MenuScenePreview.Open(true);

        [MenuItem("Survivor/Menu/Create Default Skins")]
        public static void CreateDefaultSkins()
        {
            Directory.CreateDirectory(Root + "/Skins");
            Directory.CreateDirectory(Root + "/Resources");
            AssetDatabase.Refresh();
            var style = AssetDatabase.LoadAssetAtPath<UIStyle>(Root + "/Resources/Capsule Bonk UI Style.asset");
            if (!style)
            {
                style = ScriptableObject.CreateInstance<UIStyle>();
                style.pixelFont = Resources.Load<Font>("SurvivorPixelify");
                style.bodyFont = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
                AssetDatabase.CreateAsset(style, Root + "/Resources/Capsule Bonk UI Style.asset");
            }
            Skin("classic", "Classic White", "The original CapsuleBoy finish.", new Color32(232, 226, 212, 255), 0, 0, 1);
            Skin("obsidian", "Obsidian Black", "A graphite shell with a matte finish.", new Color32(96, 103, 112, 255), 125, 0, 2);
            Skin("crimson", "Crimson Red", "A deep red coat with a hint of iron oxide.", new Color32(201, 70, 66, 255), 175, 0, 2);
            Skin("hazard", "Hazard Yellow", "The safety paint used on the old mine equipment.", new Color32(237, 185, 56, 255), 220, 0, 3);
            Skin("toxic", "Toxic Green", "A sharp green finish inspired by the glowing caves.", new Color32(114, 194, 89, 255), 0, 4, 4);
            Skin("royal", "Royal Blue", "A cool cobalt shell with a deep blue finish.", new Color32(76, 131, 221, 255), 0, 6, 5);
            if (!AssetDatabase.LoadAssetAtPath<CharacterSkinCatalog>(CatalogPath))
            {
                var catalog = ScriptableObject.CreateInstance<CharacterSkinCatalog>();
                catalog.skins = new[] { "classic", "obsidian", "crimson", "hazard", "toxic", "royal" }
                    .Select(id => AssetDatabase.LoadAssetAtPath<CharacterSkinData>(Root + "/Skins/" + id + ".asset")).ToArray();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            AssetDatabase.SaveAssets();
        }

        private static void Skin(string id, string title, string description, Color tint, int gold, int gems, int stars)
        {
            string path = Root + "/Skins/" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<CharacterSkinData>(path)) return;
            var skin = ScriptableObject.CreateInstance<CharacterSkinData>();
            skin.skinId = id;
            skin.displayName = title;
            skin.description = description;
            skin.bodyTint = tint;
            skin.unlockCostGold = gold;
            skin.unlockCostGems = gems;
            skin.rarityStars = stars;
            skin.unlockedByDefault = id == "classic";
            AssetDatabase.CreateAsset(skin, path);
        }

        [MenuItem("Survivor/Menu/Auto-Bind Scene References")]
        public static void Bind()
        {
            var menu = Object.FindAnyObjectByType<GameMainMenu>(FindObjectsInactive.Include);
            var follow = Object.FindAnyObjectByType<TopDownSurvivor>(FindObjectsInactive.Include);
            var terrain = Object.FindAnyObjectByType<TerrainGenerator>(FindObjectsInactive.Include);
            var camera = follow && follow.viewCamera ? follow.viewCamera : Camera.main;
            var wallet = menu ? new SerializedObject(menu).FindProperty("wallet").objectReferenceValue as PlayerWallet : null;
            if (!wallet) wallet = Object.FindAnyObjectByType<PlayerWallet>(FindObjectsInactive.Include);
            var style = AssetDatabase.LoadAssetAtPath<UIStyle>(Root + "/Diorama/Approved/Just Survival Style.asset");
            if (!style) style = AssetDatabase.LoadAssetAtPath<UIStyle>(Root + "/Resources/Capsule Bonk UI Style.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>(Model + "Main.mat");
            var idle = AssetDatabase.LoadAllAssetsAtPath(Model + "CapsuleBoy.fbx").OfType<AnimationClip>()
                .FirstOrDefault(clip => clip.name == "Idle");
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterSkinCatalog>(CatalogPath);
            var required = new (string name, Object asset)[]
            {
                ("GameMainMenu", menu), ("TopDownSurvivor", follow), ("TerrainGenerator", terrain),
                ("Main Camera", camera), ("PlayerWallet", wallet), ("UIStyle", style),
                ("Main material", material), ("CapsuleBoy Idle", idle)
            };
            var missing = required.Where(entry => !entry.asset).Select(entry => entry.name).ToList();
            if (!catalog || catalog.skins == null || catalog.skins.Length == 0) missing.Add("skin catalog");
            if (missing.Count > 0)
            {
                Debug.LogWarning("Menu setup is missing " + string.Join(", ", missing));
                return;
            }

            var presentation = menu.GetComponent<MainMenuSkinUIController>() ??
                Undo.AddComponent<MainMenuSkinUIController>(menu.gameObject);
            var settings = new SerializedObject(presentation);
            Set("style", style);
            Set("mainMaterial", material);
            Set("view", camera);
            Set("wallet", wallet);
            Set("follow", follow);
            Set("terrain", terrain);
            var stats = Object.FindAnyObjectByType<PlayerStatsPanel>(FindObjectsInactive.Include);
            if (stats) Set("statsShortcut", stats.OpenButtonRect);
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuDiorama.ScenePath))
                Debug.LogWarning("MainMenu scene is missing.", presentation);
            foreach (var marker in new[] { "CamPos_MainMenu", "CamPos_SkinSelect" })
                if (menu.transform.Find(marker) is { } old && old) Undo.DestroyObjectImmediate(old.gameObject);
            Set("skinCatalog", catalog);
            settings.ApplyModifiedProperties();
            MenuDioramaSetup.SaveLayout(menu, presentation, style);
            EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
            Debug.Log("Capsule Bonk menu bound to the GameData skin catalog.", presentation);

            void Set(string property, Object value) => settings.FindProperty(property).objectReferenceValue = value;
        }

    }
}
