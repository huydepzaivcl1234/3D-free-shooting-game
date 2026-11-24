using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MiningSimulator.Ores.Editor
{
    internal static class MenuDioramaSetup
    {
        private const string Root = "Assets/GameData/Survivor/UI/MainMenu";
        private const string Art = Root + "/Diorama";

        internal static MenuDiorama Create(TerrainGenerator terrain, AnimationClip idle)
        {
            foreach (var existing in Object.FindObjectsByType<MenuDiorama>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (existing.gameObject.scene == terrain.gameObject.scene) return existing;
            if (!AssetDatabase.IsValidFolder(Art)) AssetDatabase.CreateFolder(Root, "Diorama");
            var materials = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Menu Props.mat");
            if (!materials) materials = StoreMaterial("Menu Props", new Material(Shader.Find("Universal Render Pipeline/Lit")));
            var root = new GameObject("MenuDiorama");
            Undo.RegisterCreatedObjectUndo(root, "Create Menu Diorama");
            var diorama = root.AddComponent<MenuDiorama>();
            var turntable = Child(root.transform, "Turntable");
            Material palette(string name, Color32 color, bool glow = false)
            {
                var result = StoreMaterial(name, new Material(materials));
                result.SetColor("_BaseColor", color);
                result.SetFloat("_Smoothness", 0f);
                if (glow)
                {
                    result.EnableKeyword("_EMISSION");
                    result.SetColor("_EmissionColor", (Color)color * .8f);
                }
                return result;
            }
            var glove = palette("Canvas Mittens", new Color32(203, 196, 172, 255));
            var brass = palette("Old Brass", new Color32(168, 116, 47, 255));
            var rubber = palette("Carbon Rubber", new Color32(47, 52, 47, 255));
            var wood = palette("Mine Wood", new Color32(121, 73, 45, 255));
            var rock = palette("Mine Stone", new Color32(104, 99, 86, 255));
            var ore = palette("Copper Ore", new Color32(218, 136, 76, 255));
            var glass = palette("Lantern Glass", new Color32(255, 191, 89, 255), true);
            var coral = palette("Ochre Mushroom", new Color32(161, 84, 51, 255));
            var cloudMat = palette("Menu Cloud", new Color32(213, 218, 210, 255));
            var props = Child(root.transform, "Props");
            var crate = Child(props, "Foreground crate");
            crate.localPosition = new Vector3(2.15f, 0, 2.75f);
            crate.localRotation = Quaternion.Euler(0, 13, 0);
            Cube(crate, "Crate", new Vector3(0, .34f, 0), new Vector3(.70f, .68f, .66f), wood);
            foreach (int sign in new[] { -1, 1 })
                Cube(crate, "Frame", new Vector3(sign * .26f, .36f, .343f), new Vector3(.092f, .71f, .04f), brass);
            Cube(props, "Foreground rock", new Vector3(1.69f, .12f, 2.62f), new Vector3(.62f, .26f, .47f), rock).localRotation = Quaternion.Euler(2, 31, 5);
            var lamp = Child(props, "Mine lantern");
            lamp.localPosition = new Vector3(1.55f, 0, -.17f);
            Cube(lamp, "Foot", new Vector3(0, .055f, 0), new Vector3(.36f, .11f, .36f), rubber);
            Cube(lamp, "Glass", new Vector3(0, .28f, 0), new Vector3(.23f, .42f, .23f), glass);
            Cube(lamp, "Cap", new Vector3(0, .51f, 0), new Vector3(.33f, .11f, .33f), rubber);
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                Cube(lamp, "Lantern rail", new Vector3(x * .137f, .29f, z * .137f), new Vector3(.035f, .46f, .035f), brass);
            var flame = Child(lamp, "Flicker").gameObject.AddComponent<Light>();
            flame.transform.localPosition = new Vector3(0, .57f, .20f);
            flame.type = LightType.Point;
            flame.color = new Color32(255, 167, 79, 255);
            flame.range = 3.5f;
            flame.intensity = 1.1f;
            var portalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/GameData/Maps/World1/LowPolyNature/Prefabs/Stage Portal.prefab");
            var arch = portalPrefab ? portalPrefab.transform.Find("Stone arch") : null;
            if (arch)
            {
                var portal = Child(props, "Stone Portal");
                portal.localPosition = new Vector3(1.26f, 0, -3.1f);
                portal.localRotation = Quaternion.Euler(0, 5, 0);
                portal.localScale = Vector3.one * .35f;
                portal.gameObject.AddComponent<MeshFilter>().sharedMesh = arch.GetComponent<MeshFilter>().sharedMesh;
                portal.gameObject.AddComponent<MeshRenderer>().sharedMaterials = arch.GetComponent<MeshRenderer>().sharedMaterials;
            }
            Cube(props, "Copper rock", new Vector3(-1.56f, .11f, -.76f), new Vector3(.45f, .22f, .32f), rock);
            Cube(props, "Copper vein", new Vector3(-1.61f, .18f, -.59f), new Vector3(.155f, .069f, .033f), ore);
            for (int i = 0; i < 2; i++)
            {
                var cap = Child(props, "Voxel mushroom");
                cap.localPosition = new Vector3(.88f + .42f * i, 0, -1.28f - .20f * i);
                cap.localScale = Vector3.one * (1 + i * .44f);
                Cube(cap, "Stem", new Vector3(0, .069f, 0), new Vector3(.06f, .14f, .06f), glove);
                Cube(cap, "Cap", new Vector3(0, .14f, 0), new Vector3(.19f, .07f, .16f), coral);
            }
            var clouds = Child(root.transform, "Voxel clouds");
            clouds.localPosition = new Vector3(-10, 10, -32);
            Cube(clouds, "Cloud", Vector3.zero, new Vector3(5, .75f, 1.5f), cloudMat);
            Cube(clouds, "Cloud step", new Vector3(.5f, .6f, 0), new Vector3(2.8f, .5f, 1.35f), cloudMat);
            Cube(clouds, "Far cloud", new Vector3(18, 1.4f, -11), new Vector3(6, .65f, 1.7f), cloudMat);
            AddDistantTree(props);
            AddDust(root.transform, glove);

            var sun = Child(root.transform, "Sunset key").gameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.localRotation = Quaternion.Euler(35, -145, 0);
            sun.color = new Color32(255, 201, 138, 255);
            sun.intensity = 1.65f;
            sun.shadows = LightShadows.Soft;
            sun.GetUniversalAdditionalLightData().softShadowQuality = SoftShadowQuality.High;
            var rim = Child(root.transform, "Cold rim").gameObject.AddComponent<Light>();
            rim.transform.localPosition = new Vector3(2, 2.3f, -1.5f);
            rim.type = LightType.Point;
            rim.color = new Color32(148, 188, 255, 255);
            rim.range = 5f;
            rim.intensity = .85f;

            var main = Shot(root.transform, "CamPos_MainMenu", new Vector3(.15f, .55f, 5.8f));
            var skin = Shot(root.transform, "CamPos_SkinSelect", new Vector3(1.12f, .5f, 5.30f));
            var volume = Child(root.transform, "Menu Volume").gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100;
            volume.sharedProfile = VolumeAsset();
            var data = new SerializedObject(diorama);
            Set("turntable", turntable); Set("idle", idle); Set("clouds", clouds);
            Set("sunset", sun); Set("lantern", flame); Set("mainShot", main); Set("skinShot", skin); Set("atmosphere", volume);
            data.ApplyModifiedPropertiesWithoutUndo();
            Physics.SyncTransforms();
            diorama.FrameTerrain(terrain);
            AssetDatabase.SaveAssets();
            return diorama;

            void Set(string property, Object value) => data.FindProperty(property).objectReferenceValue = value;
        }

        public static void AddDistantTree(Transform props)
        {
            if (props.Find("Tree at the wall")) return;
            const string treePath = "Assets/GameData/Maps/World1/LowPolyNature/Prefabs/Meadow Tree A.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(treePath);
            if (!prefab) return;
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, props);
            tree.name = "Tree at the wall";
            tree.transform.localPosition = new Vector3(-4.5f, 0, -7.2f);
            tree.transform.localRotation = Quaternion.Euler(0, 33f, 0);
            tree.transform.localScale *= 1.1f;
            foreach (var collider in tree.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        }

        private static void AddDust(Transform parent, Material material)
        {
            var node = Child(parent, "Pixel dust");
            node.localPosition = new Vector3(0, 1.2f, -2f);
            var dust = node.gameObject.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.useUnscaledTime = true;
            main.startLifetime = 7f;
            main.startSize = new ParticleSystem.MinMaxCurve(.018f, .031f);
            main.startSpeed = .19f;
            main.maxParticles = 32;
            var emission = dust.emission;
            emission.rateOverTime = 4f;
            var shape = dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(8, 2, 6);
            var renderer = node.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            renderer.mesh = cube.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(cube);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        internal static void SaveLayout(GameMainMenu menu, MainMenuSkinUIController presentation, UIStyle style)
        {
            var styleData = new SerializedObject(style);
            var font = styleData.FindProperty("pixelAsset");
            if (!font.objectReferenceValue)
            {
                var pixel = TMP_FontAsset.CreateFontAsset(style.pixelFont);
                pixel.name = "Survivor Pixelify Menu";
                AssetDatabase.CreateAsset(pixel, Root + "/Resources/Survivor Pixelify Menu.asset");
                AssetDatabase.AddObjectToAsset(pixel.material, pixel);
                foreach (var atlas in pixel.atlasTextures)
                    if (atlas) AssetDatabase.AddObjectToAsset(atlas, pixel);
                font.objectReferenceValue = pixel;
                styleData.ApplyModifiedPropertiesWithoutUndo();
            }
            var art = presentation.BuildEditableLayout(menu);
            if (art.Count > 0 && !AssetDatabase.Contains(art[0]))
            {
                AssetDatabase.CreateAsset(art[0], AssetDatabase.GenerateUniqueAssetPath(Art + "/Menu Sprites.asset"));
                foreach (var asset in art.Skip(1)) AssetDatabase.AddObjectToAsset(asset, art[0]);
            }
            EditorUtility.SetDirty(presentation);
            AssetDatabase.SaveAssets();
        }

        private static VolumeProfile VolumeAsset()
        {
            string path = Art + "/Menu Afternoon.asset";
            var asset = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (asset) return asset;
            asset = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(asset, path);
            var depth = asset.Add<DepthOfField>();
            depth.mode.Override(DepthOfFieldMode.Gaussian);
            depth.gaussianStart.Override(7.2f);
            depth.gaussianEnd.Override(18f);
            depth.gaussianMaxRadius.Override(.67f);
            var colors = asset.Add<ColorAdjustments>();
            colors.saturation.Override(-13f);
            colors.colorFilter.Override(new Color(1f, .965f, .90f));
            var vignette = asset.Add<Vignette>();
            vignette.intensity.Override(.25f);
            vignette.smoothness.Override(.44f);
            var bloom = asset.Add<Bloom>();
            bloom.intensity.Override(0f);
            foreach (var component in asset.components) AssetDatabase.AddObjectToAsset(component, asset);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static Material StoreMaterial(string name, Material material)
        {
            string path = Art + "/" + name + ".mat";
            var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (saved)
            {
                Object.DestroyImmediate(material);
                return saved;
            }
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Camera Shot(Transform parent, string name, Vector3 offset)
        {
            var shot = Child(parent, name).gameObject.AddComponent<Camera>();
            shot.transform.localPosition = offset;
            shot.transform.localRotation = Quaternion.LookRotation(-offset);
            shot.fieldOfView = 28;
            shot.enabled = false;
            return shot;
        }
        private static Transform Child(Transform parent, string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }
        private static Transform Cube(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube.transform;
        }
    }
}
