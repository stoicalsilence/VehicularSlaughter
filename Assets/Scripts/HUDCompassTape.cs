using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Compass Tape")]
public sealed class HUDCompassTape : MaskableGraphic
{
    public Color lineColor = new Color32(169, 190, 204, 190);
    public Color accentColor = new Color32(112, 231, 237, 255);
    [Min(0.5f)] public float lineThickness = 1.25f;
    [Min(1f)] public float tickHeight = 12f;
    public bool minorTicks = false;
    private float heading;
    private float span = 250f;
    private float fade = 0.18f;
    private bool ready;

    public void SetHeading(float value, float angularSpan, float edgeFade)
    {
        if (ready && Mathf.Abs(Mathf.DeltaAngle(heading, value)) < 0.03f &&
            Mathf.Approximately(span, angularSpan) && Mathf.Approximately(fade, edgeFade)) return;
        heading = value; span = Mathf.Clamp(angularSpan, 60f, 350f); fade = Mathf.Clamp(edgeFade, 0.01f, 0.49f);
        ready = true; SetVerticesDirty();
    }
    public static float EdgeOpacity(float normalizedX, float fraction)
    {
        float t = Mathf.Clamp01((1f - Mathf.Abs(normalizedX)) / Mathf.Max(0.02f, fraction * 2f));
        return t * t * (3f - 2f * t);
    }
    private static Color WithAlpha(Color c, float a) { c.a *= a; return c; }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); Rect r = rectTransform.rect;
        float half = r.width * 0.5f, cx = r.center.x, y = r.center.y;
        if (half < 20f || r.height < 20f) return;
        Color rim = lineColor * color, accent = accentColor * color;
        // Faded ends, horizontal baseline, raised notch and fixed pointer.
        float inset = Mathf.Min(10f,half*0.2f), notch = Mathf.Min(15f,half*0.35f);
        float shoulder = Mathf.Clamp(half * (1f - fade * 2f),notch,half-inset);
        Segment(vh, new Vector2(cx-half,y+7), new Vector2(cx-half+inset,y), WithAlpha(rim,0), WithAlpha(rim,0.25f));
        Segment(vh, new Vector2(cx-half+inset,y), new Vector2(cx-shoulder,y), WithAlpha(rim,0.25f),rim);
        Segment(vh, new Vector2(cx-shoulder,y), new Vector2(cx-notch,y), rim,rim);
        Segment(vh, new Vector2(cx-notch,y), new Vector2(cx-5,y+7), rim,rim);
        Segment(vh, new Vector2(cx-5,y+7), new Vector2(cx+5,y+7), rim,rim);
        Segment(vh, new Vector2(cx+5,y+7), new Vector2(cx+notch,y), rim,rim);
        Segment(vh, new Vector2(cx+notch,y), new Vector2(cx+shoulder,y), rim,rim);
        Segment(vh, new Vector2(cx+shoulder,y), new Vector2(cx+half-inset,y), rim,WithAlpha(rim,0.25f));
        Segment(vh, new Vector2(cx+half-inset,y), new Vector2(cx+half,y+7), WithAlpha(rim,0.25f),WithAlpha(rim,0));
        Triangle(vh, new Vector2(cx-8,y+22),new Vector2(cx+8,y+22),new Vector2(cx,y+11),accent);
        int step = minorTicks ? 15 : 45;
        for (int b = 0; b < 360; b += step)
        {
            float n = Mathf.DeltaAngle(heading,b)/(span*0.5f);
            if (Mathf.Abs(n)>=1f) continue;
            float x = cx + n*half;
            // Avoid drawing over the fixed center notch.
            if (Mathf.Abs(x-cx)<notch) continue;
            Color c = WithAlpha(rim,EdgeOpacity(n,fade)*(b%45==0?1f:0.45f));
            Segment(vh,new Vector2(x,y),new Vector2(x,y+tickHeight*(b%45==0?1f:0.5f)),c,c);
        }
    }
    private void Segment(VertexHelper vh, Vector2 a, Vector2 b, Color ca, Color cb)
    {
        Vector2 d = b-a; if(d.sqrMagnitude<0.0001f) return;
        Vector2 n = new Vector2(-d.y,d.x).normalized * (Mathf.Max(0.5f,lineThickness)*0.5f);
        int i=vh.currentVertCount;
        vh.AddVert(a-n,ca,Vector2.zero); vh.AddVert(a+n,ca,Vector2.zero);
        vh.AddVert(b+n,cb,Vector2.zero); vh.AddVert(b-n,cb,Vector2.zero);
        vh.AddTriangle(i,i+1,i+2); vh.AddTriangle(i,i+2,i+3);
    }
    private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int i=vh.currentVertCount;
        vh.AddVert(a,tint,Vector2.zero); vh.AddVert(b,tint,Vector2.zero); vh.AddVert(c,tint,Vector2.zero); vh.AddTriangle(i,i+1,i+2);
    }
#if UNITY_EDITOR
    protected override void Reset() { base.Reset(); raycastTarget=false; color=Color.white; }
    protected override void OnValidate() { base.OnValidate(); SetVerticesDirty(); }
#endif
}
