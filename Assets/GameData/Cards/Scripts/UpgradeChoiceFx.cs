using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
namespace MiningSimulator.Ores
{
    public sealed class UpgradeChoiceFx : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
    {
        [SerializeField,Tooltip("Disable to keep the prefab colors. Hover/punch/flip still animate.")] bool tintSurfaces=true;
        UpgradeSurface frame,action;TMP_Text label;Color accent;bool hover,press,acquired;
        RustUpgradeLayout.CardStyle rust;
        Button button;RectTransform rect,back;CanvasGroup interaction;
        readonly List<CanvasGroup> front=new();
        bool revealing, confirming, pointerInside;
        float revealElapsed, revealDelay, confirmStart, hoverPunchRemaining;
        int revealStartFrame, confirmSibling;
        const float PunchSeconds=.18f, FlipSeconds=.38f, BackHoldSeconds=.08f;
        const float HoverPunchSeconds = .24f, HoverPunchScale = .08f;
        Vector3 restScale;Quaternion restRotation;bool hasRestPose,settling;
        public bool IsRevealing=>revealing;
        public bool IsConfirming=>confirming;
        public void Configure(UpgradeSurface surface,UpgradeSurface cta,TMP_Text text,Color color,Sprite backIcon=null)
        {
            CaptureRestPose();
            ResetMotion();frame=surface;action=cta;label=text;accent=color;button=GetComponent<Button>();rect=(RectTransform)transform;
            if (TryGetComponent<UpgradeCardView>(out var card)) rust=card.RustStyle;
            interaction=GetComponent<CanvasGroup>();if(!interaction)interaction=gameObject.AddComponent<CanvasGroup>();
            BuildBack(backIcon);front.Clear();
            foreach(Transform child in transform){if(child==back)continue;var group=child.GetComponent<CanvasGroup>();if(!group)group=child.gameObject.AddComponent<CanvasGroup>();group.alpha=1;front.Add(group);}
            hover=press=acquired=false;Paint();
        }
        void CaptureRestPose(){if(!hasRestPose||!revealing&&!hover&&!press&&!acquired&&!settling){restScale=transform.localScale;restRotation=transform.localRotation;hasRestPose=true;}}
        public void PlayReveal(float delay)
        {
            ResetMotion();revealDelay=delay;revealElapsed=0;revealing=true;revealStartFrame=Time.frameCount;
            interaction.interactable=false;SetFront(false);back.gameObject.SetActive(true);back.SetAsLastSibling();
            rect.localRotation=restRotation*Quaternion.Euler(0,180,0);rect.localScale=restScale*.94f;
        }
        void BuildBack(Sprite icon)
        {
            if(!back)back=transform.Find("Card back") as RectTransform;
            if(back){var existing=back.Find("Card emblem")?.GetComponent<UnityEngine.UI.Image>();if(existing)existing.sprite=icon;return;}
            if(!back){var go=new GameObject("Card back",typeof(RectTransform),typeof(CanvasRenderer),typeof(UpgradeSurface));back=(RectTransform)go.transform;back.SetParent(transform,false);}
            back.anchorMin=Vector2.zero;back.anchorMax=Vector2.one;back.offsetMin=back.offsetMax=Vector2.zero;back.localRotation=Quaternion.Euler(0,180,0);
            if (rust != null)
                back.GetComponent<UpgradeSurface>().Paint(C("201C19"),C("201C19"),C("584A3E"),0,2);
            else
                back.GetComponent<UpgradeSurface>().Paint(C("223C48"),C("0D1925"),C("B6A57C"),18,2);
            var image=back.Find("Card emblem")?.GetComponent<UnityEngine.UI.Image>();
            if(!image){var go=new GameObject("Card emblem",typeof(RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(back,false);image=go.GetComponent<UnityEngine.UI.Image>();}
            var r=image.rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(0,20);r.sizeDelta=new Vector2(140,180);image.sprite=icon;image.preserveAspect=true;image.raycastTarget=false;image.gameObject.SetActive(icon);
            var caption=back.Find("Back label")?.GetComponent<TextMeshProUGUI>();
            if(!caption){var go=new GameObject("Back label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(back,false);caption=go.GetComponent<TextMeshProUGUI>();}
            r=caption.rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(0,-130);r.sizeDelta=new Vector2(310,90);caption.font=label?label.font:TMP_Settings.defaultFontAsset;caption.text="SURVIVOR\nSANCTUARY";caption.fontSize=19;caption.fontStyle=FontStyles.Bold;caption.color=C("D9B674");caption.alignment=TextAlignmentOptions.Center;caption.raycastTarget=false;
            back.gameObject.SetActive(false);
        }
        bool Usable=>!revealing&&!acquired&&button&&button.IsInteractable();
        public bool Confirm()
        {
            if (!isActiveAndEnabled || !hasRestPose || !back || revealing) return false;
            acquired = confirming = true;
            confirmStart = Time.unscaledTime;
            confirmSibling = transform.GetSiblingIndex();
            transform.SetAsLastSibling();
            Paint();
            return true;
        }
        public void OnPointerEnter(PointerEventData e)
        {
            pointerInside = true;
            if (Usable) StartHoverPunch();
        }

        void StartHoverPunch()
        {
            CaptureRestPose();
            hover = true;
            hoverPunchRemaining = HoverPunchSeconds;
            FindAnyObjectByType<MiningAudioManager>()?.PlayCardHoverSfx();
            Paint();
        }

        public void OnPointerExit(PointerEventData e)
        {
            pointerInside = hover = press = false;
            hoverPunchRemaining = 0f;
            settling = true;
            Paint();
        }

        public void OnPointerDown(PointerEventData e){press=Usable;hoverPunchRemaining=0f;Paint();}
        public void OnPointerUp(PointerEventData e){press=false;Paint();}
        public void OnSelect(BaseEventData e){if(Usable){CaptureRestPose();hover=true;Paint();}}
        public void OnDeselect(BaseEventData e)
        {
            press = false;
            if (!pointerInside)
            {
                hover = false;
                hoverPunchRemaining = 0f;
                settling = true;
            }
            Paint();
        }
        void Update()
        {
            if (confirming)
            {
                TickConfirm();
                return;
            }
            if(!hasRestPose||!revealing&&!hover&&!press&&!acquired&&!settling)return;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            if(revealing){
                if(Time.frameCount==revealStartFrame)return;
                revealElapsed+=dt;float t=Mathf.Clamp01((revealElapsed-revealDelay)/.46f);
                float eased=t*t*(3-2*t);rect.localRotation=restRotation*Quaternion.Euler(0,180*(1-eased),0);
                bool face=t>=.5f;SetFront(face);back.gameObject.SetActive(!face);
                float scale=.94f+.06f*eased+.035f*Mathf.Sin(t*Mathf.PI);rect.localScale=restScale*scale;
                if (t >= 1f)
                {
                    revealing = false;
                    rect.localRotation = restRotation;
                    rect.localScale = restScale;
                    interaction.interactable = true;
                    if (pointerInside && Usable) StartHoverPunch();
                }
                return;
            }
            if (acquired) return;
            float target=press?.975f:hover&&rust==null?1.035f:1;
            if (hoverPunchRemaining > 0f)
            {
                hoverPunchRemaining = Mathf.Max(0f, hoverPunchRemaining - dt);
                float progress = 1f - hoverPunchRemaining / HoverPunchSeconds;
                float pulse = Mathf.Sin(Mathf.PI * Mathf.Sqrt(progress));
                rect.localScale = restScale * (target + HoverPunchScale * pulse);
                return;
            }
            transform.localScale=Vector3.Lerp(transform.localScale,restScale*target,1-Mathf.Exp(-24*dt));
            if(settling&&!hover&&!press&&!acquired&&(transform.localScale-restScale).sqrMagnitude<.0000001f){transform.localScale=restScale;settling=false;}
        }
        void TickConfirm()
        {
            float elapsed = Time.unscaledTime - confirmStart;
            if (elapsed < PunchSeconds)
            {
                float t = elapsed / PunchSeconds;
                rect.localScale = restScale * (1f + .16f * Mathf.Sin(t * Mathf.PI) + .07f * t);
                return;
            }
            float flip = Mathf.Clamp01((elapsed - PunchSeconds) / FlipSeconds);
            float eased = Mathf.SmoothStep(0f, 1f, flip);
            rect.localRotation = restRotation * Quaternion.Euler(0f, 180f * eased, 0f);
            rect.localScale = restScale * Mathf.Lerp(1.07f, 1f, eased);
            bool frontVisible = eased < .5f;
            SetFront(frontVisible);
            if (back.gameObject.activeSelf == frontVisible) back.gameObject.SetActive(!frontVisible);
            if (elapsed >= PunchSeconds + FlipSeconds + BackHoldSeconds) RestoreConfirmOrder();
        }
        void RestoreConfirmOrder()
        {
            if (!confirming) return;
            confirming = false;
            transform.SetSiblingIndex(confirmSibling);
        }
        void SetFront(bool visible){foreach(var group in front)if(group)group.alpha=visible?1:0;}
        void ResetMotion(){RestoreConfirmOrder();revealing=false;settling=false;hoverPunchRemaining=0f;hover=press=acquired=false;SetFront(true);if(back)back.gameObject.SetActive(false);if(interaction)interaction.interactable=true;if(hasRestPose){transform.localRotation=restRotation;transform.localScale=restScale;}}
        void OnDisable()
        {
            pointerInside = false;
            ResetMotion();
        }
        void Paint(){if(rust!=null){rust.PaintFocus(hover||acquired,acquired);return;}if(!tintSurfaces){if(label)label.text=acquired?"ACQUIRED":"CHOOSE UPGRADE";return;}if(!frame||!action)return;bool focus=hover||acquired;var edge=accent;edge.a=focus?.95f:.44f;frame.Paint(C(focus?"223C42":"1B2A38"),C("111B27"),edge,18,focus?2:1);var fill=press?Color.Lerp(accent,C("10212B"),.2f):focus?accent:C("243846");action.Paint(fill,fill,new Color(accent.r,accent.g,accent.b,.3f),9);if(label){label.text=acquired?"ACQUIRED":"CHOOSE UPGRADE";label.color=focus?C("10212B"):C("E1D8C2");}}
        static Color C(string s){ColorUtility.TryParseHtmlString("#"+s,out var c);return c;}
    }
}

