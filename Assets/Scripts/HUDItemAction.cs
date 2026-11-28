using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public enum HUDItemDecision { Collect, Banish, Discard }

/// <summary>Visual-only focus/hover adapter. Inventory still owns the button callbacks.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class HUDItemAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public HUDItemDiscovery owner;
    public HUDItemDecision decision;
    public HUDPanel frame;
    public Button button;
    public TMP_Text label, countLabel;
    public Color accent = Color.white;
    private void Awake() { if(button==null) button=GetComponent<Button>(); }
    public bool Available => button != null && button.IsActive() && button.IsInteractable();
    public void Paint(bool highlighted, bool confirmed = false)
    {
        if(frame==null) return;
        Color c=accent; if(!Available && !confirmed) c=Color.Lerp(c,new Color(.35f,.4f,.44f),.75f);
        if(confirmed) c=decision==HUDItemDecision.Collect?new Color(.46f,.94f,.74f):new Color(.91f,.42f,.42f);
        if(label!=null)label.color=c;if(countLabel!=null)countLabel.color=c;
        frame.fillColor=Color.Lerp(new Color(.10f,.15f,.19f,.99f),c,confirmed?.32f:highlighted?.23f:.08f);
        frame.borderColor=c; frame.accentColor=c; frame.borderThickness=highlighted||confirmed?2.2f:1.2f; frame.Refresh();
    }
    public void OnPointerEnter(PointerEventData e) { if(owner!=null) owner.Hover(this,true); }
    public void OnPointerExit(PointerEventData e) { if(owner!=null) owner.Hover(this,false); }
    public void OnSelect(BaseEventData e) { if(owner!=null) owner.Focus(this,true); }
    public void OnDeselect(BaseEventData e) { if(owner!=null) owner.Focus(this,false); }
}
