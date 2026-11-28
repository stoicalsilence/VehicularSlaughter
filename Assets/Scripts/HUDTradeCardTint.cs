using UnityEngine;
using UnityEngine.UI;

/// <summary>Static benefit/cost gradients, inset far enough to avoid the card chamfers.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/HUD Trade Card Tint")]
public sealed class HUDTradeCardTint : MaskableGraphic
{
    private HUDPanel frame;
    private Color benefit, cost;
    private float strength;
    public void Configure(HUDPanel source, Color positive, Color negative, float opacity)
    {
        frame=source;benefit=positive;cost=negative;strength=Mathf.Clamp01(opacity);
        raycastTarget=false;SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();if(frame==null||strength<=0f)return;
        Rect r=rectTransform.rect;
        float inset=Mathf.Max(4f,Mathf.Max(Mathf.Max(frame.topLeft,frame.topRight),Mathf.Max(frame.bottomLeft,frame.bottomRight))*.5f+3f);
        if(r.width<=2*inset||r.height<=2*inset)return;
        float l=r.xMin+inset, rr=r.xMax-inset, b=r.yMin+inset, t=r.yMax-inset,m=r.center.y;
        // Fade at both ends, keeping the center divider and outer border clean.
        Band(vh,l,rr,m,t,benefit);
        Band(vh,l,rr,b,m,cost);
    }
    private void Band(VertexHelper vh,float l,float r,float b,float t,Color tint)
    {
        Color c=tint*color;c.a*=strength;Color zero=c;zero.a=0;
        float h=t-b;
        Strip(vh,l,r,b,b+h*.22f,zero,c);
        Strip(vh,l,r,b+h*.22f,b+h*.78f,c,c);
        Strip(vh,l,r,b+h*.78f,t,c,zero);
    }
    private static void Strip(VertexHelper vh,float l,float r,float b,float t,Color32 bottom,Color32 top)
    {
        int n=vh.currentVertCount;Vector2 uv=new Vector2(.5f,.5f);
        vh.AddVert(new Vector2(l,b),bottom,uv);vh.AddVert(new Vector2(l,t),top,uv);
        vh.AddVert(new Vector2(r,t),top,uv);vh.AddVert(new Vector2(r,b),bottom,uv);
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
    }
}
