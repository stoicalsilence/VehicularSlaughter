using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Presentation only. The trade system owns all stat changes and button actions.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button), typeof(HUDPanel))]
public sealed class HUDStatTradeCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Button button;
    public HUDPanel frame;
    public CanvasGroup group;
    public TMP_Text benefitCategory, benefitName, benefitAmount, benefitComparison;
    public TMP_Text costCategory, costName, costAmount, costComparison;
    public HUDParticleBurst confirmationParticles;
    public Color accent = new Color32(180, 125, 232, 255);
    public Color baseFill = new Color32(33, 28, 46, 247);
    [Range(0f,1f)] public float hoverTint = .16f;
    private Action<HUDStatTradeCard, bool> focus;
    private Vector3 restScale;
    private bool captured, highlighted, confirmed;
    private Color confirmColor;
    private float flash;
    private bool enhanced;
    private float tintOpacity, numberPunch;
    private HUDTradeCardTint tradeTint;
    private Color benefitTint=Color.green,costTint=Color.red;
    private Vector3 benefitRest=Vector3.one,costRest=Vector3.one;
    private bool numberScalesCaptured;

    public void ConfigureEnhancements(bool visibility,float opacity,float punch)
    {
        if(frame==null)frame=GetComponent<HUDPanel>();
        enhanced=visibility;tintOpacity=Mathf.Clamp01(opacity);numberPunch=Mathf.Clamp(punch,0f,.15f);
        CaptureNumberScales();
        if(visibility && frame!=null && benefitAmount!=null && costAmount!=null && tradeTint==null)
        {
            var go=new GameObject("BenefitCostTint (Runtime)",typeof(RectTransform),typeof(CanvasRenderer));
            go.layer=gameObject.layer;var r=(RectTransform)go.transform;
            r.SetParent(transform,false);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;
            r.offsetMin=r.offsetMax=Vector2.zero;r.SetAsFirstSibling();
            tradeTint=go.AddComponent<HUDTradeCardTint>();
            tradeTint.raycastTarget=false;
        }
        UpdateTint();
    }
    private void CaptureNumberScales()
    {
        if(numberScalesCaptured)return;
        if(benefitAmount!=null)benefitRest=benefitAmount.rectTransform.localScale;
        if(costAmount!=null)costRest=costAmount.rectTransform.localScale;
        numberScalesCaptured=true;
    }
    private void UpdateTint()
    {
        if(tradeTint!=null)tradeTint.Configure(frame,benefitTint,costTint,enhanced?tintOpacity:0f);
    }
    private void PunchNumber(Sequence seq,TMP_Text label,Vector3 scale,float duration,float offset)
    {
        if(label==null||numberPunch<=0f||duration<=offset)return;
        float d=duration-offset;
        seq.Insert(offset,label.rectTransform.DOScale(scale*(1f+numberPunch),d*.4f).SetEase(Ease.OutCubic));
        seq.Insert(offset+d*.4f,label.rectTransform.DOScale(scale,d*.6f).SetEase(Ease.OutQuad));
    }

    public void SetFocusHandler(Action<HUDStatTradeCard, bool> callback) { focus = callback; }
    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (frame == null) frame = GetComponent<HUDPanel>();
        Capture();
    }
    private void Capture()
    {
        if (captured) return;
        restScale = transform.localScale; captured = true;
    }
    public void SetBenefit(string category, string name, string amount, string comparison, Color tint)
    {
        Set(benefitCategory, "BENEFIT  TO  " + category, tint);
        Set(benefitName, name, Color.white);
        Set(benefitAmount, amount, tint);
        if(benefitComparison!=null){benefitComparison.richText=true;if(enhanced)benefitComparison.fontSize=Mathf.Max(benefitComparison.fontSize,28f);}
        Set(benefitComparison, comparison, new Color32(205,216,224,255));
        benefitTint=tint;UpdateTint();
    }
    public void SetCost(string category, string name, string amount, string comparison, Color tint)
    {
        Set(costCategory, "COST  TO  " + category, tint);
        Set(costName, name, Color.white);
        Set(costAmount, amount, tint);
        if(costComparison!=null){costComparison.richText=true;if(enhanced)costComparison.fontSize=Mathf.Max(costComparison.fontSize,28f);}
        Set(costComparison, comparison, new Color32(205,216,224,255));
        costTint=tint;UpdateTint();
    }
    private static void Set(TMP_Text t, string value, Color c) { if(t!=null){ t.text=value; t.color=c; } }
    public void ResetVisual()
    {
        Capture(); transform.localScale = restScale; highlighted = confirmed = false; flash = 0f;
        CaptureNumberScales();
        if(benefitAmount!=null)benefitAmount.rectTransform.localScale=benefitRest;
        if(costAmount!=null)costAmount.rectTransform.localScale=costRest;
        if(group != null) group.alpha = 1f;
        if(confirmationParticles != null) confirmationParticles.Clear();
        Draw();
    }
    public void Highlight(bool value)
    {
        if (confirmed || highlighted == value) return;
        highlighted = value; Draw();
    }
    private void Draw()
    {
        if (frame == null) return;
        Color c = confirmed ? confirmColor : accent;
        frame.fillColor = Color.Lerp(baseFill,c,confirmed ? .22f : highlighted ? hoverTint : 0f);
        frame.borderColor = Color.Lerp(c,Color.white,flash);
        frame.accentColor = Color.Lerp(c,Color.white,Mathf.Max(flash,enhanced?.28f:0f));
        frame.borderThickness = confirmed || highlighted ? (enhanced?3f:2.4f) : (enhanced?1.8f:1.3f);
        frame.Refresh();
    }
    public void AddReveal(Sequence seq, float start, float duration)
    {
        Capture();
        if(duration <= 0f) return;
        transform.localScale = restScale * .92f;
        seq.Insert(start, transform.DOScale(restScale,duration).SetEase(Ease.OutBack));
        if(group != null) {group.alpha=0f;seq.Insert(start,group.DOFade(1f,duration).SetEase(Ease.OutQuad));}
    }
    public void AddConfirmation(Sequence seq, float hold, float punch, Color tint, bool emit)
    {
        Capture(); confirmed=true; highlighted=false; confirmColor=tint; flash=.75f; Draw();
        if(emit && confirmationParticles!=null)
        {
            confirmationParticles.color=tint;
            confirmationParticles.Emit((RectTransform)transform,true);
        }
        if(hold<=0f){flash=0f;Draw();return;}
        float d=Mathf.Min(.26f,hold), p=Mathf.Clamp(punch,0f,.15f);
        seq.Insert(0f,transform.DOScale(Vector3.Scale(restScale,new Vector3(1f+p*.25f,1f-p,1f)),d*.18f).SetEase(Ease.OutQuad));
        seq.Insert(d*.18f,transform.DOScale(restScale*(1f+p),d*.32f).SetEase(Ease.OutCubic));
        seq.Insert(d*.5f,transform.DOScale(restScale,d*.5f).SetEase(Ease.OutCubic));
        seq.Insert(0f,DOTween.To(()=>flash,v=>{flash=v;Draw();},0f,d).SetEase(Ease.OutQuad));
        PunchNumber(seq,benefitAmount,benefitRest,d,d*.12f);
        PunchNumber(seq,costAmount,costRest,d,d*.24f);
    }
    public void AddDim(Sequence seq, float duration, float opacity)
    {
        Highlight(false);
        if(group!=null && duration>0f) seq.Insert(0f,group.DOFade(opacity,Mathf.Min(.14f,duration)));
    }
    public void OnPointerEnter(PointerEventData e)
    {
        if(button==null || !button.IsInteractable()) return;
        if(EventSystem.current!=null && EventSystem.current.currentSelectedGameObject!=null &&
            EventSystem.current.currentSelectedGameObject!=gameObject) EventSystem.current.SetSelectedGameObject(null);
        focus?.Invoke(this,true);
    }
    public void OnPointerExit(PointerEventData e)
    {
        if(EventSystem.current!=null && EventSystem.current.currentSelectedGameObject==gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        focus?.Invoke(this,false);
    }
    public void OnSelect(BaseEventData e) {if(button!=null && button.IsInteractable()) focus?.Invoke(this,true);}
    public void OnDeselect(BaseEventData e) {focus?.Invoke(this,false);}
}
