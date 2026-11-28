using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Camera-relative tape and bounded generic world marker display.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
[AddComponentMenu("UI/Compass Meter")]
public sealed class CompassMeter : MonoBehaviour
{
    [Header("Camera source")]
    public PlayerVehicleInteractor vehicleInteractor;
    public Camera fallbackCamera;
    [Tooltip("Optional distance/bearing origin. Defaults to the active camera position.")]
    public Transform trackingOrigin;
    [Tooltip("World yaw that represents North. Zero = world +Z.")]
    public float northYaw;
    [Header("References")]
    public HUDCompassTape tape;
    public RectTransform labelsRoot;
    public RectTransform markersRoot;
    public TMP_FontAsset font;
    [Header("Tape")]
    [Range(60f,350f)] public float visibleAngle=250f;
    [Range(0.02f,0.4f)] public float edgeFade=0.18f;
    public Color labelColor=new Color32(244,246,247,255);
    public Color headingColor=new Color32(112,231,237,255);
    [Min(8f)] public float labelSize=18f;
    [Range(0f,22f)] public float headingHighlightAngle=12f;
    [Header("Markers")]
    [Range(1,24)] public int maxVisibleMarkers=12;
    [Min(0.05f)] public float scanInterval=0.1f;
    [Min(12f)] public float iconSize=24f;
    [Min(0f)] public float minimumIconSpacing=34f;
    public bool showDistance=false;
    [Min(8f)] public float distanceFontSize=12f;
    [Header("Marker feedback")]
    [Range(0f,0.4f)] public float appearancePunch=0.16f;
    [Min(0f)] public float appearanceDuration=0.22f;
    private static readonly string[] Names={"N","NE","E","SE","S","SW","W","NW"};
    private const int Capacity=24;
    private readonly List<Candidate> candidates=new List<Candidate>(128);
    private readonly HUDCompassMarker[] desired=new HUDCompassMarker[Capacity];
    private readonly int[] orderedSlots=new int[Capacity];
    private readonly float[] drawnPositions=new float[Capacity];
    private readonly Label[] labels=new Label[8];
    private Slot[] slots;
    private int desiredCount;
    private float nextScan, nextCameraRetry, lastYaw, lastScanYaw;
    private Camera lastCamera;
    private bool hasYaw, wasDriving, initialized;
    private float lastWidth, lastSpan;
    private struct Candidate : IComparable<Candidate>
    {
        public HUDCompassMarker marker;
        public float distanceSq, x;
        public int priority, id;
        public int CompareTo(Candidate other)
        {
            int cmp=other.priority.CompareTo(priority);
            if(cmp!=0) return cmp;
            cmp=distanceSq.CompareTo(other.distanceSq);
            return cmp!=0?cmp:id.CompareTo(other.id);
        }
    }
    private sealed class Label
    {
        public RectTransform rect;
        public TextMeshProUGUI text;
        public CanvasGroup group;
    }
    private sealed class Slot
    {
        public RectTransform rect, iconRect;
        public Image image;
        public TextMeshProUGUI distanceText;
        public CanvasGroup group;
        public HUDCompassMarker marker;
        public float age;
        public int distance=-1;
        public bool wasVisible;
    }
    private void Awake()
    {
        if(tape==null || labelsRoot==null || markersRoot==null)
        {
            Debug.LogError("CompassMeter: assign Tape, Labels Root and Markers Root (or run the installer).",this);
            enabled=false; return;
        }
        if(fallbackCamera==null && vehicleInteractor==null) fallbackCamera=Camera.main;
        for(int i=0;i<8;i++)
        {
            var t=MakeText(labelsRoot,Names[i],labelSize,new Vector2(70f,30f));
            t.text=Names[i]; t.color=labelColor;
            labels[i]=new Label{rect=t.rectTransform,text=t,group=t.gameObject.AddComponent<CanvasGroup>()};
            labels[i].group.alpha=0f;
        }
        slots=new Slot[Mathf.Clamp(maxVisibleMarkers,1,Capacity)];
        for(int i=0;i<slots.Length;i++)
        {
            var root=MakeRect(markersRoot,"Marker "+(i+1),new Vector2(60f,48f));
            var group=root.gameObject.AddComponent<CanvasGroup>();
            group.interactable=false; group.blocksRaycasts=false; group.alpha=0f;
            var iconRect=MakeRect(root,"Icon",Vector2.one*iconSize);
            iconRect.gameObject.AddComponent<CanvasRenderer>();
            var image=iconRect.gameObject.AddComponent<Image>();
            image.raycastTarget=false; image.preserveAspect=true;
            var distance=MakeText(root,"Distance",distanceFontSize,new Vector2(84f,18f));
            distance.rectTransform.anchoredPosition=new Vector2(0,-iconSize*0.5f-10f);
            distance.gameObject.SetActive(showDistance);
            slots[i]=new Slot{rect=root,iconRect=iconRect,image=image,distanceText=distance,group=group};
            root.gameObject.SetActive(false);
        }
        initialized=true;
    }
    private void OnEnable() { nextScan=0; nextCameraRetry=0; hasYaw=false; lastCamera=null; }
    private void OnDisable()
    {
        if(!initialized) return;
        HideAll(); if(tape!=null) tape.enabled=false; lastCamera=null; hasYaw=false;
    }
    private void HideAll()
    {
        foreach(var label in labels) if(label!=null) label.group.alpha=0;
        foreach(var slot in slots) Release(slot);
        desiredCount=0; nextScan=0; Array.Clear(desired,0,desired.Length); candidates.Clear();
    }
    private Camera ResolveCamera(float now)
    {
        Camera view=vehicleInteractor!=null?vehicleInteractor.ActiveViewCamera:null;
        if(view!=null && view.isActiveAndEnabled) return view;
        if(fallbackCamera!=null && fallbackCamera.isActiveAndEnabled) return fallbackCamera;
        // Fallback only, rate-limited; assigned interactor never needs camera tag searches.
        if(vehicleInteractor==null && now>=nextCameraRetry)
        {
            nextCameraRetry=now+1f; fallbackCamera=Camera.main;
        }
        return fallbackCamera!=null && fallbackCamera.isActiveAndEnabled?fallbackCamera:null;
    }
    private void LateUpdate()
    {
        if(!initialized) return;
        float now=Time.unscaledTime;
        Camera view=ResolveCamera(now);
        if(view==null)
        {
            if(lastCamera!=null || hasYaw) HideAll();
            tape.enabled=false; lastCamera=null; hasYaw=false; return;
        }
        tape.enabled=true;
        Vector3 f=view.transform.forward;
        if(f.x*f.x+f.z*f.z>0.00001f) {lastYaw=Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg; hasYaw=true;}
        else if(!hasYaw) {lastYaw=view.transform.eulerAngles.y; hasYaw=true;}
        float yaw=lastYaw, span=Mathf.Clamp(visibleAngle,60f,350f), half=span*0.5f;
        float width=tape.rectTransform.rect.width;
        if(width<40f) {HideAll(); return;}
        float halfWidth=width*0.5f;
        tape.SetHeading(Mathf.Repeat(yaw-northYaw,360f),span,edgeFade);
        DrawLabels(yaw,half,halfWidth);
        Vector3 origin=trackingOrigin!=null?trackingOrigin.position:view.transform.position;
        bool driving=vehicleInteractor!=null && vehicleInteractor.IsInVehicle;
        bool changed=view!=lastCamera || driving!=wasDriving || Mathf.Abs(width-lastWidth)>0.1f || !Mathf.Approximately(span,lastSpan);
        // Rescan at 10 Hz normally; rapid camera turns may request an earlier refresh (max ~30 Hz).
        float interval=Mathf.Max(0.05f,scanInterval);
        bool fastTurn=Mathf.Abs(Mathf.DeltaAngle(lastScanYaw,yaw))>8f && now>=nextScan-interval+0.033f;
        if(changed || now>=nextScan || fastTurn)
        {
            Scan(origin,yaw,half,halfWidth,driving);
            nextScan=now+interval; lastScanYaw=yaw;
        }
        lastCamera=view; wasDriving=driving; lastWidth=width; lastSpan=span;
        DrawMarkers(origin,yaw,half,halfWidth,driving,Time.unscaledDeltaTime);
    }
    private void DrawLabels(float yaw,float half,float halfWidth)
    {
        for(int i=0;i<8;i++)
        {
            Label l=labels[i]; float d=Mathf.DeltaAngle(yaw,northYaw+i*45f), n=d/half;
            float a=HUDCompassTape.EdgeOpacity(n,edgeFade);
            l.group.alpha=a;
            if(a<=0f) continue;
            l.rect.anchoredPosition=new Vector2(n*halfWidth,0);
            bool highlight=Mathf.Abs(d)<=headingHighlightAngle;
            Color c=highlight?headingColor:labelColor;
            if(l.text.color!=c) l.text.color=c;
            float size=labelSize*(highlight?1.18f:1f);
            if(!Mathf.Approximately(l.text.fontSize,size)) l.text.fontSize=size;
        }
    }
    private static bool TryBearing(HUDCompassMarker m,Vector3 origin,float yaw,bool driving,out float delta,out float sq)
    {
        delta=0f; sq=0f;
        if(m==null || !m.isActiveAndEnabled || !m.visibleOnCompass || (driving && m.hideWhileDriving)) return false;
        Vector3 v=m.Position-origin; sq=v.x*v.x+v.z*v.z;
        float min=Mathf.Max(0.01f,m.minimumDistance), max=m.maximumDistance;
        if(float.IsNaN(sq)||float.IsInfinity(sq)||sq<min*min||(max>0f&&sq>max*max)) return false;
        delta=Mathf.DeltaAngle(yaw,Mathf.Atan2(v.x,v.z)*Mathf.Rad2Deg);
        return true;
    }
    private void Scan(Vector3 origin,float yaw,float half,float halfWidth,bool driving)
    {
        candidates.Clear(); desiredCount=0;
        float usable=Mathf.Max(1f,halfWidth-iconSize*0.55f);
        foreach(var m in HUDCompassMarker.Active)
        {
            if(!TryBearing(m,origin,yaw,driving,out float d,out float sq)||Mathf.Abs(d)>=half) continue;
            float x=d/half*halfWidth;
            if(Mathf.Abs(x)>usable) continue;
            candidates.Add(new Candidate{marker=m,distanceSq=sq,x=x,priority=m.priority,id=m.GetInstanceID()});
        }
        candidates.Sort();
        int limit=Mathf.Clamp(maxVisibleMarkers,1,slots.Length);
        float spacing=Mathf.Max(minimumIconSpacing,iconSize*1.2f);
        if(showDistance) spacing=Mathf.Max(spacing,58f);
        for(int i=0;i<candidates.Count && desiredCount<limit;i++)
        {
            Candidate c=candidates[i]; bool overlaps=false;
            for(int j=0;j<desiredCount;j++) if(Mathf.Abs(c.x-drawnPositions[j])<spacing){overlaps=true;break;}
            if(overlaps) continue;
            desired[desiredCount]=c.marker; drawnPositions[desiredCount]=c.x; desiredCount++;
        }
        // Retain a slot for a marker already shown, so reordering by priority cannot restart its punch.
        foreach(var slot in slots)
        {
            bool keep=false;
            for(int j=0;j<desiredCount;j++) if(slot.marker!=null && slot.marker==desired[j]){keep=true;break;}
            if(!keep) Release(slot);
        }
        for(int i=0;i<desiredCount;i++)
        {
            int index=-1;
            for(int j=0;j<slots.Length;j++) if(slots[j].marker==desired[i]){index=j;break;}
            if(index<0)
            {
                for(int j=0;j<slots.Length;j++) if(slots[j].marker==null){index=j;break;}
                if(index<0) continue; // unreachable with the bounded desired list
                Slot s=slots[index]; s.marker=desired[i]; s.age=0; s.distance=-1; s.wasVisible=false;
                s.group.alpha=0f; s.rect.gameObject.SetActive(true);
            }
            orderedSlots[i]=index;
            Slot current=slots[index]; HUDCompassMarker marker=current.marker;
            if(current.image.sprite!=marker.icon) current.image.sprite=marker.icon;
            if(current.image.color!=marker.tint) current.image.color=marker.tint;
            bool fallback=marker.icon==null;
            current.iconRect.sizeDelta=Vector2.one*iconSize*(fallback?0.65f:1f);
            current.iconRect.localRotation=Quaternion.Euler(0,0,fallback?45f:0f);
            if(current.distanceText.gameObject.activeSelf!=showDistance) current.distanceText.gameObject.SetActive(showDistance);
            if(showDistance)
            {
                Vector3 v=marker.Position-origin;
                int meters=Mathf.RoundToInt(Mathf.Sqrt(v.x*v.x+v.z*v.z));
                if(meters!=current.distance){current.distance=meters;current.distanceText.SetText("{0:0}m",(float)meters);}
                current.distanceText.color=marker.tint;
                current.distanceText.fontSize=distanceFontSize;
                current.distanceText.rectTransform.anchoredPosition=new Vector2(0,-iconSize*0.5f-10f);
            }
        }
        Array.Clear(desired,desiredCount,Capacity-desiredCount);
    }
    private void DrawMarkers(Vector3 origin,float yaw,float half,float halfWidth,bool driving,float dt)
    {
        int drawn=0; float spacing=Mathf.Max(minimumIconSpacing,iconSize*1.2f);
        if(showDistance) spacing=Mathf.Max(spacing,58f);
        float usable=Mathf.Max(1f,halfWidth-iconSize*0.55f);
        for(int i=0;i<desiredCount;i++)
        {
            Slot s=slots[orderedSlots[i]];
            if(!TryBearing(s.marker,origin,yaw,driving,out float d,out _) || Mathf.Abs(d)>=half)
            { s.group.alpha=0f; s.wasVisible=false; continue; }
            float x=d/half*halfWidth;
            bool overlap=Mathf.Abs(x)>=usable;
            for(int j=0;j<drawn&&!overlap;j++) if(Mathf.Abs(x-drawnPositions[j])<spacing) overlap=true;
            if(overlap){s.group.alpha=0f; continue;}
            drawnPositions[drawn++]=x;
            if(!s.wasVisible){s.age=0f; s.wasVisible=true;}
            s.age+=dt;
            float t=appearanceDuration>0?Mathf.Clamp01(s.age/appearanceDuration):1f;
            float scale=1f+Mathf.Sin(t*Mathf.PI)*appearancePunch;
            s.rect.localScale=Vector3.one*scale;
            s.rect.anchoredPosition=new Vector2(x,0f);
            s.group.alpha=HUDCompassTape.EdgeOpacity(x/usable,edgeFade)*Mathf.Lerp(0.55f,1f,t);
        }
    }
    private static void Release(Slot s)
    {
        s.marker=null; s.wasVisible=false;
        if(s.rect.gameObject.activeSelf) s.rect.gameObject.SetActive(false);
    }
    private static RectTransform MakeRect(RectTransform parent,string name,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform)); go.layer=parent.gameObject.layer;
        var rt=(RectTransform)go.transform; rt.SetParent(parent,false); rt.anchorMin=rt.anchorMax=rt.pivot=new Vector2(.5f,.5f);
        rt.anchoredPosition=Vector2.zero; rt.sizeDelta=size; return rt;
    }
    private TextMeshProUGUI MakeText(RectTransform parent,string name,float size,Vector2 rectSize)
    {
        var rt=MakeRect(parent,name,rectSize);
        rt.gameObject.AddComponent<CanvasRenderer>();
        var t=rt.gameObject.AddComponent<TextMeshProUGUI>();
        if(font!=null) t.font=font;
        t.fontSize=size; t.alignment=TextAlignmentOptions.Center; t.raycastTarget=false;
        t.richText=false; t.enableWordWrapping=false; t.overflowMode=TextOverflowModes.Overflow;
        return t;
    }
}
