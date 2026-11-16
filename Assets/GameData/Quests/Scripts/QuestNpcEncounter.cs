using System;
using System.Collections.Generic;
using DialogueEditor;
using GameData.Player.Manager;
using GameData.Player.Script;
using UnityEngine;
using QuestGoal = MiningSimulator.Ores.QuestDialogueData.QuestGoal;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MiningSimulator.Ores
{
    internal sealed class QuestNpcEncounter
    {
        internal enum QuestObjective { Mushroom, Stamina, EasyWalk, Golem, Damage, HardWalk }

        [Serializable]
        internal sealed class QuestProgress
        {
            public int objective;
            public float target;
            public float current;
            public bool Complete => current >= target;
        }

        [Serializable]
        internal sealed class QuestEncounter
        {
            public Vector3 origin;
            public Vector3 location;
            public bool placed;
            public int completedObjectives;
            public List<int> completedQuestIds = new();
            public QuestProgress easy;
            public QuestProgress hard;
        }

        internal delegate bool FindGround(Vector3 position, out RaycastHit ground);

        private readonly QuestDialogueData settings;
        private readonly CharacterHealth player;
        private readonly PlayerStats playerStats;
        private readonly UiPanelCoordinator uiCoordinator;
        private readonly MiningAudioManager audioManager;
        private readonly Scene scene;
        private readonly FindGround tryFindGround;
        private readonly Func<float> elapsedSeconds;
        private readonly Action saveWorld;
        private bool running;
        private QuestEncounter questEncounter;
        internal QuestEncounter State => questEncounter;
        private QuestNpcAnchor questNpcAnchor;
        private GameObject questNpc;
        private Conversation questConversation;
        private readonly HashSet<int> offeredQuestIds = new();
        private SurvivorQuestDialogue questDialogue;
        private AnimationRunMana questStamina;
        private InputManager questInput;
        private Rigidbody questBody;
        private MiningCardSystem questCards;
        private float questPlacementRetry;
        private Vector3 questPreviousPosition;
        private bool questPositionSampled;
        private readonly List<Vector3> questPath = new();

        internal QuestNpcEncounter(QuestDialogueData settings, CharacterHealth player, PlayerStats playerStats,
            UiPanelCoordinator uiCoordinator, MiningAudioManager audioManager, Scene scene,
            FindGround tryFindGround, Func<float> elapsedSeconds, Action saveWorld)
        {
            this.settings = settings;
            this.player = player;
            this.playerStats = playerStats;
            this.uiCoordinator = uiCoordinator;
            this.audioManager = audioManager;
            this.scene = scene;
            this.tryFindGround = tryFindGround;
            this.elapsedSeconds = elapsedSeconds;
            this.saveWorld = saveWorld;
        }

        internal void Start()
        {
            if (running || !player) return;
            running = true;
            questEncounter ??= new QuestEncounter { origin = player.transform.position };
            questNpcAnchor = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.activeInHierarchy) continue;
                questNpcAnchor = root.GetComponentInChildren<QuestNpcAnchor>();
                if (questNpcAnchor) break;
            }
            questStamina = player.GetComponent<AnimationRunMana>();
            questInput = player.GetComponent<InputManager>();
            questBody = player.GetComponent<Rigidbody>();
            MonsterKillCredit.Defeated += OnQuestKill;
            CharacterHealth.AnyDamaged += OnQuestDamage;
            if (questStamina) questStamina.Spent += OnQuestStamina;
        }

        internal void Stop()
        {
            if (!running) return;
            running = false;
            MonsterKillCredit.Defeated -= OnQuestKill;
            CharacterHealth.AnyDamaged -= OnQuestDamage;
            if (questStamina) questStamina.Spent -= OnQuestStamina;
            questDialogue?.Dispose();
            questDialogue = null;
            questConversation = null;
            offeredQuestIds.Clear();
            if (questNpc)
            {
                questNpc.SetActive(false);
                Object.Destroy(questNpc);
            }
            questNpc = null;
            questPositionSampled = false;
        }

        internal void Reset()
        {
            Stop();
            questEncounter = null;
            questPlacementRetry = 0;
            Start();
        }

        internal void Restore(QuestEncounter saved)
        {
            questEncounter = saved;
            saved.completedQuestIds ??= new();
            questEncounter.easy = RestoreQuestProgress(saved.easy, false);
            questEncounter.hard = RestoreQuestProgress(saved.hard, true);
        }

        internal void Tick()
        {
            if (!running || questEncounter == null || !player) return;
            questDialogue?.Tick();
            if (player.Health <= 0)
            {
                questDialogue?.Close();
                questDialogue?.UpdateOverhead(false, false, false, "");
                questPositionSampled = false;
                return;
            }

            if (!questNpc && Time.deltaTime > 0 && Time.time >= questPlacementRetry)
            {
                questPlacementRetry = Time.time + 1f;
                TryPlaceQuestNpc();
            }
            Vector3 position = questBody ? questBody.position : player.transform.position;
            bool playing = Time.deltaTime > 0 && (!uiCoordinator || !uiCoordinator.BlocksGameplay);
            if (playing && questPositionSampled && questInput && questInput.Move.sqrMagnitude > .01f)
            {
                Vector3 travel = position - questPreviousPosition;
                travel.y = 0;
                float distance = travel.magnitude;
                float maxStep = (playerStats ? playerStats.RunSpeed : 5f) * (Time.deltaTime + Time.fixedDeltaTime) * 2f;
                if (distance <= maxStep)
                {
                    AddQuestProgress(QuestGoal.Travel, distance);
                }
            }
            questPreviousPosition = position;
            questPositionSampled = playing;
            if (playing)
            {
                bool rewarded = TryCompleteQuest(false, position);
                rewarded |= TryCompleteQuest(true, position);
                if (rewarded)
                {
                    saveWorld();
                    GameSave.Save();
                }
            }
            if (!questNpc) return;

            Vector3 toward = position - questNpc.transform.position;
            bool near = toward.sqrMagnitude <= settings.questTalkDistance * settings.questTalkDistance;
            bool available = HasAvailableQuest();
            questDialogue?.UpdateOverhead(playing, near, available, available || !playing ? "" : NoQuestMessage());
            if (!playing || !near) return;
            toward.y = 0;
            if (toward.sqrMagnitude > .001f)
                questNpc.transform.rotation = Quaternion.LookRotation(toward);
            if (available && Keyboard.current?.eKey.wasPressedThisFrame == true && questDialogue != null)
            {
                OpenQuestConversation();
            }
        }

        private void TryPlaceQuestNpc()
        {
            if (!questNpcAnchor && !questEncounter.placed)
            {
                var grid = WorldNavigationGrid.Instance;
                if (!grid || !grid.HasBaked) return;

                float minimum = Mathf.Max(0, settings.questMinDistance);
                float maximum = Mathf.Max(minimum, settings.questMaxDistance);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    float radius = UnityEngine.Random.Range(minimum, maximum);
                    var candidate = questEncounter.origin + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                    if (!tryFindGround(candidate, out var ground) ||
                        !grid.IsCapsulePlacementClear(ground.point, .42f, 2.2f) ||
                        !grid.TryFindPath(questEncounter.origin, ground.point, questPath)) continue;
                    questEncounter.location = ground.point;
                    questEncounter.placed = true;
                    break;
                }
                if (!questEncounter.placed) return;
            }

            questNpc = new GameObject("Quest NPC");
            SceneManager.MoveGameObjectToScene(questNpc, scene);
            if (questNpcAnchor)
                questNpc.transform.SetPositionAndRotation(questNpcAnchor.transform.position, questNpcAnchor.transform.rotation);
            else
                questNpc.transform.position = questEncounter.location;
            if (settings.questNpcModel)
            {
                var model = Object.Instantiate(settings.questNpcModel, questNpc.transform, false);
                foreach (var body in model.GetComponentsInChildren<Rigidbody>())
                {
                    body.useGravity = false;
                    body.isKinematic = true;
                }
                var animator = model.GetComponentInChildren<Animator>();
                if (animator)
                {
                    animator.applyRootMotion = false;
                    animator.updateMode = AnimatorUpdateMode.Normal;
                    if (settings.questNpcAnimator)
                        animator.runtimeAnimatorController = settings.questNpcAnimator;
                    if (!string.IsNullOrWhiteSpace(settings.questNpcIdleState))
                    {
                        int state = Animator.StringToHash(settings.questNpcIdleState);
                        if (animator.HasState(0, state)) animator.Play(state, 0);
                    }
                }
            }
            else
            {
                var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                model.transform.SetParent(questNpc.transform, false);
                model.transform.localPosition = Vector3.up;
                model.GetComponent<Collider>().enabled = false;
            }

            questDialogue ??= new SurvivorQuestDialogue(uiCoordinator, audioManager, settings);
            questDialogue.AttachOverhead(questNpc.transform);
            ReadOfferedQuestIds();
        }

        private static bool IsBuiltIn(int id) => id >= 0 && id <= (int)QuestObjective.HardWalk;

        private bool IsHardQuest(int id) => IsBuiltIn(id)
            ? id >= (int)QuestObjective.Golem : settings.FindQuest(id)?.hard == true;

        private QuestProgress RestoreQuestProgress(QuestProgress quest, bool hard)
        {
            // JsonUtility can deserialize an absent inline record as a zeroed object.
            if (quest == null || !float.IsFinite(quest.target) || quest.target <= 0 ||
                (IsBuiltIn(quest.objective) ? IsHardQuest(quest.objective) != hard : settings.FindQuest(quest.objective) == null))
                return null;
            quest.current = float.IsFinite(quest.current) ? Mathf.Clamp(quest.current, 0, quest.target) : 0;
            return quest;
        }

        private float QuestTarget(int id)
        {
            int steps = Mathf.FloorToInt(elapsedSeconds() / Mathf.Max(1, settings.questScalingSeconds));
            var definition = settings.FindQuest(id);
            if (definition != null)
                return Mathf.Ceil(Mathf.Max(1f, definition.amount) + steps * definition.growthPerStep);

            // Convert the designer-entered decimal rates before ceiling a scaled goal.
            decimal scale = 1 + steps * (decimal)(IsHardQuest(id) ? settings.hardQuestGrowth : settings.easyQuestGrowth);
            return ((QuestObjective)id) switch
            {
                QuestObjective.Mushroom => settings.questMushroomCount + steps,
                QuestObjective.Stamina => (float)Math.Ceiling((decimal)settings.questStaminaAmount * scale),
                QuestObjective.EasyWalk => (float)Math.Ceiling((decimal)settings.questEasyDistance * scale),
                QuestObjective.Golem => settings.questGolemCount + steps,
                QuestObjective.Damage => (float)Math.Ceiling((decimal)settings.questDamageAmount * scale),
                _ => (float)Math.Ceiling((decimal)settings.questHardDistance * scale)
            };
        }

        private bool IsQuestAvailable(int objective)
        {
            if (questEncounter.easy?.objective == objective || questEncounter.hard?.objective == objective ||
                (IsHardQuest(objective) ? questEncounter.hard : questEncounter.easy) != null) return false;

            if (IsBuiltIn(objective))
                return (questEncounter.completedObjectives & (1 << objective)) == 0;

            var definition = settings.FindQuest(objective);
            return definition != null && (definition.goal != QuestGoal.DefeatMonster || definition.monster) &&
                !questEncounter.completedQuestIds.Contains(objective);
        }

        private void AcceptQuest(int objective)
        {
            if (!IsQuestAvailable(objective)) return;
            var quest = new QuestProgress { objective = objective, target = QuestTarget(objective) };
            if (IsHardQuest(objective)) questEncounter.hard = quest;
            else questEncounter.easy = quest;
            questDialogue.Close();
            saveWorld();
            GameSave.Save();
        }

        private bool TryCompleteQuest(bool hard, Vector3 position)
        {
            var quest = hard ? questEncounter.hard : questEncounter.easy;
            if (quest == null || !quest.Complete) return false;

            if (!questCards) questCards = Object.FindAnyObjectByType<MiningCardSystem>();
            if (!questCards) return false;

            Transform playerTransform = player.transform;
            Vector3 side = playerTransform.right * (hard ? -1.6f : 1.6f);
            Vector3 landing = QuestRewardLanding(position, side);

            var animator = player.GetComponentInChildren<Animator>();
            var head = animator && animator.isHuman && animator.avatar && animator.avatar.isValid
                ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            float height = player.TryGetComponent<Collider>(out var playerCollider) ? playerCollider.bounds.max.y : position.y + 1.6f;
            Vector3 launch = (head ? head.position : new Vector3(position.x, height, position.z)) + Vector3.up * .15f;
            if (!questCards.TryDropQuestReward(hard, launch, landing)) return false;

            if (IsBuiltIn(quest.objective)) questEncounter.completedObjectives |= 1 << quest.objective;
            else questEncounter.completedQuestIds.Add(quest.objective);
            if (hard) questEncounter.hard = null;
            else questEncounter.easy = null;
            TryAwardBonusReward(launch, position, -side);
            questDialogue?.ShowCompletion(QuestStatus(quest));
            return true;
        }

        private Vector3 QuestRewardLanding(Vector3 position, Vector3 side)
        {
            var grid = WorldNavigationGrid.Instance;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 offset = Quaternion.AngleAxis(45f * attempt, Vector3.up) * side;
                if (tryFindGround(position + offset, out var ground) &&
                    (!grid || !grid.HasBaked || grid.IsCapsulePlacementClear(ground.point, .25f, .7f)))
                    return ground.point;
            }
            return position;
        }

        private void TryAwardBonusReward(Vector3 launch, Vector3 position, Vector3 side)
        {
            var support = PlayerSupportUpgrades.For(player);
            int level = support?.Level(SupportUpgradeId.QuestReward) ?? 0;
            if (level <= 0 || UnityEngine.Random.value >= (.05f + .0025f * (level - 1))) return;

            if (UnityEngine.Random.value < .5f && support.TryGrantRandom(out _)) return;
            bool ability = questCards.CanOfferAbilities;
            Vector3 landing = QuestRewardLanding(position, side);
            if (!questCards.TryDropQuestReward(ability, launch + side * .25f, landing) &&
                (!ability || !questCards.TryDropQuestReward(false, launch + side * .25f, landing)))
                support.TryGrantRandom(out _);
        }

        private void AddQuestProgress(QuestGoal goal, float amount, MonsterRewardData species = null)
        {
            if (questEncounter == null || amount <= 0) return;
            Advance(questEncounter.easy);
            Advance(questEncounter.hard);

            void Advance(QuestProgress quest)
            {
                if (quest == null || quest.Complete) return;
                var id = quest.objective;
                var definition = IsBuiltIn(id) ? null : settings.FindQuest(id);
                bool matches = definition != null
                    ? definition.goal == goal && (goal != QuestGoal.DefeatMonster || definition.monster == species)
                    : (QuestObjective)id switch
                    {
                        QuestObjective.Mushroom => goal == QuestGoal.DefeatMonster && species == settings.questMushroom,
                        QuestObjective.Golem => goal == QuestGoal.DefeatMonster && species == settings.questGolem,
                        QuestObjective.Stamina => goal == QuestGoal.SpendStamina,
                        QuestObjective.EasyWalk or QuestObjective.HardWalk => goal == QuestGoal.Travel,
                        QuestObjective.Damage => goal == QuestGoal.DealDamage,
                        _ => false
                    };
                if (matches) quest.current = Mathf.Min(quest.target, quest.current + amount);
            }
        }

        private void OnQuestKill(MonsterRewardData species, bool boss)
        {
            if (species && !boss) AddQuestProgress(QuestGoal.DefeatMonster, 1, species);
        }

        private void OnQuestDamage(CharacterHealth target, float damage, GameObject source, bool basic, CombatDamageType type)
        {
            if ((questEncounter?.easy == null && questEncounter?.hard == null) || !target || !target.IsMonster ||
                !MonsterKillCredit.IsPlayerOwned(source)) return;
            AddQuestProgress(QuestGoal.DealDamage, damage);
        }

        private void OnQuestStamina(float amount) => AddQuestProgress(QuestGoal.SpendStamina, amount);

        private string NpcName => string.IsNullOrWhiteSpace(settings.questNpcName)
            ? L("QUEST_NPC_NAME", "Quest Guide") : settings.questNpcName;

        private static string L(string key, string english) => MiningLocalization.TextKey(key, english);

        private static string QuestLabel(QuestObjective objective) => objective switch
        {
            QuestObjective.Mushroom => L("QUEST_KILL_MUSHROOM", "Defeat Mushroom"),
            QuestObjective.Stamina => L("QUEST_SPEND_STAMINA", "Spend stamina"),
            QuestObjective.Golem => L("QUEST_KILL_GOLEM", "Defeat Golem"),
            QuestObjective.Damage => L("QUEST_DEAL_DAMAGE", "Deal damage"),
            _ => L("QUEST_TRAVEL", "Travel (m)")
        };

        private string QuestStatus(QuestProgress quest) =>
            $"{QuestName(quest.objective)}  {Mathf.FloorToInt(quest.current)}/{quest.target:0}";

        private string QuestName(int id)
        {
            var definition = settings.FindQuest(id);
            return definition != null ? GraphText(definition.label ?? "") : QuestLabel((QuestObjective)id);
        }

        internal string QuestTracker()
        {
            if (!running || questEncounter == null) return "";
            var easy = questEncounter.easy;
            var hard = questEncounter.hard;
            if (easy == null) return hard == null ? "" : QuestStatus(hard);
            return hard == null ? QuestStatus(easy) : QuestStatus(easy) + "\n" + QuestStatus(hard);
        }

        private void ReadOfferedQuestIds()
        {
            offeredQuestIds.Clear();
            var root = settings.questConversation ? settings.questConversation.Deserialize()?.Root : null;
            if (root == null) return;
            var pending = new Stack<ConversationNode>();
            var visited = new HashSet<ConversationNode>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == null || !visited.Add(node)) continue;
                if (node is OptionNode option && QuestId(option) is int id && id >= 0)
                    offeredQuestIds.Add(id);
                foreach (var connection in node.Connections)
                {
                    if (connection is SpeechConnection speech) pending.Push(speech.SpeechNode);
                    else if (connection is OptionConnection choice) pending.Push(choice.OptionNode);
                }
            }
        }

        private bool HasAvailableQuest()
        {
            foreach (int id in offeredQuestIds)
                if (IsQuestAvailable(id)) return true;
            return false;
        }

        private string NoQuestMessage() => string.IsNullOrWhiteSpace(settings.noAvailableQuestDialogue)
            ? L("QUEST_NONE_AVAILABLE", "No new quests yet!")
            : GraphText(settings.noAvailableQuestDialogue);

        private void OpenQuestConversation()
        {
            if (!HasAvailableQuest() || !settings.questConversation) return;
            questConversation = settings.questConversation.Deserialize();
            if (questConversation?.Root == null) return;

            for (int id = 0; id <= (int)QuestObjective.HardWalk; id++)
                questConversation.SetBool(((QuestObjective)id).ToString(), offeredQuestIds.Contains(id) && IsQuestAvailable(id), out _);

            bool easy = false, hard = false;
            foreach (int id in offeredQuestIds)
            {
                if (!IsQuestAvailable(id)) continue;
                if (IsHardQuest(id)) hard = true;
                else easy = true;
            }
            questConversation.SetBool("EasyAvailable", easy, out _);
            questConversation.SetBool("HardAvailable", hard, out _);
            questDialogue.Open(questNpc.transform, player.GetComponent<TopDownSurvivor>());
            ShowQuestSpeech(questConversation.Root);
        }

        private void ShowQuestSpeech(SpeechNode speech)
        {
            if (speech == null) { questDialogue.Close(); return; }
            ApplyQuestNode(speech);
            var choices = new List<SurvivorQuestDialogue.Choice>();
            Action advance = questDialogue.Close;
            foreach (var connection in speech.Connections)
            {
                if (!QuestConditionsMet(connection)) continue;
                if (connection is SpeechConnection next)
                {
                    advance = () => ShowQuestSpeech(next.SpeechNode);
                    break;
                }
                var option = ((OptionConnection)connection).OptionNode;
                int questId = QuestId(option);
                string label = GraphText(option.Text);
                if (questId >= 0)
                {
                    if (!IsQuestAvailable(questId)) continue;
                    if (string.IsNullOrWhiteSpace(label)) label = QuestName(questId);
                    string amount = QuestTarget(questId).ToString("0");
                    label = label.Contains("{amount}") ? label.Replace("{amount}", amount) : label + "\n" + amount;
                }
                choices.Add(new SurvivorQuestDialogue.Choice(label, () => ChooseQuestOption(option, questId), option.TMPFont));
            }

            string speaker = string.IsNullOrWhiteSpace(speech.Name) ? NpcName : GraphText(speech.Name);
            questDialogue.Display(speaker, GraphText(speech.Text), speech.TMPFont, advance, choices.ToArray());
        }

        private void ChooseQuestOption(OptionNode option, int questId)
        {
            if (questId >= 0)
            {
                if (!IsQuestAvailable(questId)) return;
                ApplyQuestNode(option);
                AcceptQuest(questId);
                return;
            }
            ApplyQuestNode(option);
            foreach (var connection in option.Connections)
            {
                if (connection is SpeechConnection next && QuestConditionsMet(connection))
                {
                    ShowQuestSpeech(next.SpeechNode);
                    return;
                }
            }
            questDialogue.Close();
        }

        private static int QuestId(OptionNode option)
        {
            foreach (var action in option.ParamActions)
                if (action is SetIntParamAction select && select.ParameterName == "QuestChoice" &&
                    select.Value >= 0) return select.Value;
            return -1;
        }

        private void ApplyQuestNode(ConversationNode node)
        {
            foreach (var action in node.ParamActions)
            {
                if (action is SetIntParamAction number)
                    questConversation.SetInt(number.ParameterName, number.Value, out _);
                else if (action is SetBoolParamAction flag)
                    questConversation.SetBool(flag.ParameterName, flag.Value, out _);
            }
            if (node is SpeechNode speech) speech.Event?.Invoke();
            else if (node is OptionNode option) option.Event?.Invoke();
        }

        private bool QuestConditionsMet(Connection connection)
        {
            foreach (var condition in connection.Conditions)
            {
                if (condition is BoolCondition flag)
                {
                    bool value = questConversation.GetBool(flag.ParameterName, out var status);
                    bool equal = flag.CheckType == BoolCondition.eCheckType.equal;
                    if (status != eParamStatus.OK || (value == flag.RequiredValue) != equal) return false;
                }
                else if (condition is IntCondition number)
                {
                    int value = questConversation.GetInt(number.ParameterName, out var status);
                    if (status != eParamStatus.OK) return false;
                    bool matches = number.CheckType switch
                    {
                        IntCondition.eCheckType.lessThan => value < number.RequiredValue,
                        IntCondition.eCheckType.greaterThan => value > number.RequiredValue,
                        _ => value == number.RequiredValue
                    };
                    if (!matches) return false;
                }
            }
            return true;
        }

        private static string GraphText(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '{') return text ?? "";
            int end = text.IndexOf('}');
            return end > 1 ? L(text.Substring(1, end - 1), text.Substring(end + 1)) : text;
        }
    }
}
