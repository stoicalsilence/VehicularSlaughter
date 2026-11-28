using UnityEngine;
using UnityEngine.UI;

/// <summary>One diagonal highlight sweep clipped to a HUDPanel's chamfered face.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/HUD Reward Shine")]
public sealed class HUDRewardShine : MaskableGraphic
{
    [SerializeField] private HUDPanel panel;
    [SerializeField, Range(.04f, .4f)] private float bandWidth = .20f;
    [SerializeField, Min(0f)] private float inset = 3f;
    private readonly Vector2[] polygon = new Vector2[8];
    private readonly Vector2[] workA = new Vector2[16], workB = new Vector2[16];
    private float elapsed, duration, opacity;
    private Color tint;
    protected override void Awake() { base.Awake(); raycastTarget = false; if (panel == null) panel = GetComponentInParent<HUDPanel>(); }
    public void Play(Color accent, float strength, float seconds)
    {
        if (!isActiveAndEnabled) return;
        tint = Color.Lerp(accent, Color.white, .65f); opacity = Mathf.Clamp01(strength);
        elapsed = 0f; duration = Mathf.Max(.01f, seconds); SetVerticesDirty();
    }
    public void Clear() { duration = elapsed = 0f; SetVerticesDirty(); }
    protected override void OnDisable() { duration = elapsed = 0f; base.OnDisable(); }
    private void LateUpdate()
    {
        if (duration <= 0f || Time.unscaledDeltaTime <= 0f) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= duration) duration = 0f;
        SetVerticesDirty();
    }
    private static float U(Vector2 p, Rect r) => (p.x-r.xMin)/r.width + .42f*(p.y-r.yMin)/r.height;
    // Sutherland-Hodgman clipping against a diagonal half-plane.
    private static int Clip(Vector2[] src, int n, Vector2[] dst, Rect r, float edge, bool greater)
    {
        if (n == 0) return 0;
        int count = 0; Vector2 a = src[n-1]; float da = U(a,r)-edge;
        for(int i=0;i<n;i++)
        {
            Vector2 b=src[i]; float db=U(b,r)-edge;
            bool ia=greater?da>=0f:da<=0f, ib=greater?db>=0f:db<=0f;
            if(ia!=ib) dst[count++]=Vector2.LerpUnclamped(a,b,da/(da-db));
            if(ib) dst[count++]=b;
            a=b; da=db;
        }
        return count;
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if(duration<=0f || opacity<=0f) return;
        Rect r=rectTransform.rect; float pad=Mathf.Max(0f,inset);
        r=Rect.MinMaxRect(r.xMin+pad,r.yMin+pad,r.xMax-pad,r.yMax-pad);
        if(r.width<=0f || r.height<=0f) return;
        float cap=Mathf.Min(r.width,r.height)*.5f;
        float adjust=(2f-Mathf.Sqrt(2f))*pad;
        float tl=panel!=null?Mathf.Clamp(panel.topLeft-adjust,0,cap):0;
        float tr=panel!=null?Mathf.Clamp(panel.topRight-adjust,0,cap):0;
        float bl=panel!=null?Mathf.Clamp(panel.bottomLeft-adjust,0,cap):0;
        float br=panel!=null?Mathf.Clamp(panel.bottomRight-adjust,0,cap):0;
        polygon[0]=new Vector2(r.xMin+bl,r.yMin); polygon[1]=new Vector2(r.xMax-br,r.yMin);
        polygon[2]=new Vector2(r.xMax,r.yMin+br); polygon[3]=new Vector2(r.xMax,r.yMax-tr);
        polygon[4]=new Vector2(r.xMax-tr,r.yMax); polygon[5]=new Vector2(r.xMin+tl,r.yMax);
        polygon[6]=new Vector2(r.xMin,r.yMax-tl); polygon[7]=new Vector2(r.xMin,r.yMin+bl);
        float w=Mathf.Max(.01f,bandWidth), t=Mathf.Clamp01(elapsed/duration);
        float mid=Mathf.Lerp(-w,1.42f+w,t);
        // Four adjacent strips give a smooth triangular highlight profile.
        for(int s=0;s<4;s++)
        {
            float lo=mid-w+s*w*.5f, hi=lo+w*.5f;
            int n=Clip(polygon,8,workA,r,lo,true); n=Clip(workA,n,workB,r,hi,false);
            if(n<3) continue;
            int start=vh.currentVertCount;
            for(int i=0;i<n;i++)
            {
                Vector2 p=workB[i]; Color c=tint*color;
                c.a*=opacity*Mathf.Clamp01(1f-Mathf.Abs(U(p,r)-mid)/w);
                vh.AddVert(p,c,Vector2.zero);
            }
            for(int i=1;i<n-1;i++) vh.AddTriangle(start,start+i,start+i+1);
        }
    }
}
