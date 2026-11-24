using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiningSimulator.Ores
{
    public sealed partial class MainMenuSkinUIController
    {
        private CanvasGroup questPage;
        private RectTransform questRows;
        private ScrollRect questScroll;
        private MiningQuestSystem quests;
        private MonsterSpawnZone questEncounter;
        private MiningAudioSettingsPanel menuSettings;
        private MiningQuestPeriod questPeriod;
        private MiningQuestDefinition selectedQuest;
        private TMP_Text dailyClock, weeklyClock, questTitle, questProgress, questReward, questClaimLabel, questNotice, npcProgress;
        private UnityEngine.UI.Image questFill;
        private Button questBack, claimQuest, dailyTab, weeklyTab;
        private readonly List<(MiningQuestDefinition definition, TMP_Text title, TMP_Text progress, UnityEngine.UI.Image edge)> questViews = new();
        private float nextQuestRefresh;
        private float? claimedQuestGold;

        private void BindMenuPages(MiningAudioSettingsPanel settings)
        {
            menuSettings = settings;
            bevel = ((UnityEngine.UI.Image)playButton.targetGraphic).sprite;
            focusOutline = playButton.targetGraphic.transform.Find("Teal outline")?.GetComponent<UnityEngine.UI.Image>()?.sprite;
        }

        internal void PrepareSettingsPage()
        {
            if (page != Page.MainMenu || !menuSettings) return;
            menuSettings.ApplySurvivorTheme(style, bevel);
            menuSettings.PanelClosed += ReturnFromSettings;
            page = Page.Settings;
            home.gameObject.SetActive(false);
        }

        private void ReturnFromSettings()
        {
            menuSettings.PanelClosed -= ReturnFromSettings;
            if (page == Page.Settings && isActiveAndEnabled) ExitMenuPage(settingsButton);
        }

        private void CloseMenuPages()
        {
            if (menuSettings) menuSettings.PanelClosed -= ReturnFromSettings;
            StopQuestListening();
            if (questPage) questPage.gameObject.SetActive(false);
        }

        private void ExitMenuPage(Button focus)
        {
            if (page == Page.Quests)
            {
                StopQuestListening();
                questPage.gameObject.SetActive(false);
            }
            page = Page.MainMenu;
            ShowPage(false);
            Focus(focus);
        }

        internal bool OpenQuestPage()
        {
            if (page != Page.MainMenu) return false;
            quests = FindAnyObjectByType<MiningQuestSystem>();
            if (!quests || !quests.Data) return false;
            questEncounter = FindAnyObjectByType<MonsterSpawnZone>();
            if (!questPage) BuildQuestPage();
            page = Page.Quests;
            home.gameObject.SetActive(false);
            questPage.gameObject.SetActive(true);
            questPage.alpha = 0f;
            quests.QuestsChanged += RefreshQuestPage;
            MiningLocalization.LanguageChanged += RefreshQuestPage;
            SelectQuestPeriod(questPeriod);
            nextQuestRefresh = 0f;
            TickQuestPage();
            Focus(questBack);
            return true;
        }

        private void StopQuestListening()
        {
            if (quests) quests.QuestsChanged -= RefreshQuestPage;
            MiningLocalization.LanguageChanged -= RefreshQuestPage;
        }

        private void BuildQuestPage()
        {
            questPage = Group(frame, "Quest selection");
            questBack = Action(questPage.transform, "Back", 89, 49, 251, 77, style.card, null, 31);
            questBack.onClick.AddListener(() => ExitMenuPage(questButton));
            var backText = Localize(questBack.GetComponentInChildren<TMP_Text>(), "MENU_BACK", "BACK");
            var icon = backButton.targetGraphic.transform.Find("IconBack")?.GetComponent<UnityEngine.UI.Image>()?.sprite;
            if (icon) Flat(questBack.targetGraphic.transform, "Back icon", 27, 20, 33, 33, Color.white).sprite = icon;
            backText.rectTransform.anchoredPosition = new Vector2(77, 0);
            backText.rectTransform.sizeDelta = new Vector2(160, 77);

            var list = Surface(questPage.transform, "Mission list", 89, 184, 532, 730, style.panel);
            Localize(Label(list, "", 26, 12, 480, 66, 47, style.ink, true), "MENU_QUEST", "QUEST");
            Flat(list, "Divider", 26, 82, 480, 2, style.edge);
            dailyTab = Action(list, "Daily", 26, 106, 229, 57, style.card, null, 29);
            weeklyTab = Action(list, "Weekly", 277, 106, 229, 57, style.card, null, 29);
            Localize(dailyTab.GetComponentInChildren<TMP_Text>(), "DAILY", "DAILY");
            Localize(weeklyTab.GetComponentInChildren<TMP_Text>(), "WEEKLY", "WEEKLY");
            dailyTab.onClick.AddListener(() => SelectQuestPeriod(MiningQuestPeriod.Daily));
            weeklyTab.onClick.AddListener(() => SelectQuestPeriod(MiningQuestPeriod.Weekly));
            dailyClock = Label(list, "", 26, 170, 229, 32, 23, style.muted, true);
            weeklyClock = Label(list, "", 277, 170, 229, 32, 23, style.muted, true);
            dailyClock.alignment = weeklyClock.alignment = TextAlignmentOptions.Center;

            var viewport = Flat(list, "Quest list viewport", 26, 224, 480, 477, Color.clear);
            viewport.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            questRows = Box(viewport.transform, "Quest rows", 0, 0, 480, 477);
            questScroll = viewport.gameObject.AddComponent<ScrollRect>();
            questScroll.viewport = viewport.rectTransform;
            questScroll.content = questRows;
            questScroll.horizontal = false;
            questScroll.movementType = ScrollRect.MovementType.Clamped;

            var details = Surface(questPage.transform, "Quest details", 1303, 264, 527, 482, style.panel);
            questTitle = Label(details, "", 31, 25, 465, 98, 47, style.ink, true);
            questTitle.font = style.headingFont;
            Flat(details, "Divider", 31, 126, 465, 2, style.edge);
            questProgress = Label(details, "", 31, 151, 465, 45, 30, style.ink, true);
            var track = Flat(details, "Progress track", 31, 201, 465, 14, style.slot);
            questFill = Flat(track.transform, "Progress fill", 0, 0, 465, 14, style.accent);
            questFill.rectTransform.anchorMax = Vector2.up;
            questReward = Label(details, "", 78, 235, 418, 48, 28, style.ink, true);
            Flat(details, "Gold icon", 31, 243, 34, 34, Color.white).sprite = goldIcon;
            claimQuest = Action(details, "Claim", 31, 313, 465, 78, PlayFill, null, 31);
            questClaimLabel = claimQuest.GetComponentInChildren<TMP_Text>();
            claimQuest.onClick.AddListener(ClaimSelectedQuest);
            questNotice = Label(details, "", 31, 403, 465, 48, 22, style.muted, false);
            npcProgress = Label(questPage.transform, "", 1315, 766, 506, 122, 22, style.ink, false);
            npcProgress.color = style.ink;
        }

        private void SelectQuestPeriod(MiningQuestPeriod period)
        {
            questPeriod = period;
            foreach (Transform row in questRows) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
            questViews.Clear();
            selectedQuest = null;
            claimedQuestGold = null;
            foreach (var definition in quests.Data.Quests)
            {
                if (definition == null || !definition.IsValid || definition.Period != period) continue;
                var row = Action(questRows, definition.QuestId, 0, questViews.Count * 98, 480, 88, style.card, null);
                var hit = row.targetGraphic.rectTransform;
                var edge = Outline(hit, 480, 88, style.accent);
                var title = row.GetComponentInChildren<TMP_Text>();
                title.rectTransform.anchoredPosition = new Vector2(19, -7);
                title.rectTransform.sizeDelta = new Vector2(442, 44);
                title.alignment = TextAlignmentOptions.MidlineLeft;
                title.fontSizeMin = 19;
                title.fontSizeMax = 29;
                var progress = Label(hit, "", 19, 50, 442, 26, 22, style.muted, true);
                var targetQuest = definition;
                row.onClick.AddListener(() => { selectedQuest = targetQuest; claimedQuestGold = null; RefreshQuestPage(); });
                questViews.Add((definition, title, progress, edge));
                selectedQuest ??= definition;
            }
            questRows.sizeDelta = new Vector2(480, Mathf.Max(477, questViews.Count * 98));
            questScroll.StopMovement();
            questScroll.verticalNormalizedPosition = 1;
            dailyTab.interactable = period != MiningQuestPeriod.Daily;
            weeklyTab.interactable = period != MiningQuestPeriod.Weekly;
            RefreshQuestPage();
        }

        private void RefreshQuestPage()
        {
            if (page != Page.Quests) return;
            foreach (var item in questViews)
            {
                var definition = item.definition;
                item.title.text = definition.GetLocalizedName();
                item.progress.text = QuestState(definition, out _);
                item.edge.enabled = definition == selectedQuest;
            }
            questNotice.text = claimedQuestGold.HasValue
                ? Format("MENU_QUEST_REWARD_RECEIVED", "+{0} GOLD", MiningMoneyFormatter.Format(claimedQuestGold.Value))
                : "";
            claimQuest.gameObject.SetActive(selectedQuest != null);
            if (selectedQuest == null)
            {
                questTitle.text = MiningLocalization.TextKey("MENU_QUEST_EMPTY", "No quests this period");
                questProgress.text = questReward.text = "";
                questFill.rectTransform.sizeDelta = new Vector2(0, 14);
                return;
            }

            questTitle.text = selectedQuest.GetLocalizedName();
            questProgress.text = QuestState(selectedQuest, out int progress);
            questReward.text = selectedQuest.GetRewardPreview();
            questFill.rectTransform.sizeDelta = new Vector2(465f * progress / selectedQuest.TargetAmount, 14);
            bool claimed = quests.IsClaimed(selectedQuest.QuestId);
            claimQuest.interactable = !claimed && progress >= selectedQuest.TargetAmount;
            questClaimLabel.text = claimed ? MiningLocalization.TextKey("CLAIMED", "CLAIMED") :
                claimQuest.interactable ? MiningLocalization.TextKey("CLAIM", "CLAIM") :
                MiningLocalization.TextKey("IN PROGRESS", "IN PROGRESS");
            questClaimLabel.color = claimQuest.interactable ? PlayInk : style.ink;
        }

        private string QuestState(MiningQuestDefinition definition, out int progress)
        {
            progress = Mathf.Min(quests.GetProgress(definition.QuestId), definition.TargetAmount);
            return Format("MENU_QUEST_PROGRESS", "{0:N0} / {1:N0}", progress, definition.TargetAmount);
        }

        private void ClaimSelectedQuest()
        {
            if (selectedQuest == null || !quests.TryClaimReward(selectedQuest.QuestId, out float reward)) return;
            claimedQuestGold = reward;
            RefreshQuestPage();
        }

        private void TickQuestPage()
        {
            if (Time.unscaledTime < nextQuestRefresh) return;
            nextQuestRefresh = Time.unscaledTime + 1f;
            quests.RefreshPeriods();
            dailyClock.text = QuestCountdown(MiningQuestPeriod.Daily);
            weeklyClock.text = QuestCountdown(MiningQuestPeriod.Weekly);
            npcProgress.text = questEncounter ? questEncounter.ActiveNpcQuestProgress : "";
        }

        private string QuestCountdown(MiningQuestPeriod period)
        {
            TimeSpan remaining = quests.GetTimeUntilReset(period);
            return remaining.Days > 0
                ? Format("MENU_QUEST_RESET_DAYS", "{0}d {1:00}:{2:00}:{3:00}",
                    remaining.Days, remaining.Hours, remaining.Minutes, remaining.Seconds)
                : Format("MENU_QUEST_RESET", "Reset {0:00}:{1:00}:{2:00}",
                    remaining.Hours, remaining.Minutes, remaining.Seconds);
        }
    }
}
