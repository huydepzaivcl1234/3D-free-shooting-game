using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GameData.Player.Script;
namespace MiningSimulator.Ores
{
    public sealed partial class MiningCardSystem
    {
        private sealed class QueuedUpgrade
        {
            public MiningCardData.Tier tier;
            public float rarityMultiplier;
            public bool lootDraft;
            public List<SurvivorUpgradeData.Offer> rolled;
        }
        [System.Serializable]
        public sealed class SavedQueuedUpgrade
        {
            public bool ability;
            public bool lootDraft;
            public float rarityMultiplier;
            public List<SurvivorUpgradeData.Offer> rolled;
        }

        internal List<SavedQueuedUpgrade> CaptureRuntimeQueue()
        {
            var saved = new List<SavedQueuedUpgrade>(upgradeQueue.Count);
            if (!data) return saved;
            foreach (var entry in upgradeQueue)
                saved.Add(new SavedQueuedUpgrade
                {
                    ability = entry.tier == data.abilityCard,
                    lootDraft = entry.lootDraft,
                    rarityMultiplier = entry.rarityMultiplier,
                    rolled = entry.rolled
                });
            return saved;
        }

        internal void RestoreRuntimeQueue(List<SavedQueuedUpgrade> saved)
        {
            if (saved == null || !data || data.tiers == null || data.tiers.Length == 0) return;
            upgradeQueue.Clear();
            foreach (var entry in saved)
            {
                if (entry == null) continue;
                var tier = entry.ability ? data.abilityCard : data.tiers[0];
                if (tier == null) continue;
                upgradeQueue.Add(new QueuedUpgrade
                {
                    tier = tier,
                    lootDraft = entry.lootDraft,
                    rarityMultiplier = float.IsFinite(entry.rarityMultiplier) && entry.rarityMultiplier > 0
                        ? entry.rarityMultiplier : 1f,
                    rolled = entry.rolled != null && entry.rolled.Count > 0 ? entry.rolled : null
                });
            }
            RefreshQueueHud();
        }

        private readonly List<QueuedUpgrade> upgradeQueue=new();
        private QueuedUpgrade currentUpgrade;
        private bool choiceOpen;
        private RectTransform queueHud;
        private Button statQueueButton,abilityQueueButton;
        private int shownStats=-1,shownAbilities=-1;
        public int PendingStatUpgrades=>PendingCount(false);
        public int PendingAbilityUpgrades=>PendingCount(true);
        private int PendingCount(bool ability){int count=0;foreach(var q in upgradeQueue)if((q.tier==data.abilityCard)==ability)count++;return count;}
        public bool Collect(MiningCardData.Tier tier,float rarityMultiplier=1f)
        {
            return (CanCollect || choiceOpen) && Enqueue(tier, rarityMultiplier);
        }

        private bool Enqueue(MiningCardData.Tier tier, float rarityMultiplier, bool lootDraft = false)
        {
            if (!data || tier == null || data.tiers == null || data.tiers.Length == 0 ||
                (tier != data.tiers[0] && tier != data.abilityCard)) return false;
            if(data.survivorUpgrades&&!data.survivorUpgrades.HasEligible(player,tier==data.abilityCard))return false;
            upgradeQueue.Add(new QueuedUpgrade { tier = tier, rarityMultiplier = rarityMultiplier, lootDraft = lootDraft });
            RefreshQueueHud();
            return true;
        }
        private SurvivorDailyLoot lootDraftOwner;
        private Button lootRerollButton;
        private TMPro.TMP_Text lootRerollLabel;

        internal bool QueueLootDraft(float rarityMultiplier, SurvivorDailyLoot owner)
        {
            if (!CanCollect || choiceOpen || !data || !data.survivorUpgrades ||
                !Enqueue(data.abilityCard, rarityMultiplier, lootDraft: true)) return false;
            lootDraftOwner = owner;
            return true;
        }

        private void RefreshLootReroll()
        {
            if (currentUpgrade?.lootDraft != true)
            {
                if (lootRerollButton) lootRerollButton.gameObject.SetActive(false);
                return;
            }
            if (!lootDraftOwner) lootDraftOwner = FindAnyObjectByType<SurvivorDailyLoot>();
            if (!lootRerollButton)
            {
                var rect = Box(choicePanel, "Lootbox reroll", Vector2.zero, Vector2.one, Hex("305260"));
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, 0);
                rect.sizeDelta = new Vector2(186, 42);
                rect.anchoredPosition = new Vector2(165, 22);
                var image = rect.GetComponent<Image>();
                image.raycastTarget = true;
                lootRerollButton = rect.gameObject.AddComponent<Button>();
                lootRerollButton.targetGraphic = image;
                lootRerollButton.onClick.AddListener(RerollLootDraft);
                lootRerollLabel = Label(rect, "Label", "", 0, 0, 186, 42, 17, Hex("F5E0AB"));
                lootRerollLabel.alignment = TMPro.TextAlignmentOptions.Center;
            }
            int remaining = lootDraftOwner ? lootDraftOwner.RerollsRemaining : 0;
            lootRerollButton.transform.SetAsLastSibling();
            lootRerollButton.gameObject.SetActive(true);
            lootRerollButton.interactable = remaining > 0;
            lootRerollLabel.text = string.Format(CardText("LOOTBOX_REROLL", "REROLL ({0})"), remaining);
        }

        private void RerollLootDraft()
        {
            if (!choiceOpen || pickFeedback != null || currentUpgrade?.lootDraft != true ||
                !lootDraftOwner || lootDraftOwner.RerollsRemaining == 0) return;
            var roll = data.survivorUpgrades.Roll(player, 3, true, currentUpgrade.rarityMultiplier, offers);
            if (roll.Count == 0 || !lootDraftOwner.SpendReroll()) return;
            currentUpgrade.rolled = offers = roll;
            RefreshPresentation();
            RefreshLootReroll();
            RevealOffers();
            MonsterSpawnZone.CaptureWorldsIfDue(force: true);
        }

        public bool OpenQueued(bool ability)
        {
            if(!choiceOpen&&!CanCollect)return false;
            foreach(var q in upgradeQueue)if((q.tier==data.abilityCard)==ability)return OpenQueuedRequest(q);
            return false;
        }
        private bool OpenQueuedRequest(QueuedUpgrade request)
        {
            currentUpgrade=request;
            if(request.rolled==null){
                if(!BeginChoice(request.tier,request.rarityMultiplier)){currentUpgrade=null;return false;}
                request.rolled=offers;
            }else{
                offers=request.rolled;pending=request.tier;abilityChoice=request.tier==data.abilityCard;blueChoice=false;
                title.text=abilityChoice?"CHOOSE AN ABILITY":"CHOOSE AN UPGRADE";
                for(int i=0;i<choices.Length;i++){bool shown=i<offers.Count&&i<3;choices[i].gameObject.SetActive(shown);choices[i].interactable=shown;}
                RefreshPresentation();
                if(!pauseOwned){previousTimeScale=Time.timeScale;previousCursor=Cursor.lockState;previousCursorVisible=Cursor.visible;previousAudioPause=AudioListener.pause;pauseOwned=true;if(controller){controllerWasEnabled=controller.enabled;controller.enabled=false;}}
                choiceOpen=true;if(!coordinator)Time.timeScale=0;AudioListener.pause=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
                if(coordinator)coordinator.OpenPanel(choicePanel);else choicePanel.gameObject.SetActive(true);
            }
            RefreshLootReroll();
            PanelAudio(true);
            RevealOffers();
            return true;
        }
        private void CompleteQueuedChoice()
        {
            bool lootDraft = currentUpgrade?.lootDraft == true;
            if(currentUpgrade!=null)upgradeQueue.Remove(currentUpgrade);
            currentUpgrade=null;pending=null;offers=new();blueChoice=false;
            RefreshLootReroll();
            if (lootDraft) MonsterSpawnZone.CaptureWorldsIfDue(force: true);
            RefreshQueueHud();
            while (upgradeQueue.Count > 0)
            {
                var next = upgradeQueue[0];
                if (OpenQueuedRequest(next)) return;
                upgradeQueue.RemoveAt(0);
            }
            ClosePending();
        }
        public void ClosePending()
        {
            CancelPickFeedback();
            PanelAudio(false);
            choiceOpen=false;
            if(coordinator&&coordinator.isActiveAndEnabled&&choicePanel)coordinator.NotifyExternalPanelClosed(choicePanel);
            if(choicePanel)choicePanel.gameObject.SetActive(false);
            RestorePause();RefreshQueueHud();
        }
        private void EnsureCloseButton()
        {
            var rect=Box(choicePanel,"ClosePending",Vector2.zero,Vector2.one,Hex("30383D"));Pixel(rect,1384,24,44,44);
            var image=rect.GetComponent<Image>();image.raycastTarget=true;var button=rect.GetComponent<Button>();if(!button)button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            button.onClick.RemoveListener(ClosePending);button.onClick.AddListener(ClosePending);
            Label(rect,"X","X",0,0,44,44,23,Color.white).alignment=TMPro.TextAlignmentOptions.Center;
        }
        private void RefreshQueueHud()
        {
            if(!Application.isPlaying||!data||!title)return;
            if(!queueHud){
                var statsPanel=FindAnyObjectByType<PlayerStatsPanel>();var anchor=statsPanel?statsPanel.OpenButtonRect:null;if(!anchor)return;
                queueHud=new GameObject("Queued upgrade cards",typeof(RectTransform)).GetComponent<RectTransform>();queueHud.SetParent(anchor.parent,false);
                queueHud.anchorMin=anchor.anchorMin;queueHud.anchorMax=anchor.anchorMax;queueHud.pivot=anchor.pivot;queueHud.sizeDelta=new Vector2(150,84);queueHud.anchoredPosition=anchor.anchoredPosition+new Vector2(0,anchor.rect.height+18);
                statQueueButton=QueueButton("Stat card",0,Hex("B94257"),false);abilityQueueButton=QueueButton("Ability card",78,Hex("277FCA"),true);
            }
            bool showHud = !coordinator || !coordinator.IsMainMenuOpen;
            if (queueHud.gameObject.activeSelf != showHud) queueHud.gameObject.SetActive(showHud);
            int red=PendingStatUpgrades,blue=PendingAbilityUpgrades;if(red==shownStats&&blue==shownAbilities)return;
            shownStats=red;shownAbilities=blue;
            statQueueButton.gameObject.SetActive(red>0);abilityQueueButton.gameObject.SetActive(blue>0);
            Label((RectTransform)statQueueButton.transform.Find("CountBadge"),"Count",red.ToString(),0,0,26,26,17,Color.white).alignment=TMPro.TextAlignmentOptions.Center;
            Label((RectTransform)abilityQueueButton.transform.Find("CountBadge"),"Count",blue.ToString(),0,0,26,26,17,Color.white).alignment=TMPro.TextAlignmentOptions.Center;
        }
        private Button QueueButton(string name,float x,Color color,bool ability)
        {
            var rect=Box(queueHud,name,Vector2.zero,Vector2.one,Color.white);Pixel(rect,x,0,66,82);
            var image=rect.GetComponent<Image>();image.sprite=ability?data.abilityQueueIcon:data.statQueueIcon;image.type=Image.Type.Simple;image.preserveAspect=true;image.raycastTarget=true;
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>OpenQueued(ability));
            var badge=Box(rect,"CountBadge",Vector2.zero,Vector2.one,Hex("171B21"));Pixel(badge,40,0,26,26);
            return button;
        }
    }
}
