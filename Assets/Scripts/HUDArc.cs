using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free radial gauge for a uGUI Canvas. Angles are degrees from +X.</summary>
[AddComponentMenu("UI/HUD Arc")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HUDArc : MaskableGraphic
{
    [Header("Geometry (Canvas units)")]
    [Range(-360f, 360f)] public float startAngle = 205f;
    [Range(1f, 360f)] public float sweepAngle = 230f;
    public bool clockwise = true;
    [Range(8, 128)] public int resolution = 48;
    [Min(0f)] public float padding = 1f;
    [Min(0f)] public float arcThickness = 3f;
    [Header("Appearance")]
    public Color trackColor = new Color32(100, 116, 126, 180);
    public Color fillColor = new Color32(112, 231, 237, 255);
    public Color tickColor = new Color32(220, 232, 238, 220);
    public Color needleColor = new Color32(244, 246, 247, 255);
    [Header("Ticks")]
    public bool showTicks = true;
    [Range(2, 25)] public int tickCount = 7;
    [Min(0f)] public float tickLength = 4f;
    [Min(0f)] public float tickWidth = 1.4f;
    [Min(0f)] public float tickGap = 1.5f;
    [Header("Needle")]
    public bool showNeedle = true;
    [Range(0f, 1f)] public float needleLength = 0.65f;
    [Min(0f)] public float needleWidth = 3f;
    [Min(0f)] public float hubRadius = 2.6f;
    [Header("Value / Editor Preview")]
    [SerializeField, Range(0f, 1f)] private float value;

    private bool hasRuntimeTint;
    private Color runtimeTint;
    private static readonly Vector2 UV = new Vector2(0.5f, 0.5f);
    public float Value => value;

    public void SetValue(float amount)
    {
        amount = Sanitize(amount);
        if (value == amount) return;
        value = amount;
        SetVerticesDirty();
    }

    public void SetValue(float amount, Color accent)
    {
        amount = Sanitize(amount);
        if (value == amount && hasRuntimeTint && runtimeTint == accent) return;
        value = amount;
        runtimeTint = accent;
        hasRuntimeTint = true;
        SetVerticesDirty();
    }

    public void ClearRuntimeTint()
    {
        if (!hasRuntimeTint) return;
        hasRuntimeTint = false;
        SetVerticesDirty();
    }

    public void Refresh() => SetVerticesDirty();

    private static float Sanitize(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? 0f : Mathf.Clamp01(v);

#if UNITY_EDITOR
    protected override void Reset()
    {
        base.Reset();
        raycastTarget = false;
        color = Color.white;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f - Mathf.Max(0f, padding);
        if (radius <= 0.001f) return;
        Vector2 center = rect.center;
        float thickness = Mathf.Clamp(arcThickness, 0f, radius);
        float inner = radius - thickness;
        float sweep = Mathf.Clamp(sweepAngle, 1f, 360f) * (clockwise ? -1f : 1f);
        int steps = Mathf.Clamp(resolution, 8, 128);
        float progress = Sanitize(value);
        Color32 active = (hasRuntimeTint ? runtimeTint : fillColor) * color;
        Color32 track = trackColor * color;
        if (thickness > 0.001f)
        {
            // Split only the segment containing the current value. The track and
            // fill share exact vertices, rather than drawing translucent layers.
            for (int i = 0; i < steps; i++)
            {
                float a = (float)i / steps;
                float b = (float)(i + 1) / steps;
                float cut = Mathf.Clamp(progress, a, b);
                AddRing(vh, center, inner, radius, a, cut, sweep, active);
                AddRing(vh, center, inner, radius, cut, b, sweep, track);
            }
        }
        if (showTicks && tickWidth > 0f && tickLength > 0f)
        {
            int count = Mathf.Clamp(tickCount, 2, 25);
            bool closed = Mathf.Abs(sweep) >= 359.999f;
            float outer = Mathf.Max(0f, inner - Mathf.Max(0f, tickGap));
            float low = Mathf.Max(0f, outer - tickLength);
            float halfWidth = Mathf.Min(tickWidth * 0.5f,
                Mathf.Sqrt(Mathf.Max(0f, radius * radius - outer * outer)));
            if (outer > low && halfWidth > 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    float t = (float)i / (closed ? count : count - 1);
                    Vector2 d = Direction(t, sweep);
                    Vector2 side = new Vector2(-d.y, d.x) * halfWidth;
                    AddQuad(vh, center + d * low - side, center + d * outer - side,
                        center + d * outer + side, center + d * low + side, tickColor * color);
                }
            }
        }
        if (showNeedle)
        {
            Vector2 d = Direction(progress, sweep);
            float len = radius * Mathf.Clamp01(needleLength);
            float halfWidth = Mathf.Min(Mathf.Max(0f, needleWidth) * 0.5f, radius);
            Vector2 side = new Vector2(-d.y, d.x) * halfWidth;
            Color32 tint = needleColor * color;
            if (len > 0.001f && halfWidth > 0.001f)
            {
                int index = vh.currentVertCount;
                vh.AddVert(center - side, tint, UV);
                vh.AddVert(center + d * len, tint, UV);
                vh.AddVert(center + side, tint, UV);
                vh.AddTriangle(index, index + 1, index + 2);
            }
            float hub = Mathf.Clamp(hubRadius, 0f, radius);
            if (hub > 0.001f)
            {
                int index = vh.currentVertCount;
                vh.AddVert(center, tint, UV);
                const int sides = 16;
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * hub, tint, UV);
                }
                for (int i = 0; i < sides; i++)
                    vh.AddTriangle(index, index + 1 + i, index + 1 + (i + 1) % sides);
            }
        }
    }

    private Vector2 Direction(float t, float sweep)
    {
        float angle = (startAngle + t * sweep) * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private void AddRing(VertexHelper vh, Vector2 center, float inner, float outer,
        float a, float b, float sweep, Color32 tint)
    {
        if (b <= a) return;
        Vector2 da = Direction(a, sweep), db = Direction(b, sweep);
        AddQuad(vh, center + da * inner, center + da * outer,
            center + db * outer, center + db * inner, tint);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, tint, UV); vh.AddVert(b, tint, UV);
        vh.AddVert(c, tint, UV); vh.AddVert(d, tint, UV);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}
