using TMPro;
using UnityEngine;

/// <summary>Event-driven level-earned toast. Aggregates actual earned levels, not visual XP wraps.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Level Up Notification")]
public sealed class HUDLevelUpNotification : MonoBehaviour
{
    [SerializeField] private PlayerLevelSystem levelSystem;
    [SerializeField] private CanvasGroup visualGroup;
    [SerializeField] private RectTransform visual;
    [SerializeField] private TMP_Text titleText, detailText;
    [SerializeField, Min(.1f)] private float holdSeconds = .9f;
    [SerializeField, Min(.01f)] private float fadeSeconds = .25f;
    [SerializeField, Min(0f)] private float riseDistance = 16f;
    [SerializeField, Range(0f,.3f)] private float punch = .10f;
    private PlayerLevelSystem subscribed;
    private int lastTotal, batch;
    private float age;
    private Vector2 restPosition;
    private Vector3 restScale;
    private bool captured, active;
    private void OnEnable()
    {
        if(visual!=null) { restPosition=visual.anchoredPosition; restScale=visual.localScale; captured=true; }
        if(visualGroup!=null) { visualGroup.interactable=false; visualGroup.blocksRaycasts=false; }
        Hide();
        if(levelSystem==null) return;
        subscribed=levelSystem; lastTotal=subscribed.TotalLevelsEarned; subscribed.XPChanged+=OnXPChanged;
    }
    private void OnDisable()
    {
        if(!ReferenceEquals(subscribed,null)) subscribed.XPChanged-=OnXPChanged;
        subscribed=null; Hide(); captured=false;
    }
    private void OnXPChanged()
    {
        if(subscribed==null) return;
        int total=subscribed.TotalLevelsEarned;
        if(total<lastTotal) { lastTotal=total; Hide(); return; }
        int gained=total-lastTotal; lastTotal=total;
        if(gained<=0 || Time.timeScale<=0f) return;
        batch=active?(int)System.Math.Min(int.MaxValue,(long)batch+gained):gained;
        if(titleText!=null) titleText.text="LEVEL UP  /  LEVEL "+((long)total+1);
        if(detailText!=null) detailText.text="+"+batch+(batch==1?" UPGRADE BANKED":" UPGRADES BANKED");
        age=0f; active=true; Draw();
    }
    private void Update()
    {
        if(!active) return;
        // A modal pause should not leave a stale gameplay toast behind the menu.
        if(Time.timeScale<=0f) { Hide(); return; }
        age+=Time.deltaTime;
        if(age>=Mathf.Max(.1f,holdSeconds)+Mathf.Max(.01f,fadeSeconds)) { Hide(); return; }
        Draw();
    }
    private void Draw()
    {
        float enter=Mathf.Clamp01(age/.16f), fade=Mathf.Clamp01((age-Mathf.Max(.1f,holdSeconds))/Mathf.Max(.01f,fadeSeconds));
        if(visualGroup!=null) visualGroup.alpha=Mathf.Min(1f,enter*2f)*(1f-fade);
        if(visual!=null && captured)
        {
            visual.anchoredPosition=restPosition+Vector2.up*(riseDistance*(1f-Mathf.Pow(1f-enter,3f)));
            visual.localScale=restScale*(1f-.08f*(1f-enter)+punch*Mathf.Sin(enter*Mathf.PI));
        }
    }
    private void Hide()
    {
        active=false; batch=0; age=0;
        if(visualGroup!=null) visualGroup.alpha=0f;
        if(visual!=null && captured) { visual.anchoredPosition=restPosition; visual.localScale=restScale; }
    }
}
