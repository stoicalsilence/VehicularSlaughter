using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>Presentation only. PlayerInventory remains the authority for item acquisition and pause.</summary>
[DisallowMultipleComponent]
public sealed class HUDItemDiscovery : MonoBehaviour
{
    [Header("Generated references")]
    public HUDPanel frame, rarityFrame;
    public RectTransform card, previewMount, burstSource;
    public RawImage previewImage;
    public TMP_Text heading, rarityLabel, stackLabel, banishCount, hint, scrollHint;
    public ScrollRect descriptionScroll;
    public TMP_Text description;
    public HUDItemHalo halo;
    public HUDRadialRays rays;
    public HUDRewardShine shine;
    public HUDParticleBurst revealParticles, rareParticles, epicParticles, legendaryParticles, confirmParticles, banishParticles;
    public HUDItemAction[] actions;
    [Header("Preview framing")]
    public bool automaticFraming = true;
    [Range(.5f,.95f)] public float previewFill = .82f;
    [Header("Reveal and selection")]
    [Range(0,.35f)] public float revealPunch = .13f;
    [Range(0,.4f)] public float confirmationHold = .20f;
    [Range(0,.3f)] public float confirmationPunch = .10f;
    [Header("Rarity colors")]
    public Color common = new Color32(166,179,187,255), rare = new Color32(105,185,255,255), epic = new Color32(196,138,255,255), legendary = new Color32(255,191,87,255);
    [Header("Optional hover audio")]
    public AudioClip hoverSound;
    [Range(0,1)] public float hoverVolume = .15f;
    [Min(0)] public float hoverInterval = .075f;
    private float lastHover = -100f;
    private bool ready;
    private HUDItemAction hovered, focused, highlighted;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>(16);
    private PointerEventData pointerData;
    private EventSystem pointerSystem;
    private Vector2 lastPointer;
    private Sequence fx;
    private Color rarityColor;
    private ItemRarity tier;
    private Camera fittedCamera;
    private Vector3 originalPosition, spinCenter;
    private float originalSize, originalNear, originalFar;
    public Transform Motion => card != null ? card : transform;
    public bool Ready => ready;

    public void Prepare(ItemDefinition item,int stacks,int remaining,bool showPreview)
    {
        ResetEffects(); tier=item.rarity; rarityColor=GetColor(tier);
        if(heading!=null){heading.text="ITEM DISCOVERED";heading.color=new Color32(218,231,237,255);}
        if(rarityLabel!=null){rarityLabel.text=tier.ToString().ToUpperInvariant();rarityLabel.color=rarityColor;}
        if(rarityFrame!=null){rarityFrame.borderColor=rarityColor;rarityFrame.fillColor=Color.Lerp(new Color(.10f,.15f,.19f,.96f),rarityColor,.10f);rarityFrame.Refresh();}
        if(stackLabel!=null) {stackLabel.text=stacks==0?"NEW ITEM":$"STACKS   {stacks} -> <color=#75EFBE>{(long)stacks+1}</color>";stackLabel.color=stacks==0?new Color32(117,239,190,255):new Color32(180,194,204,255);}
        if(banishCount!=null) banishCount.text=remaining.ToString();
        if(frame!=null){frame.fillColor=new Color(.085f,.13f,.17f,.96f);frame.borderColor=Color.Lerp(new Color(.48f,.57f,.62f),rarityColor,.6f);frame.accentColor=rarityColor;frame.Refresh();}
        if(halo!=null) halo.SetTint(rarityColor,tier==ItemRarity.Legendary?.45f:tier==ItemRarity.Epic?.32f:tier==ItemRarity.Rare?.23f:.13f);
        if(previewMount!=null) previewMount.localScale=Vector3.one*(1f-Mathf.Clamp(revealPunch,0,.35f)*1.6f);
        if(previewImage!=null) previewImage.gameObject.SetActive(showPreview);
        if(previewImage!=null){previewImage.color=Color.white;var fitter=previewImage.GetComponent<AspectRatioFitter>();if(fitter!=null && previewImage.texture!=null && previewImage.texture.height>0) fitter.aspectRatio=(float)previewImage.texture.width/previewImage.texture.height;}
        if(descriptionScroll!=null){Canvas.ForceUpdateCanvases();descriptionScroll.StopMovement();descriptionScroll.verticalNormalizedPosition=1;
            if(scrollHint!=null)scrollHint.gameObject.SetActive(descriptionScroll.content!=null && descriptionScroll.viewport!=null && descriptionScroll.content.rect.height>descriptionScroll.viewport.rect.height+2);
        }
        SetReady(false);
    }
    public void Reveal(float duration)
    {
        if(previewMount==null) return;
        fx?.Kill(); float d=Mathf.Max(.01f,duration);
        fx=DOTween.Sequence().SetUpdate(true);
        fx.Append(previewMount.DOScale(Vector3.one*(1+Mathf.Clamp(revealPunch,0,.35f)),d*.58f).SetEase(Ease.OutCubic));
        fx.Append(previewMount.DOScale(Vector3.one,d*.42f).SetEase(Ease.OutQuad));
    }
    public void RevealComplete()
    {
        if(previewMount!=null) previewMount.localScale=Vector3.one;
        Canvas.ForceUpdateCanvases();
        if(tier==ItemRarity.Epic||tier==ItemRarity.Legendary)
        {
            bool top=tier==ItemRarity.Legendary;
            if(rays!=null) rays.Play(rarityColor,top?.32f:.16f,top?1.05f:.75f,true);
            if(shine!=null) shine.Play(rarityColor,top?.28f:.10f,top?.85f:.58f);
        }
        var emitter=tier==ItemRarity.Legendary?legendaryParticles:tier==ItemRarity.Epic?epicParticles:tier==ItemRarity.Rare?rareParticles:revealParticles;
        Emit(emitter,rarityColor);
        SetReady(true);
        RefreshPointer(false);
    }
    public void SetReady(bool value)
    {
        ready=value; hovered=focused=highlighted=null; Paint();
        if(!value) ClearMenuFocus();
    }
    private void ClearMenuFocus()
    {
        if(EventSystem.current==null) return;
        var selected=EventSystem.current.currentSelectedGameObject;
        if(selected!=null && selected.transform.IsChildOf(transform)) EventSystem.current.SetSelectedGameObject(null);
    }
    public void Hover(HUDItemAction action,bool enter)
    {
        if(!ready) return;
        if(enter){hovered=action;if(focused!=action){focused=null;ClearMenuFocus();}}
        else if(hovered==action) hovered=null;
        RefreshHighlight();
    }
    public void Focus(HUDItemAction action,bool enter)
    {
        if(!ready) return;
        if(enter){focused=action;hovered=null;}
        else if(focused==action) focused=null;
        RefreshHighlight();
    }
    private void RefreshHighlight()
    {
        var next=hovered!=null?hovered:focused;
        if(next!=highlighted && next!=null && next.Available && hoverSound!=null && Time.unscaledTime-lastHover>=Mathf.Max(0,hoverInterval))
        {lastHover=Time.unscaledTime; AudioManager.Play2D(hoverSound,hoverVolume);}
        highlighted=next;Paint();
    }
    private void RefreshPointer(bool sound)
    {
        var es=EventSystem.current;if(es==null)return;
        if(pointerData==null||pointerSystem!=es){pointerSystem=es;pointerData=new PointerEventData(es);}
        lastPointer=Input.mousePosition;pointerData.position=lastPointer;pointerHits.Clear();es.RaycastAll(pointerData,pointerHits);
        var hit=pointerHits.Count>0 && pointerHits[0].gameObject!=null?pointerHits[0].gameObject.GetComponentInParent<HUDItemAction>():null;
        hovered=hit!=null&&hit.owner==this?hit:null;pointerHits.Clear();
        if(sound)RefreshHighlight();else{highlighted=hovered;Paint();}
    }
    private void LateUpdate()
    {
        if(!ready||EventSystem.current==null)return;
        Vector2 mouse=Input.mousePosition;
        if(mouse!=lastPointer){lastPointer=mouse;if(focused!=null){focused=null;ClearMenuFocus();RefreshPointer(true);}}
        if(EventSystem.current.currentSelectedGameObject!=null)return;
        bool reverse=Input.GetKeyDown(KeyCode.LeftArrow)||Input.GetKeyDown(KeyCode.UpArrow);
        if(!reverse&&!Input.GetKeyDown(KeyCode.RightArrow)&&!Input.GetKeyDown(KeyCode.DownArrow))return;
        if(actions==null)return;
        for(int i=0;i<actions.Length;i++)
        {var a=actions[reverse?actions.Length-1-i:i];if(a!=null&&a.Available){EventSystem.current.SetSelectedGameObject(a.gameObject);break;}}
    }
    private void Paint()
    {
        if(actions!=null) foreach(var a in actions) if(a!=null) a.Paint(ready && highlighted==a && a.Available);
        if(hint!=null) hint.text=highlighted==null?"":highlighted.decision==HUDItemDecision.Banish?(highlighted.Available?"Remove this item from future drops for this run.":"No banishes remaining."):highlighted.decision==HUDItemDecision.Discard?"Leave this item behind without banishing it.":"";
    }
    public float Confirm(HUDItemDecision decision, int actualStacks, int remainingBanishes)
    {
        SetReady(false);fx?.Kill();ClearBursts();if(rays!=null)rays.Clear();if(shine!=null)shine.Clear();
        bool collect=decision==HUDItemDecision.Collect; bool banish=decision==HUDItemDecision.Banish;
        if(heading!=null){heading.text=collect?"ITEM COLLECTED":banish?"ITEM BANISHED":"ITEM DISCARDED";heading.color=collect?(Color)new Color32(117,239,190,255):banish?(Color)new Color32(232,114,114,255):common;}
        if(banishCount!=null)banishCount.text=remainingBanishes.ToString();
        if(collect&&stackLabel!=null){stackLabel.text=$"STACKS   {actualStacks}";stackLabel.color=new Color32(117,239,190,255);}
        if(hint!=null)hint.text=string.Empty;
        if(actions!=null)foreach(var a in actions)if(a!=null)a.Paint(false,a.decision==decision);
        Color c=collect?rarityColor:banish?(Color)new Color32(232,114,114,255):common;
        if(collect||banish)Emit(collect?confirmParticles:banishParticles,c);
        if(halo!=null)halo.SetTint(c,collect?.40f:.12f);
        float d=Mathf.Max(0,confirmationHold);
        if(previewMount!=null && d>0)
        {
            previewMount.localScale=Vector3.one;fx=DOTween.Sequence().SetUpdate(true);
            if(collect){fx.Append(previewMount.DOScale(1+confirmationPunch,d*.4f).SetEase(Ease.OutCubic));fx.Append(previewMount.DOScale(.86f,d*.6f).SetEase(Ease.InQuad));}
            else {fx.Append(previewMount.DOScale(.70f,d).SetEase(Ease.InQuad));if(previewImage!=null)fx.Join(previewImage.DOColor(banish?new Color(.85f,.55f,.55f,.18f):new Color(1,1,1,.18f),d));}
        }
        return d;
    }
    private void Emit(HUDParticleBurst emitter,Color tint)
    {if(emitter==null||burstSource==null)return;emitter.color=tint;emitter.Emit(burstSource,true);}
    private Color GetColor(ItemRarity rarity) => rarity==ItemRarity.Legendary?legendary:rarity==ItemRarity.Epic?epic:rarity==ItemRarity.Rare?rare:common;
    private void ClearBursts(){if(revealParticles!=null)revealParticles.Clear();if(rareParticles!=null)rareParticles.Clear();if(epicParticles!=null)epicParticles.Clear();if(legendaryParticles!=null)legendaryParticles.Clear();if(confirmParticles!=null)confirmParticles.Clear();if(banishParticles!=null)banishParticles.Clear();}
    private void ResetEffects(){fx?.Kill();fx=null;ClearBursts();if(rays!=null)rays.Clear();if(shine!=null)shine.Clear();if(previewMount!=null)previewMount.localScale=Vector3.one;if(previewImage!=null)previewImage.color=Color.white;}
    private void OnDisable(){ready=false;hovered=focused=highlighted=null;ResetEffects();RestorePreview();}

    // Fit a sphere enclosing the world-space renderer bounds. No prefab scale changes.
    public void FitPreview(GameObject model, Camera camera)
    {
        RestorePreview();
        if(!automaticFraming || model==null || camera==null || model.transform.IsChildOf(camera.transform) || camera.transform.IsChildOf(model.transform))return;
        var renderers=model.GetComponentsInChildren<Renderer>(false); Bounds bounds=default;bool found=false;
        foreach(var r in renderers)
        {
            if(!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)continue;
            Bounds b=r.bounds;if(!Finite(b.center)||!Finite(b.extents))continue;
            if(!found){bounds=b;found=true;}else bounds.Encapsulate(b);
        }
        if(!found)return;
        float radius=bounds.extents.magnitude;float aspect=camera.aspect;
        if(!Finite(radius)||radius<.00001f||!Finite(aspect)||aspect<=0)return;
        float fill=Mathf.Clamp(previewFill,.5f,.95f);
        float distance;
        if(camera.orthographic)distance=radius*3f;
        else
        {
            float v=Mathf.Clamp(camera.fieldOfView,1,179)*Mathf.Deg2Rad*.5f;
            float h=Mathf.Atan(Mathf.Tan(v)*aspect);
            distance=radius/Mathf.Sin(Mathf.Atan(Mathf.Tan(Mathf.Min(v,h))*fill));
        }
        if(!Finite(distance)||distance<=0||!Finite(distance+radius*2f))return;
        fittedCamera=camera;originalPosition=camera.transform.position;originalSize=camera.orthographicSize;originalNear=camera.nearClipPlane;originalFar=camera.farClipPlane;spinCenter=bounds.center;
        camera.transform.position=spinCenter-camera.transform.forward*distance;
        if(camera.orthographic)camera.orthographicSize=radius/(Mathf.Min(1,aspect)*fill);
        camera.nearClipPlane=Mathf.Max(.000001f,(distance-radius)*.5f);
        camera.farClipPlane=distance+radius*2f;
    }
    public void RotatePreview(Transform model,float angle)
    {if(model==null)return;if(fittedCamera!=null)model.RotateAround(spinCenter,Vector3.up,angle);else model.Rotate(Vector3.up,angle,Space.World);}
    public void RestorePreview()
    {if(fittedCamera!=null){fittedCamera.transform.position=originalPosition;fittedCamera.orthographicSize=originalSize;fittedCamera.nearClipPlane=originalNear;fittedCamera.farClipPlane=originalFar;}fittedCamera=null;}
    private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
    private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
}
