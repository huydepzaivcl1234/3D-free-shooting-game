using System.Collections.Generic;
using GameData.Player.Script;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    [DisallowMultipleComponent]
    public sealed class SurvivorMinimapHud : MonoBehaviour
    {
        private const float Diameter = 212f;
        private const float ViewWidth = 68f;
        private const int GroundResolution = 192;
        private const float TrackingInterval = 1f / 12f;
        private const float DiscoveryInterval = .75f;
        private static readonly Color32 VoidColor = new(12, 15, 22, 255);
        private static readonly Vector2 TopRight = Vector2.one;

        private enum Icon { Player, Lantern, Shrine, Chest, Portal, OpenPortal }

        private static readonly string[][] Glyphs =
        {
            new[] { "....#....", "....#....", "...###...", "...###...", "..#####..", "..#####..", ".#######.", ".###.###.", ".#.....#." },
            new[] { "...###...", "....#....", "..#####..", "..#+++#..", "..#+++#..", "..#+++#..", "..#####..", "...###...", "........." },
            new[] { ".#.....#.", ".#.....#.", "..#####..", ".#######.", ".#+###+#.", ".#######.", "...#.#...", "...###...", "........." },
            new[] { ".........", "..#####..", ".#######.", "#########", "###++####", "#########", ".#######.", ".#######.", "........." },
            new[] { "..#####..", ".##...##.", "##.....##", "##..#..##", "##..#..##", "##.....##", "##.....##", "#########", "........." },
            new[] { "..#####..", ".##+++##.", "##++#++##", "##+#+++##", "##+++#.##", "##+##++##", "##+++++##", "#########", "........." }
        };

        private static readonly Color32[] IconColors =
        {
            new(90, 212, 255, 255), new(255, 189, 72, 255), new(255, 111, 111, 255),
            new(249, 220, 132, 255), new(183, 123, 255, 255), new(220, 145, 255, 255)
        };

        private readonly List<(Component source, Icon icon)> markers = new();
        private readonly List<Image> markerViews = new();
        private readonly List<SupportUpgradePillar> lanterns = new();
        private readonly List<StagePortalVisual> portals = new();

        private Transform player;
        private TerrainGenerator generator;
        private Terrain terrain;
        private MonsterSpawnZone stage;
        private SurvivorDailyLoot loot;
        private RectTransform disk, playerArrow;
        private CanvasGroup visibility;
        private RawImage groundImage;
        private Texture2D groundTexture, iconAtlas, diskTexture;
        private Sprite diskSprite;
        private Sprite[] iconSprites;
        private Vector2 mapOrigin, mapExtent;
        private Vector3 projectedCenter;
        private int cachedSeed, shrineLayer;
        private bool hasProjected, terrainDirty = true;
        private float nextDiscovery, nextTracking, nextBinding;

        public static void Attach(Transform player, RectTransform parent)
        {
            if (!player || !parent) return;

            var hud = parent.GetComponentInChildren<SurvivorMinimapHud>(true);
            if (!hud)
            {
                var root = new GameObject("Survivor Minimap", typeof(RectTransform), typeof(SurvivorMinimapHud));
                root.transform.SetParent(parent, false);
                hud = root.GetComponent<SurvivorMinimapHud>();
                hud.Build();
            }

            if (hud.player == player) return;
            hud.player = player;
            hud.nextBinding = hud.nextDiscovery = hud.nextTracking = 0f;
        }

        private void Build()
        {
            shrineLayer = LayerMask.NameToLayer("MapObstacle");
            var root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = root.pivot = TopRight;
            root.anchoredPosition = new Vector2(-32f, -90f);
            root.sizeDelta = Vector2.one * Diameter;

            const int diskResolution = 128;
            var circle = new Color32[diskResolution * diskResolution];
            for (int y = 0; y < diskResolution; ++y)
            for (int x = 0; x < diskResolution; ++x)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), Vector2.one * (diskResolution * .5f));
                byte alpha = (byte)(255f * Mathf.Clamp01(diskResolution * .5f - distance));
                circle[y * diskResolution + x] = new Color32(255, 255, 255, alpha);
            }

            diskTexture = MakeTexture("Minimap disk", diskResolution, diskResolution, circle, FilterMode.Bilinear);
            diskSprite = Sprite.Create(diskTexture, new Rect(0, 0, diskResolution, diskResolution), Vector2.one * .5f);
            var edge = MakeImage("Rim", root, diskSprite, Diameter, Diameter);
            edge.color = new Color32(12, 13, 20, 255);
            var trim = MakeImage("Inset rim", root, diskSprite, Diameter - 4f, Diameter - 4f);
            trim.color = new Color32(132, 124, 139, 255);
            var face = MakeImage("Map window", root, diskSprite, Diameter - 8f, Diameter - 8f);
            face.color = VoidColor;
            face.gameObject.AddComponent<Mask>();
            disk = face.rectTransform;

            groundImage = new GameObject("Terrain", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            groundImage.rectTransform.SetParent(disk, false);
            groundImage.rectTransform.anchorMin = Vector2.zero;
            groundImage.rectTransform.anchorMax = Vector2.one;
            groundImage.rectTransform.offsetMin = groundImage.rectTransform.offsetMax = Vector2.zero;
            groundImage.raycastTarget = false;
            groundImage.enabled = false;

            BuildIconAtlas();
            playerArrow = MakeImage("Player", disk, iconSprites[(int)Icon.Player], 20f, 20f).rectTransform;
            AddDirection("N", Vector2.up);
            AddDirection("E", Vector2.right);
            AddDirection("S", Vector2.down);
            AddDirection("W", Vector2.left);
            visibility = gameObject.AddComponent<CanvasGroup>();
            visibility.alpha = 0f;
            visibility.blocksRaycasts = false;
        }

        private void AddDirection(string direction, Vector2 side)
        {
            var label = new GameObject(direction, typeof(RectTransform), typeof(TextMeshProUGUI))
                .GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(transform, false);
            label.rectTransform.sizeDelta = new Vector2(22f, 18f);
            label.rectTransform.anchoredPosition = side * (Diameter * .5f + 5f);
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color32(248, 243, 233, 255);
            label.raycastTarget = false;
            label.text = direction;
        }

        private void LateUpdate()
        {
            if (!player || !player.gameObject.activeInHierarchy)
            {
                visibility.alpha = 0f;
                return;
            }

            float now = Time.unscaledTime;
            if (now >= nextBinding)
            {
                nextBinding = now + 2f;
                BindSources();
            }

            if (!generator || !generator.HasLayout)
            {
                visibility.alpha = 0f;
                return;
            }

            visibility.alpha = 1f;
            if (terrainDirty || !groundTexture || cachedSeed != generator.Seed)
                BakeTerrain();

            if (now >= nextDiscovery)
            {
                nextDiscovery = now + DiscoveryInterval;
                DiscoverMarkers();
            }

            if (now < nextTracking) return;
            nextTracking = now + TrackingInterval;
            DrawMarkers();
        }

        private void BindSources()
        {
            Scene scene = player.gameObject.scene;
            if (!generator || !generator.isActiveAndEnabled || generator.gameObject.scene != scene)
            {
                if (generator) generator.OnMapGenerated -= InvalidateTerrain;
                generator = FindInScene<TerrainGenerator>(scene);
                terrain = generator ? generator.GetComponent<Terrain>() : null;
                if (generator) generator.OnMapGenerated += InvalidateTerrain;
                InvalidateTerrain();
            }

            if (!stage || !stage.isActiveAndEnabled || stage.gameObject.scene != scene)
                stage = FindInScene<MonsterSpawnZone>(scene);
            if (!loot || !loot.isActiveAndEnabled || loot.gameObject.scene != scene)
                loot = FindInScene<SurvivorDailyLoot>(scene);
        }

        private static T FindInScene<T>(Scene scene) where T : MonoBehaviour
        {
            foreach (var item in FindObjectsByType<T>())
                if (item.isActiveAndEnabled && item.gameObject.scene == scene) return item;
            return null;
        }

        private void InvalidateTerrain()
        {
            terrainDirty = true;
            nextDiscovery = nextTracking = 0f;
        }

        private void BakeTerrain()
        {
            var data = terrain.terrainData;
            Vector3 origin = generator.transform.position;
            const float padding = ViewWidth * .5f;
            mapOrigin = new Vector2(origin.x - padding, origin.z - padding);
            mapExtent = new Vector2(data.size.x, data.size.z) + Vector2.one * (padding * 2f);
            const int resolution = GroundResolution;
            var heights = new float[resolution * resolution];
            var pixels = new Color32[heights.Length];

            for (int y = 0; y < resolution; ++y)
            for (int x = 0; x < resolution; ++x)
            {
                float localX = (x + .5f) / resolution * mapExtent.x - padding;
                float localZ = (y + .5f) / resolution * mapExtent.y - padding;
                int i = y * resolution + x;
                heights[i] = float.NaN;
                if (localX < 0f || localZ < 0f || localX >= data.size.x || localZ >= data.size.z)
                    continue;

                // The block layout owns its slopes. The native Terrain path can read its heightmap directly.
                if (terrain.enabled)
                    heights[i] = data.GetInterpolatedHeight(localX / data.size.x, localZ / data.size.z);
                else if (generator.TrySample(new Vector3(origin.x + localX, origin.y, origin.z + localZ),
                             out var surface, out _))
                    heights[i] = surface.y - origin.y;
            }

            for (int y = 0; y < resolution; ++y)
            for (int x = 0; x < resolution; ++x)
            {
                int i = y * resolution + x;
                float height = heights[i];
                pixels[i] = VoidColor;
                if (float.IsNaN(height)) continue;

                float level = Mathf.Clamp01(height / data.size.y);
                var color = Color.Lerp(new Color32(34, 42, 47, 255), new Color32(94, 114, 104, 255), level);
                var left = x > 0 ? heights[i - 1] : height;
                var below = y > 0 ? heights[i - resolution] : height;
                if (float.IsNaN(left) || float.IsNaN(below) || height - left > 1.35f || height - below > 1.35f)
                    color = Color.Lerp(color, Color.white, .16f);
                else if (left - height > 1.35f || below - height > 1.35f)
                    color = Color.Lerp(color, Color.black, .28f);
                pixels[i] = color;
            }

            if (groundTexture) Destroy(groundTexture);
            groundTexture = MakeTexture("Minimap terrain", resolution, resolution, pixels, FilterMode.Point);
            groundImage.texture = groundTexture;
            groundImage.enabled = true;
            cachedSeed = generator.Seed;
            terrainDirty = false;
            hasProjected = false;

        }

        private void DiscoverMarkers()
        {
            markers.Clear();
            generator.GetComponentsInChildren(false, lanterns);
            foreach (var lantern in lanterns) markers.Add((lantern, Icon.Lantern));
            generator.GetComponentsInChildren(false, portals);
            foreach (var portal in portals) markers.Add((portal, Icon.Portal));

            if (stage)
            {
                var root = stage.transform;
                for (int i = 0; i < root.childCount; ++i)
                {
                    var child = root.GetChild(i);
                    // The spawning code assigns this name and uses a visual-only shrine prefab.
                    if (child.gameObject.layer == shrineLayer && child.gameObject.activeInHierarchy &&
                        child.name == "God Boss Shrine")
                        markers.Add((child, Icon.Shrine));
                }
            }

            if (loot)
                foreach (var box in loot.CurrentBoxes)
                    if (box && box.isActiveAndEnabled) markers.Add((box, Icon.Chest));
        }

        private void DrawMarkers()
        {
            Vector3 center = player.position;
            if (!hasProjected || center.x != projectedCenter.x || center.z != projectedCenter.z)
            {
                float halfView = ViewWidth * .5f;
                groundImage.uvRect = new Rect((center.x - halfView - mapOrigin.x) / mapExtent.x,
                    (center.z - halfView - mapOrigin.y) / mapExtent.y,
                    ViewWidth / mapExtent.x, ViewWidth / mapExtent.y);
                projectedCenter = center;
                hasProjected = true;
            }

            Vector3 facing = player.forward;
            if (facing.x * facing.x + facing.z * facing.z > .0001f)
                playerArrow.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);

            int used = 0;
            float pixelsPerMeter = disk.rect.width / ViewWidth;
            float radius = disk.rect.width * .5f - 9f;
            foreach (var (source, icon) in markers)
            {
                if (!source || !source.gameObject.activeInHierarchy) continue;
                Vector3 delta = source.transform.position - center;
                var offset = new Vector2(delta.x, delta.z) * pixelsPerMeter;
                if (offset.sqrMagnitude > radius * radius) continue;
                Icon display = source is StagePortalVisual portal && portal.IsOpeningOrOpen ? Icon.OpenPortal : icon;
                if (used == markerViews.Count)
                {
                    var marker = MakeImage("Map marker", disk, iconSprites[(int)display], 17f, 17f);
                    marker.rectTransform.SetSiblingIndex(playerArrow.GetSiblingIndex());
                    markerViews.Add(marker);
                }

                var view = markerViews[used++];
                view.sprite = iconSprites[(int)display];
                view.rectTransform.anchoredPosition = offset;
                view.enabled = true;
            }
            for (int i = used; i < markerViews.Count; ++i) markerViews[i].enabled = false;

        }

        private void BuildIconAtlas()
        {
            const int tile = 11;
            int width = Glyphs.Length * tile;
            var pixels = new Color32[width * tile];
            iconSprites = new Sprite[Glyphs.Length];

            for (int icon = 0; icon < Glyphs.Length; ++icon)
            {
                var rows = Glyphs[icon];
                for (int y = 0; y < rows.Length; ++y)
                for (int x = 0; x < rows[y].Length; ++x)
                {
                    char pixel = rows[y][x];
                    if (pixel == '.') continue;
                    int index = (tile - 2 - y) * width + icon * tile + x + 1;
                    for (int dy = -1; dy <= 1; ++dy)
                    for (int dx = -1; dx <= 1; ++dx)
                    {
                        int neighbor = index + dy * width + dx;
                        if (pixels[neighbor].a == 0) pixels[neighbor] = new Color32(10, 10, 17, 255);
                    }
                    pixels[index] = pixel == '+' ? new Color32(255, 246, 210, 255) : IconColors[icon];
                }
            }

            iconAtlas = MakeTexture("Minimap marker atlas", width, tile, pixels, FilterMode.Point);
            for (int icon = 0; icon < iconSprites.Length; ++icon)
                iconSprites[icon] = Sprite.Create(iconAtlas, new Rect(icon * tile, 0, tile, tile), Vector2.one * .5f,
                    100f, 0, SpriteMeshType.FullRect);
        }

        private static Texture2D MakeTexture(string label, int width, int height, Color32[] colors, FilterMode filter)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = label,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            return texture;
        }

        private static Image MakeImage(string label, RectTransform parent, Sprite sprite, float width, float height)
        {
            var image = new GameObject(label, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.rectTransform.sizeDelta = new Vector2(width, height);
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private void OnDestroy()
        {
            if (generator) generator.OnMapGenerated -= InvalidateTerrain;
            if (groundTexture) Destroy(groundTexture);
            if (diskSprite) Destroy(diskSprite);
            if (diskTexture) Destroy(diskTexture);
            if (iconSprites != null)
                foreach (var sprite in iconSprites)
                    if (sprite) Destroy(sprite);
            if (iconAtlas) Destroy(iconAtlas);
        }
    }
}
