using UnityEngine;
using UnityEngine.UI;

/// <summary>One bounded mesh for the menu's ambient progression sparks, rays and confirmation ring.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/HUD Reward Atmosphere")]
public sealed class HUDRewardAtmosphere : MaskableGraphic
{
    private const int Capacity = 64;
    private const int Rays = 14;
    private const int RingSegments = 64;
    private const float Tau = Mathf.PI * 2f;
    private struct Ember { public float x, y, speed, size, phase; }
    private readonly Ember[] embers = new Ember[Capacity];
    private bool initialized, playing, ambient, ringEnabled;
    private int count;
    private Color tint, ringTint;
    private float aura, emberOpacity, elapsed, lastDraw, ringStart = -100f;
    private float ringDuration, ringRadius, ringStrength;
    private Vector2 ringCenter;
    private static readonly Vector2 UV = new Vector2(.5f,.5f);

    protected override void Awake()
    {
        base.Awake(); raycastTarget = false;
    }
    public void Begin(Color colorTint, bool showAmbient, int particleCount, float auraOpacity, float particleOpacity, bool showRing)
    {
        if (!initialized)
        {
            // Local RNG: never consume gameplay's UnityEngine.Random sequence.
            var random = new System.Random(71239);
            for (int i=0;i<Capacity;i++) embers[i] = new Ember {
                x=(float)random.NextDouble(), y=(float)random.NextDouble(),
                speed=.045f+(float)random.NextDouble()*.065f,
                size=1.6f+(float)random.NextDouble()*2.2f,
                phase=(float)random.NextDouble()*Tau };
            initialized=true;
        }
        tint=colorTint; ambient=showAmbient; ringEnabled=showRing;
        count=Mathf.Clamp(particleCount,0,Capacity); aura=Mathf.Clamp01(auraOpacity);
        emberOpacity=Mathf.Clamp01(particleOpacity); elapsed=0f;lastDraw=0f;ringStart=-100f;
        playing=ambient || ringEnabled; SetVerticesDirty();
    }
    public void Shockwave(RectTransform target, float duration, float strength, Color confirmationTint)
    {
        if (!playing || !ringEnabled || target==null || duration<=0f) return;
        Vector3 c=target.TransformPoint(target.rect.center);
        ringCenter=rectTransform.InverseTransformPoint(c);
        // Convert both card axes into this graphic's local units, including Canvas scale.
        Vector3 x=rectTransform.InverseTransformVector(target.TransformVector(Vector3.right*target.rect.width));
        Vector3 y=rectTransform.InverseTransformVector(target.TransformVector(Vector3.up*target.rect.height));
        ringRadius=Mathf.Max(1f,Mathf.Sqrt(x.sqrMagnitude+y.sqrMagnitude)*.5f);
        ringStart=elapsed;ringDuration=duration;ringStrength=Mathf.Clamp01(strength);ringTint=confirmationTint;
        SetVerticesDirty();
    }
    public void Stop()
    {
        playing=false;ringStart=-100f;SetVerticesDirty();
    }
    protected override void OnDisable(){playing=false;base.OnDisable();}
    private void LateUpdate()
    {
        if (!playing) return;
        elapsed += Time.unscaledDeltaTime;
        bool ring=elapsed-ringStart<ringDuration && ringStart>=0f;
        // Finish the final ring frame even if ambient is disabled. Cap ambient updates at ~30 Hz; use ~60 Hz for the short confirmation ring.
        if(!ambient && !ring && ringStart<0f)return;
        if(elapsed-lastDraw < (ring ? 1f/60f : 1f/30f))return;
        if(!ring)ringStart=-100f;
        lastDraw=elapsed;SetVerticesDirty();
    }
    private Color32 Tint(float alpha)
    {
        Color c=tint*color;c.a*=Mathf.Clamp01(alpha);return c;
    }
    private static Vector2 Polar(float angle,float radius) => new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
    private static void Quad(VertexHelper vh, Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color32 ca,Color32 cb,Color32 cc,Color32 cd)
    {
        int n=vh.currentVertCount;vh.AddVert(a,ca,UV);vh.AddVert(b,cb,UV);vh.AddVert(c,cc,UV);vh.AddVert(d,cd,UV);
        vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if(!playing)return;
        Rect r=rectTransform.rect;if(r.width<=0f||r.height<=0f)return;
        Vector2 center=r.center+Vector2.up*r.height*.035f;
        float t=elapsed-ringStart;
        bool ring=ringStart>=0f && t<ringDuration;
        float pulse=ring?Mathf.Sin(Mathf.PI*Mathf.Clamp01(t/ringDuration)):0f;
        if(ambient)
        {
            float opacity=aura*(.8f+.2f*Mathf.Sin(elapsed*Tau/2.8f)) + pulse*Mathf.Min(.12f,aura*1.2f);
            float radius=Mathf.Sqrt(r.width*r.width+r.height*r.height)*.55f;
            Color32 near=Tint(opacity), mid=Tint(opacity*.65f), far=Tint(0);
            for(int i=0;i<Rays;i++)
            {
                float a=i*Tau/Rays+elapsed*7f*Mathf.Deg2Rad, half=.080f;
                Vector2 v=Polar(a-half,1f),w=Polar(a+half,1f);
                Quad(vh,center+v*radius*.10f,center+w*radius*.10f,center+w*radius*.48f,center+v*radius*.48f,near,near,mid,mid);
                Quad(vh,center+v*radius*.48f,center+w*radius*.48f,center+w*radius,center+v*radius,mid,mid,far,far);
            }
            for(int i=0;i<count;i++)
            {
                Ember e=embers[i];float y=Mathf.Repeat(e.y+elapsed*e.speed,1f);
                float x=Mathf.Repeat(e.x+Mathf.Sin(elapsed*.55f+e.phase)*.025f,1f);
                float fade=Mathf.Clamp01(Mathf.Min(y,1f-y)*8f)*Mathf.Clamp01(Mathf.Min(x,1f-x)*8f);
                Color spark=Color.Lerp(tint,Color.white,i%4==0?.85f:.12f)*color;
                spark.a*=emberOpacity*fade*(.65f+.35f*Mathf.Sin(elapsed*1.6f+e.phase));
                Color32 c=spark;
                Vector2 p=new Vector2(r.xMin+x*r.width,r.yMin+y*r.height);
                Quad(vh,p+Vector2.up*e.size*1.8f,p+Vector2.right*e.size,p-Vector2.up*e.size*1.8f,p-Vector2.right*e.size,c,c,c,c);
            }
        }
        if(ring)
        {
            float u=Mathf.Clamp01(t/ringDuration), ease=1f-(1f-u)*(1f-u);
            float rad=ringRadius*Mathf.Lerp(.82f,1.8f,ease), width=Mathf.Lerp(4f,1f,u);
            Color ringColor=ringTint*color;ringColor.a*=ringStrength*(1f-u)*(1f-u);Color32 c=ringColor;
            for(int i=0;i<RingSegments;i++)
            {
                float a=i*Tau/RingSegments,b=(i+1)*Tau/RingSegments;
                Quad(vh,ringCenter+Polar(a,Mathf.Max(0f,rad-width)),ringCenter+Polar(b,Mathf.Max(0f,rad-width)),ringCenter+Polar(b,rad),ringCenter+Polar(a,rad),c,c,c,c);
            }
        }
    }
}
