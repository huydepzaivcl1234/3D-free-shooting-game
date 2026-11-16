using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using GameData.Player.Script;

namespace MiningSimulator.Ores
{
    internal sealed class SurvivorQuestDialogue
    {
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly Action Action;
            public readonly TMP_FontAsset Font;
            public Choice(string label, Action action, TMP_FontAsset font = null)
            {
                Label = label;
                Action = action;
                Font = font;
            }
        }

        private readonly UiPanelCoordinator coordinator;
        private readonly MiningAudioManager audio;
        private readonly QuestDialogueData settings;
        private readonly Sprite roundedSprite;
        private readonly RectTransform speakerBadge;
        private readonly TMP_Text advanceHint;
        private readonly GameObject root;
        private readonly RectTransform modal;
        private readonly TMP_Text nameLabel, dialogue, completion;
        private readonly TMP_FontAsset defaultFont;
        private float completionElapsed;
        private readonly List<(Button button, TMP_Text label)> buttons = new();
        private readonly RectTransform row;
        private RectTransform overhead;
        private CanvasGroup speechBubble;
        private TMP_Text bubbleLabel, questMarker, interactHint;
        private float bubbleUntil;
        private bool wasInRange;
        private Vector3 dialoguePosition, dialogueFocus;
        private readonly List<(Renderer renderer, bool hidden)> playerRenderers = new();
        private Camera camera;
        private Transform npc, npcVisual;
        private Vector3 visualScale, localFace;
        private float speechStarted, nextBlip;
        private int lastAdvanceFrame = -1;
        private bool typing, hasChoices;
        private Action advance;
        private const string BounceLink = "dialogue-bounce";
        private readonly List<(int first, int end)> bounceRanges = new();
        private Vector3 originalPosition;
        private Quaternion originalRotation;
        private float originalFov, elapsed, nextCharacter, previousTimeScale;
        private int character, characterCount;
        private bool isOpen;
        private CursorLockMode previousCursor;
        private bool previousCursorVisible;

        public SurvivorQuestDialogue(UiPanelCoordinator coordinator, MiningAudioManager audio, QuestDialogueData settings)
        {
            this.coordinator = coordinator;
            this.audio = audio;
            this.settings = settings;
            roundedSprite = settings.questDialogueFrame;
            root = new GameObject("Survivor Quest UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 105;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var parent = (RectTransform)root.transform;

            completion = Text(Rect(parent, "Quest completed", new Vector2(.5f, .75f), Vector2.zero, new Vector2(760, 90)), 30);
            completion.alignment = TextAlignmentOptions.Center;
            completion.gameObject.SetActive(false);

            modal = Rect(parent, "Quest dialogue", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            modal.anchorMax = Vector2.one;
            var blocker = modal.gameObject.AddComponent<Image>();
            blocker.color = new Color(0, 0, 0, .18f);
            var click = modal.gameObject.AddComponent<Button>();
            click.targetGraphic = blocker;
            click.transition = Selectable.Transition.None;
            click.navigation = new Navigation { mode = Navigation.Mode.None };
            click.onClick.AddListener(Advance);

            var panel = Rect(modal, "DialoguePanel", new Vector2(.5f, 0), new Vector2(0, 124),
                new Vector2(0, 140), new Vector2(.5f, 0));
            panel.anchorMin = new Vector2(.15f, 0);
            panel.anchorMax = new Vector2(.85f, 0);
            RoundedImage(panel, new Color(.045f, .078f, .105f, .98f)).raycastTarget = false;

            speakerBadge = Rect(panel, "SpeakerBadge", new Vector2(0, 1), new Vector2(22, 0),
                new Vector2(180, 44), new Vector2(0, .5f));
            RoundedImage(speakerBadge, new Color(.88f, .71f, .42f), 1.42f).raycastTarget = false;
            var name = Rect(speakerBadge, "NPC name", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            name.anchorMax = Vector2.one;
            name.offsetMin = new Vector2(18, 0);
            name.offsetMax = new Vector2(-18, 0);
            nameLabel = Text(name, 22);
            nameLabel.alignment = TextAlignmentOptions.MidlineLeft;
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            nameLabel.color = new Color(.085f, .10f, .12f);

            var body = Rect(panel, "NPC dialogue", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            body.anchorMax = Vector2.one;
            body.offsetMin = new Vector2(27, 14);
            body.offsetMax = new Vector2(-27, -37);
            dialogue = Text(body, 27);
            dialogue.overflowMode = TextOverflowModes.Page;
            defaultFont = dialogue.font;

            advanceHint = Text(Rect(modal, "AdvanceHint", new Vector2(.5f, 0), new Vector2(0, 74),
                new Vector2(600, 34)), 17);
            advanceHint.alignment = TextAlignmentOptions.Center;
            advanceHint.color = new Color(.91f, .88f, .79f, .9f);
            advanceHint.gameObject.SetActive(false);

            row = Rect(modal, "ChoicePanel", new Vector2(0, 0), Vector2.zero, Vector2.zero, Vector2.zero);
            row.anchorMin = new Vector2(.15f, 0);
            row.anchorMax = new Vector2(.85f, 0);
            row.offsetMin = new Vector2(0, 38);
            row.offsetMax = new Vector2(0, 106);
            row.gameObject.SetActive(false);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            modal.gameObject.SetActive(false);
        }

        private void AddChoiceButton()
        {
            var option = Rect(row, $"Choice {buttons.Count + 1}", Vector2.zero, Vector2.zero, Vector2.zero);
            var fill = RoundedImage(option, new Color(.14f, .23f, .29f, .98f));
            var button = option.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var label = Rect(option, "Label", Vector2.zero, Vector2.zero, Vector2.zero);
            label.anchorMax = Vector2.one;
            label.offsetMin = new Vector2(7, 2);
            label.offsetMax = new Vector2(-7, -2);
            var text = Text(label, 20);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 14;
            text.fontSizeMax = 20;
            buttons.Add((button, text));
        }

        public void ShowCompletion(string objective)
        {
            completion.text = (completionElapsed == 0 && completion.gameObject.activeSelf
                ? completion.text + "\n" : MiningLocalization.TextKey("QUEST_COMPLETE", "Quest complete") + "\n") + objective;
            completionElapsed = 0;
            completion.color = new Color(.40f, 1f, .61f);
            completion.rectTransform.localScale = Vector3.one * .88f;
            completion.gameObject.SetActive(true);
        }

        public void AttachOverhead(Transform target)
        {
            overhead = Rect(target, "Quest Overhead", new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 104));
            var canvas = overhead.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            overhead.localScale = Vector3.one * .0026f;

            float top = FindFace(target).y;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    top = Mathf.Max(top, renderer.bounds.max.y);
            overhead.position = target.position + Vector3.up * (top - target.position.y + .48f);

            var badge = Rect(overhead, "Quest Available", new Vector2(.5f, .5f), Vector2.zero, new Vector2(112, 116));
            RoundedImage(badge, new Color(.09f, .07f, .035f, .96f)).raycastTarget = false;
            questMarker = Text(Rect(badge, "Exclamation", new Vector2(.5f, .5f), Vector2.zero, new Vector2(108, 116)), 108);
            questMarker.text = "!";
            questMarker.alignment = TextAlignmentOptions.Center;
            questMarker.fontStyle = FontStyles.Bold;
            questMarker.outlineColor = new Color32(94, 43, 6, 255);
            questMarker.outlineWidth = .18f;

            interactHint = Text(Rect(overhead, "Interact", new Vector2(.5f, .5f), new Vector2(0, -72), new Vector2(74, 34)), 28);
            interactHint.alignment = TextAlignmentOptions.Center;
            interactHint.text = "[E]";

            var bubble = Rect(overhead, "No Quest Bubble", new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 84));
            RoundedImage(bubble, new Color(.07f, .11f, .16f, .94f)).raycastTarget = false;
            speechBubble = bubble.gameObject.AddComponent<CanvasGroup>();
            var textRect = Rect(bubble, "Message", new Vector2(.5f, .5f), Vector2.zero, new Vector2(312, 66));
            bubbleLabel = Text(textRect, 24);
            bubbleLabel.alignment = TextAlignmentOptions.Center;
            overhead.gameObject.SetActive(false);
        }

        public void UpdateOverhead(bool visible, bool inRange, bool available, string message)
        {
            if (!overhead) return;
            float now = Time.unscaledTime;
            if (!visible || isOpen)
            {
                overhead.gameObject.SetActive(false);
                return;
            }

            if (inRange && !wasInRange && !available)
                bubbleUntil = now + 2f;
            wasInRange = inRange;
            if (available) bubbleUntil = 0;

            bool showBubble = now < bubbleUntil;
            overhead.gameObject.SetActive(available || showBubble);
            if (!overhead.gameObject.activeSelf) return;

            var view = Camera.main;
            if (view) overhead.rotation = view.transform.rotation;
            questMarker.transform.parent.gameObject.SetActive(available);
            interactHint.gameObject.SetActive(available && inRange);
            questMarker.color = Color.Lerp(new Color(1f, .78f, .24f), new Color(1f, .97f, .67f),
                .5f + .5f * Mathf.Sin(now * 4f));
            speechBubble.gameObject.SetActive(showBubble);
            if (showBubble)
            {
                if (bubbleLabel.text != message) bubbleLabel.text = message;
                speechBubble.alpha = Mathf.Clamp01((bubbleUntil - now) / .35f);
            }
        }


        public void Open(Transform target, TopDownSurvivor view)
        {
            if (isOpen) return;
            isOpen = true;
            npc = target;
            npcVisual = npc.childCount > 0 ? npc.GetChild(0) : null;
            if (npcVisual) visualScale = npcVisual.localScale;
            camera = view && view.viewCamera ? view.viewCamera : Camera.main;
            if (camera)
            {
                originalPosition = camera.transform.position;
                originalRotation = camera.transform.rotation;
                originalFov = camera.fieldOfView;
                dialogueFocus = FindFace(npc);
                if (npcVisual) localFace = npcVisual.InverseTransformPoint(dialogueFocus);
                dialoguePosition = dialogueFocus + npc.forward * 2.2f + npc.right * .30f + Vector3.up * .04f;
            }
            if (view)
            {
                foreach (var renderer in view.GetComponentsInChildren<Renderer>())
                {
                    if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                    playerRenderers.Add((renderer, renderer.forceRenderingOff));
                    renderer.forceRenderingOff = true;
                }
            }
            elapsed = 0;
            previousTimeScale = Time.timeScale;
            previousCursor = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            modal.gameObject.SetActive(true);
            if (coordinator) coordinator.NotifyExternalPanelOpened(modal);
            else Time.timeScale = 0;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (overhead) overhead.gameObject.SetActive(false);
        }

        private static Vector3 FindFace(Transform target)
        {
            var animator = target.GetComponentInChildren<Animator>();
            Transform head = null;
            foreach (var child in target.GetComponentsInChildren<Transform>())
            {
                if (child.name == "DialogueFace") return child.position;
                if (child.name.Equals("Head", StringComparison.OrdinalIgnoreCase) ||
                    child.name.EndsWith(":Head", StringComparison.OrdinalIgnoreCase)) head = child;
            }
            if (animator && animator.isHuman && animator.avatar && animator.avatar.isValid)
            {
                var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
                if (left && right) return (left.position + right.position) * .5f;
                var humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
                if (humanoidHead) head = humanoidHead;
            }

            var mesh = target.GetComponentInChildren<SkinnedMeshRenderer>();
            float height = mesh ? mesh.bounds.size.y : 2f;
            return head ? head.position + target.up * (.06f * height) + target.forward * (.045f * height)
                : target.position + target.up * (height * .85f);
        }

        public void Display(string speaker, string text, TMP_FontAsset font, Action next, params Choice[] choices)
        {
            nameLabel.text = speaker;
            speakerBadge.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Clamp(nameLabel.preferredWidth + 36f, 120f, 440f));
            dialogue.font = font ? font : defaultFont;
            bool hasBounce = text.Contains("<bounce>", StringComparison.Ordinal);
            dialogue.text = hasBounce
                ? text.Replace("<bounce>", "<link=\"" + BounceLink + "\">").Replace("</bounce>", "</link>")
                : text;
            dialogue.pageToDisplay = 1;
            dialogue.maxVisibleCharacters = 0;
            dialogue.ForceMeshUpdate(true);
            character = 0;
            characterCount = dialogue.textInfo.characterCount;
            bounceRanges.Clear();
            if (hasBounce)
            {
                var info = dialogue.textInfo;
                for (int i = 0; i < info.linkCount; i++)
                {
                    var link = info.linkInfo[i];
                    if (link.GetLinkID() == BounceLink)
                        bounceRanges.Add((link.linkTextfirstCharacterIndex, link.linkTextfirstCharacterIndex + link.linkTextLength));
                }
            }
            nextCharacter = nextBlip = speechStarted = Time.unscaledTime;
            typing = true;
            advance = next;
            hasChoices = choices.Length > 0;
            row.gameObject.SetActive(false);
            advanceHint.gameObject.SetActive(false);
            while (buttons.Count < choices.Length) AddChoiceButton();
            for (int i = 0; i < buttons.Count; i++)
            {
                var (button, label) = buttons[i];
                bool hasChoice = i < choices.Length;
                button.gameObject.SetActive(hasChoice);
                button.onClick.RemoveAllListeners();
                if (!hasChoice) continue;
                Choice choice = choices[i];
                label.font = choice.Font ? choice.Font : defaultFont;
                label.text = choice.Label;
                button.onClick.AddListener(() =>
                {
                    if (audio && settings.choiceClickSound) audio.PlaySfx(settings.choiceClickSound);
                    choice.Action();
                });
            }
        }

        public void Tick()
        {
            if (completion.gameObject.activeSelf)
            {
                completionElapsed += Time.unscaledDeltaTime;
                float pop = Mathf.Clamp01(completionElapsed / .25f);
                completion.rectTransform.localScale = Vector3.one * (.88f + .12f * pop + .16f * Mathf.Sin(pop * Mathf.PI));
                completion.alpha = 1 - Mathf.Clamp01((completionElapsed - 2.5f) / .5f);
                if (completionElapsed >= 3f) completion.gameObject.SetActive(false);
            }
            if (!isOpen) return;
            if (!modal.gameObject.activeInHierarchy || !npc || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                Close();
                return;
            }

            TickNpcMotion();
            if (camera)
            {
                elapsed += Time.unscaledDeltaTime;
                const float centerTime = .16f;
                float center = Mathf.SmoothStep(0, 1, elapsed / centerTime);
                float blend = Mathf.SmoothStep(0, 1, (elapsed - centerTime) / .4f);
                Vector3 position = Vector3.Lerp(originalPosition, dialoguePosition, blend);
                Vector3 focus = npcVisual ? npcVisual.TransformPoint(localFace) : dialogueFocus;
                Quaternion centered = Quaternion.LookRotation(focus - position);
                camera.transform.SetPositionAndRotation(position, Quaternion.Slerp(originalRotation, centered, center));
                camera.fieldOfView = Mathf.Lerp(originalFov, 38, blend);
            }

            if (Keyboard.current?.spaceKey.wasPressedThisFrame == true) Advance();
            if (!isOpen) return;

            if (typing)
            {
                float now = Time.unscaledTime;
                bool blip = false;
                int end = CurrentPageEnd;
                float characterDelay = 1f / Mathf.Max(1f, settings.questTypingCharactersPerSecond);
                while (character < end && now >= nextCharacter)
                {
                    char c = dialogue.textInfo.characterInfo[character++].character;
                    blip |= char.IsLetterOrDigit(c);
                    nextCharacter += characterDelay + (c is '.' or '!' or '?' or '…' ? .25f :
                        c is ',' or ';' or ':' ? .12f : 0f);
                }
                dialogue.maxVisibleCharacters = character;
                if (blip && now >= nextBlip && audio && settings.questTypingSound)
                {
                    audio.PlayDialogueBlip(settings.questTypingSound);
                    nextBlip = now + .04f;
                }
                if (character >= end) FinishTyping();
            }
            if (bounceRanges.Count > 0) AnimateKeywords();
        }

        private int CurrentPageEnd => dialogue.pageToDisplay < dialogue.textInfo.pageCount
            ? dialogue.textInfo.pageInfo[dialogue.pageToDisplay - 1].lastCharacterIndex + 1 : characterCount;

        private void Advance()
        {
            if (!isOpen || lastAdvanceFrame == Time.frameCount) return;
            lastAdvanceFrame = Time.frameCount;
            if (typing)
            {
                FinishTyping();
                return;
            }
            if (dialogue.pageToDisplay < dialogue.textInfo.pageCount)
            {
                dialogue.pageToDisplay++;
                typing = true;
                nextCharacter = nextBlip = Time.unscaledTime;
                advanceHint.gameObject.SetActive(false);
                return;
            }
            if (!hasChoices) advance?.Invoke();
        }

        private void FinishTyping()
        {
            typing = false;
            character = CurrentPageEnd;
            dialogue.maxVisibleCharacters = character;
            bool lastPage = dialogue.pageToDisplay >= dialogue.textInfo.pageCount;
            row.gameObject.SetActive(lastPage && hasChoices);
            advanceHint.gameObject.SetActive(!lastPage || !hasChoices);
            if (advanceHint.gameObject.activeSelf)
                advanceHint.text = MiningLocalization.TextKey("QUEST_ADVANCE_HINT", "Click or press Space to continue");
        }

        private void TickNpcMotion()
        {
            if (!npcVisual) return;
            float phase = Mathf.Clamp01((Time.unscaledTime - speechStarted) / .36f);
            float spring = Mathf.Sin(phase * Mathf.PI * 2f) * (1f - phase);
            npcVisual.localScale = Vector3.Scale(visualScale, new Vector3(
                1f + .035f * spring, 1f - .065f * spring, 1f + .035f * spring));
        }

        private void AnimateKeywords()
        {
            var info = dialogue.textInfo;
            float time = Time.unscaledTime;
            bool changed = false;
            foreach (var range in bounceRanges)
            {
                int end = Mathf.Min(range.end, character);
                for (int i = range.first; i < end; i++)
                {
                    var glyph = info.characterInfo[i];
                    if (!glyph.isVisible || glyph.pageNumber != dialogue.pageToDisplay - 1) continue;
                    Vector3 offset = Vector3.up * (Mathf.Sin(time * 8f - i * .5f) * 2.5f);
                    var vertices = info.meshInfo[glyph.materialReferenceIndex].vertices;
                    int vertex = glyph.vertexIndex;
                    vertices[vertex] = glyph.vertex_BL.position + offset;
                    vertices[vertex + 1] = glyph.vertex_TL.position + offset;
                    vertices[vertex + 2] = glyph.vertex_TR.position + offset;
                    vertices[vertex + 3] = glyph.vertex_BR.position + offset;
                    changed = true;
                }
            }
            if (changed) dialogue.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        public void Close()
        {
            if (!isOpen) return;
            isOpen = false;
            if (coordinator) coordinator.NotifyExternalPanelClosed(modal);
            else Time.timeScale = previousTimeScale;
            modal.gameObject.SetActive(false);
            Cursor.lockState = previousCursor;
            Cursor.visible = previousCursorVisible;
            if (camera)
            {
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                camera.fieldOfView = originalFov;
            }
            foreach (var entry in playerRenderers)
                if (entry.renderer) entry.renderer.forceRenderingOff = entry.hidden;
            playerRenderers.Clear();
            if (npcVisual) npcVisual.localScale = visualScale;
            npcVisual = null;
            advance = null;
            camera = null;
            npc = null;
        }

        public void Dispose()
        {
            Close();
            if (root) UnityEngine.Object.Destroy(root);
        }

        private Image RoundedImage(RectTransform rect, Color color, float pixelsPerUnit = 2.55f)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = roundedSprite;
            if (roundedSprite)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = pixelsPerUnit;
            }
            image.color = color;
            return image;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor,
            Vector2 position, Vector2 size, Vector2? pivot = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot ?? new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text Text(RectTransform rect, float size)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = Resources.Load<TMP_FontAsset>("SurvivorRoboto");
            if (font) label.font = font;
            label.fontSize = size;
            label.color = new Color(.96f, .94f, .89f);
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }
    }
}
