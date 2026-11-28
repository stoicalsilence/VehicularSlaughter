using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free soft display halo and thin elliptical pedestal.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
public sealed class HUDItemHalo : MaskableGraphic
{
    [SerializeField, Range(0, 1)] private float intensity = .24f;
    private Color tint = Color.white;
    public void SetTint(Color value, float strength)
    { tint = value; intensity = Mathf.Clamp01(strength); SetVerticesDirty(); }
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect;
        if (r.width <= 0 || r.height <= 0) return;
        Color c = tint * color; c.a *= intensity;
        Color edge = c; edge.a = 0;
        Vector2 center = r.center + Vector2.up * 12;
        Vector2 rad = new Vector2(r.width * .49f, r.height * .48f);
        const int n = 48;
        vh.AddVert(center, c, Vector2.zero);
        Color mid = c; mid.a *= .38f;
        for (int i=0;i<n;i++)
        {
            float a = i * 2 * Mathf.PI / n;
            Vector2 v = Vector2.Scale(rad, new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            vh.AddVert(center+v*.5f,mid,Vector2.zero);
            vh.AddVert(center+v,edge,Vector2.zero);
        }
        for (int i=0;i<n;i++)
        {
            int j=1+i*2,k=1+((i+1)%n)*2;
            vh.AddTriangle(0,j,k); vh.AddTriangle(j,j+1,k+1); vh.AddTriangle(j,k+1,k);
        }
        Vector2 baseCenter = new Vector2(r.center.x,r.yMin+19);
        Ring(vh,baseCenter,new Vector2(r.width*.25f,13),c,.9f);
        Ring(vh,baseCenter,new Vector2(r.width*.17f,8),c,.65f);
    }
    private static void Ring(VertexHelper vh, Vector2 p,Vector2 radius,Color tint,float alpha)
    {
        const int n=48; int start=vh.currentVertCount; tint.a*=alpha;
        for(int i=0;i<n;i++)
        {
            float a=i*2*Mathf.PI/n; Vector2 v=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
            vh.AddVert(p+Vector2.Scale(v,radius),tint,Vector2.zero);
            vh.AddVert(p+Vector2.Scale(v,radius-new Vector2(1.5f,1.5f)),tint,Vector2.zero);
        }
        for(int i=0;i<n;i++) { int a=start+i*2,b=start+((i+1)%n)*2; vh.AddTriangle(a,b,a+1);vh.AddTriangle(a+1,b,b+1); }
    }
}
