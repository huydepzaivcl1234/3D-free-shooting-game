using System.Collections.Generic;
using GameData.Player.Script;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    internal sealed class SurvivorPlayHud : MonoBehaviour
    {
        private static readonly Vector2 TopLeft = new(0, 1), TopRight = Vector2.one;
        private static readonly Color Dark = new(.08f, .07f, .11f, .86f);
        private static readonly Color HealthRed = new(.92f, .07f, .09f);
        private static readonly Color Silver = new(.61f, .86f, 1);
        private readonly List<Tile> abilities = new();
        private MonsterSpawnZone stage;
        private CharacterHealth health;
        private PlayerStats stats;
        private PlayerWallet wallet;
        private AnimationRunMana stamina;
        private SurvivorDailyLoot loot;
        private SurvivorAbilities shield;
        private Rigidbody playerBody;
        private TMP_FontAsset font;
        private RectTransform hud, footBar, npcQuestRow;
        private TMP_Text npcQuestText;
        private RectTransform xp, hp, energy, shieldFill, footFill;
        private TMP_Text elapsed, gems, gold, kills, level, healthLabel, fps, exitState, lanterns, shrines, chests;
        private UnityEngine.UI.Image exitBackground;
        private SurvivorUpgradeData abilityData;
        private SupportUpgradePillar[] pillars = System.Array.Empty<SupportUpgradePillar>();
        private StagePortalVisual portal;
        private float refreshAt, mapRefreshAt, frameTime;
        private bool exitSeen;
        private Camera view;

        private sealed class Tile
        {
            public RectTransform Root;
            public UnityEngine.UI.Image Icon;
            public TMP_Text Name;
            public TMP_Text Level;
        }

        internal void Initialize(MonsterSpawnZone owner, CharacterHealth player, PlayerWallet ownerWallet, RectTransform parent)
        {
            stage = owner;
            health = player;
            wallet = ownerWallet;
            if (!health || !health.TryGetComponent(out stats)) return;
            playerBody = health.GetComponent<Rigidbody>();
            stamina = health.GetComponent<AnimationRunMana>();
            shield = health.GetComponent<SurvivorAbilities>();
            loot = FindAnyObjectByType<SurvivorDailyLoot>(FindObjectsInactive.Include);
            var cards = FindAnyObjectByType<MiningCardSystem>(FindObjectsInactive.Include);
            abilityData = cards ? cards.RunUpgradeData : null;
            font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");

            hud = Rect(parent, "Play HUD", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            hud.anchorMax = Vector2.one;
            var xpTrack = Plate(Rect(hud, "XP track", TopLeft, TopLeft, Vector2.zero, new Vector2(0, 16)), new Color(.045f,.045f,.06f,.98f));
            xpTrack.rectTransform.anchorMax = Vector2.one;
            xp = Fill(xpTrack.rectTransform, new Color(.03f,.7f,.92f));

            elapsed = Label(hud, "Elapsed time", TopLeft, TopLeft, 18, -31, 143, 32, 25);
            gems = Label(hud, "Gem balance", TopLeft, TopLeft, 168, -31, 156, 32, 25);
            gold = Label(hud, "Gold balance", TopLeft, TopLeft, 328, -31, 155, 32, 25);
            kills = Label(hud, "Kills", TopLeft, TopLeft, 484, -31, 133, 32, 25);
            gems.color = Silver;
            gold.color = new Color(1f,.85f,.38f);
            level = Label(hud, "Level", TopRight, TopRight, -18, -28, 180, 42, 37, TextAlignmentOptions.MidlineRight);

            var healthTrack = Rect(hud, "Player HP", TopLeft, TopLeft, new Vector2(18,-79), new Vector2(252,30));
            Plate(healthTrack, Dark);
            hp = Fill(healthTrack, HealthRed);
            shieldFill = Fill(healthTrack, Silver);
            healthLabel = Label(healthTrack, "HP text", TopLeft, TopLeft, 0, 0, 252, 30, 19, TextAlignmentOptions.Center);

            var staminaTrack = Rect(hud, "Player stamina", TopLeft, TopLeft, new Vector2(18,-116), new Vector2(252,6));
            Plate(staminaTrack, Dark);
            energy = Fill(staminaTrack, new Color(.7f,.54f,.95f));

            for (int i = 0; i < 8; i++)
            {
                var cell = Rect(hud, "Ability slot " + i, TopLeft, TopLeft,
                    new Vector2(18 + (i % 4) * 61, -138 - (i / 4) * 60), new Vector2(55,55));
                Plate(cell, new Color(.12f,.09f,.17f,.57f));
                var icon = Plate(Rect(cell,"Icon", TopLeft, TopLeft, new Vector2(9,-2), new Vector2(37,36)), Color.white);
                icon.preserveAspect = true;
                var name = Label(cell,"Ability",TopLeft,TopLeft,2,0,51,33,15,TextAlignmentOptions.Center);
                name.textWrappingMode = TextWrappingModes.Normal;
                name.enableAutoSizing = true;
                name.fontSizeMin = 9;
                name.fontSizeMax = 15;
                name.overflowMode = TextOverflowModes.Ellipsis;
                abilities.Add(new Tile {Root = cell, Icon = icon,
                    Name = name,
                    Level = Label(cell,"Ability level",TopLeft,TopLeft,0,-34,55,20,14,TextAlignmentOptions.Center)});
            }

            var objective = Rect(hud, "Exit objective", TopLeft, TopLeft, new Vector2(18,-264), new Vector2(300,32));
            exitBackground = Plate(objective, Dark);
            exitState = Label(objective, "Exit status", TopLeft, TopLeft, 8, 0, 286, 32, 20);

            npcQuestRow = Rect(hud, "NPC quest tracker", TopLeft, TopLeft, new Vector2(18, -302), new Vector2(300, 32));
            Plate(npcQuestRow, Dark);
            npcQuestText = Label(npcQuestRow, "Active NPC quests", TopLeft, TopLeft, 8, -4, 284, 24, 18);
            npcQuestText.textWrappingMode = TextWrappingModes.Normal;
            npcQuestRow.gameObject.SetActive(false);

            var progress = Rect(hud, "Map progress", Vector2.right, Vector2.right, new Vector2(-19,115), new Vector2(252,101));
            Plate(progress, Dark);
            lanterns = ProgressRow(progress, -6);
            shrines = ProgressRow(progress, -37);
            chests = ProgressRow(progress, -68);
            fps = Label(hud, "Performance and speed", Vector2.zero, Vector2.zero, 18, 14, 185, 51, 20, TextAlignmentOptions.BottomLeft);
            footBar = Rect(hud,"Player foot HP",Vector2.zero,new Vector2(.5f,.5f),Vector2.zero,new Vector2(48,5));
            Plate(footBar, Dark);
            footFill = Fill(footBar, HealthRed);

            HideOldVitals();
            if (stats.Support) SupportUpgradeHud.MountPlayHud(stats.Support, hud);
            SurvivorMinimapHud.Attach(player.transform, hud);
            ArrangeStatsButton();
            Refresh();
        }

        private void ArrangeStatsButton()
        {
            var canvas = health.HealthBar ? health.HealthBar.GetComponentInParent<Canvas>(true) : null;
            var button = canvas ? canvas.rootCanvas.transform.Find("Player Stats Button") as RectTransform : null;
            if (!button) return;
            button.anchorMin = button.anchorMax = button.pivot = Vector2.zero;
            button.anchoredPosition = new Vector2(18f, 73f);
            button.sizeDelta = new Vector2(94f, 26f);
            if (button.TryGetComponent<UnityEngine.UI.Image>(out var graphic)) graphic.color = Dark;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label)
            {
                label.color = Color.white;
                label.fontSize = 15;
            }
        }

        private void HideOldVitals()
        {
            if (!health.HealthBar) return;
            var current = health.HealthBar;
            while (current.parent && current.name != "Player Vitals Panel" &&
                !current.parent.GetComponent<Canvas>()) current = current.parent;
            var group = current.TryGetComponent<CanvasGroup>(out var existing) ? existing : current.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = false;
        }

        private void LateUpdate()
        {
            if (!hud) return;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0) frameTime = frameTime <= 0 ? dt : Mathf.Lerp(frameTime, dt, .1f);
            if (Time.unscaledTime >= refreshAt)
            {
                refreshAt = Time.unscaledTime + .13f;
                Refresh();
            }
            if (!view) view = Camera.main;
            if (!view) return;
            Vector3 screen = view.WorldToScreenPoint(health.transform.position + Vector3.up * .07f);
            bool visible = health.Health > 0 && screen.z > 0 && RectTransformUtility.RectangleContainsScreenPoint(hud, screen);
            footBar.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(hud, screen, null, out var point))
                footBar.anchoredPosition = point - hud.rect.min + Vector2.down * 10f;
        }

        private void Refresh()
        {
            float max = Mathf.Max(1f, health.MaxHealth);
            float shieldValue = (shield ? shield.Shield : 0) + (stats.Support ? stats.Support.TemporaryShield : 0);
            SetFill(xp, stats.ExperienceProgress);
            SetFill(hp, health.Health / (max + shieldValue));
            SetFill(shieldFill, (health.Health + shieldValue) / (max + shieldValue), health.Health / (max + shieldValue));
            SetFill(footFill, health.Health / max);
            if (stamina) SetFill(energy, stamina.CurrentMana / stamina.MaximumMana);
            healthLabel.SetText("{0:0}/{1:0}", Mathf.Ceil(health.Health), Mathf.Ceil(max));
            int time = Mathf.FloorToInt(stage.SpawnElapsedSeconds);
            elapsed.SetText("{0:00}:{1:00}", time / 60, time % 60);
            gems.SetText("GEM {0:#,##0}", wallet ? wallet.CurrentGems : 0);
            gold.SetText("GOLD {0:#,##0}", wallet ? wallet.CurrentMoney : 0);
            kills.SetText("KILL {0:0}", stage.SpawnKills);
            level.SetText("LVL {0:0}", stats.Level);
            Vector3 velocity = playerBody ? playerBody.linearVelocity : Vector3.zero;
            fps.SetText("FPS  {0:0}\nSPD  {1:0.0}", frameTime > 0 ? 1 / frameTime : 0, new Vector2(velocity.x, velocity.z).magnitude);

            int slot = 0;
            if (abilityData)
                foreach (var entry in abilityData.definitions)
                {
                    if (entry == null || !entry.Ability || slot >= abilities.Count) continue;
                    int count = stats.UpgradeLevel(entry.id);
                    if (count <= 0) continue;
                    var tile = abilities[slot++];
                    tile.Icon.enabled = entry.icon;
                    if (entry.icon) tile.Icon.sprite = entry.icon;
                    tile.Name.text = entry.icon ? "" : entry.title;
                    tile.Level.SetText("LVL {0:0}", count);
                }
            for (; slot < abilities.Count; slot++)
            {
                abilities[slot].Icon.enabled = false;
                abilities[slot].Name.text = "";
                abilities[slot].Level.text = "";
            }
            if (Time.unscaledTime >= mapRefreshAt)
            {
                mapRefreshAt = Time.unscaledTime + 1f;
                pillars = FindObjectsByType<SupportUpgradePillar>(FindObjectsSortMode.None);
                portal = FindAnyObjectByType<StagePortalVisual>();
                if (!loot) loot = FindAnyObjectByType<SurvivorDailyLoot>(FindObjectsInactive.Include);
            }
            if (portal && (portal.transform.position - health.transform.position).sqrMagnitude < 196f) exitSeen = true;
            exitBackground.color = exitSeen ? new Color(.04f,.31f,.13f,.90f) : Dark;
            exitState.text = stage.IsOvertime ? "✓  " + MiningLocalization.Text("Exit open", "Cổng thoát đã mở")
                : (exitSeen ? "✓  " : "–  ") + MiningLocalization.Text("Find the exit", "Tìm cổng thoát");
            RefreshNpcQuest();
            int used = 0;
            foreach (var pillar in pillars) if (pillar && pillar.IsClaimed) used++;
            lanterns.SetText(MiningLocalization.Text("Charge shrines", "Cột đèn") + "   {0:0} / {1:0}", used, pillars.Length);
            shrines.SetText(MiningLocalization.Text("Boss shrines", "Đền boss") + "   {0:0} / {1:0}", stage.UsedShrines, stage.TotalShrines);
            int collected = loot ? loot.CollectedBoxCount : 0;
            int total = loot ? loot.TotalBoxCount : 0;
            chests.SetText(MiningLocalization.Text("Chests", "Rương") + "   {0:0} / {1:0}", collected, total);
        }

        private void RefreshNpcQuest()
        {
            string progress = stage.ActiveNpcQuestProgress;
            bool visible = !string.IsNullOrEmpty(progress);
            if (npcQuestRow.gameObject.activeSelf != visible)
                npcQuestRow.gameObject.SetActive(visible);
            if (!visible || npcQuestText.text == progress) return;

            npcQuestText.text = progress;
            float height = Mathf.Ceil(npcQuestText.GetPreferredValues(progress, npcQuestText.rectTransform.rect.width, 0).y);
            npcQuestText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            npcQuestRow.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height + 8f);
        }

        private TMP_Text ProgressRow(RectTransform progress, float y)
        {
            var body = Rect(progress,"Map counter", TopLeft, TopLeft,new Vector2(6,y),new Vector2(240,28));
            Plate(body,new Color(.23f,.21f,.26f,.7f));
            return Label(body, "Value", TopLeft, TopLeft, 9, 0, 220, 28, 17);
        }

        private TMP_Text Label(RectTransform parent, string name, Vector2 anchor, Vector2 pivot,
            float x, float y, float width, float height, float size, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var label = Rect(parent, name, anchor, pivot, new Vector2(x,y), new Vector2(width,height)).gameObject.AddComponent<TextMeshProUGUI>();
            if (font) label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.outlineColor = new Color32(12,10,16,255);
            label.outlineWidth = .18f;
            return label;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 at, Vector2 size)
        {
            var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = at;
            return rect;
        }

        private static UnityEngine.UI.Image Plate(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Fill(RectTransform parent, Color color) =>
            Plate(Rect(parent,"Fill",Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero),color).rectTransform;

        private static void SetFill(RectTransform rect, float end, float start = 0)
        {
            rect.anchorMin = new Vector2(Mathf.Clamp01(start),0);
            rect.anchorMax = new Vector2(Mathf.Clamp01(end),1);
        }
    }
}

