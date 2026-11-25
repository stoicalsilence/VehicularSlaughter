using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free, fractionally filled angled segments for uGUI.</summary>
[AddComponentMenu("UI/HUD Ammo Pips")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HUDAmmoPips : MaskableGraphic
{
    [Header("Geometry (Canvas units)")]
    [SerializeField, Range(1, 32)] private int segmentCount = 4;
    [SerializeField, Min(0f)] private float gap = 4f;
    [SerializeField] private float slant = 5f;
    [SerializeField] private bool reverseFill;
    [Header("Appearance")]
    [SerializeField] private Color emptyColor = new Color32(64, 80, 88, 180);
    [SerializeField] private Color defaultFillColor = new Color32(112, 231, 237, 255);
    [SerializeField, Range(0f, 1f)] private float previewValue = 1f;
    private float value;
    private Color fillColor;
    private bool hasValue;

    public void SetValue(float fraction, Color tint)
    {
        fraction = Mathf.Clamp01(fraction);
        if (hasValue && value == fraction && fillColor.Equals(tint)) return;
        value = fraction;
        fillColor = tint;
        hasValue = true;
        SetVerticesDirty();
    }
    public void SetValue(float fraction) => SetValue(fraction, defaultFillColor);
    public void Refresh() => SetVerticesDirty();

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
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0.001f || r.height <= 0.001f) return;
        int n = Mathf.Clamp(segmentCount, 1, 32);
        // Gap measured along each horizontal row; slanted edges share the same offset.
        float shear = Mathf.Clamp(slant, -r.width * 0.45f, r.width * 0.45f);
        float usable = r.width - Mathf.Abs(shear);
        float spacing = n > 1 ? Mathf.Clamp(gap, 0f, usable / (n - 1) * 0.9f) : 0f;
        float width = (usable - (n - 1) * spacing) / n;
        float topOffset = Mathf.Max(0f, shear);
        float bottomOffset = Mathf.Max(0f, -shear);
        float f = Application.isPlaying && hasValue ? value : previewValue;
        Color32 active = (Application.isPlaying && hasValue ? fillColor : defaultFillColor) * color;
        Color32 empty = emptyColor * color;
        for (int i = 0; i < n; i++)
        {
            float x = r.xMin + i * (width + spacing);
            float amount = Mathf.Clamp01(f * n - (reverseFill ? n - 1 - i : i));
            float split = reverseFill ? 1f - amount : amount;
            if (reverseFill)
            {
                AddPart(vh, r, x, width, topOffset, bottomOffset, 0f, split, empty);
                AddPart(vh, r, x, width, topOffset, bottomOffset, split, 1f, active);
            }
            else
            {
                AddPart(vh, r, x, width, topOffset, bottomOffset, 0f, split, active);
                AddPart(vh, r, x, width, topOffset, bottomOffset, split, 1f, empty);
            }
        }
    }

    private static void AddPart(VertexHelper vh, Rect r, float x, float width,
        float top, float bottom, float from, float to, Color32 tint)
    {
        if (to <= from || tint.a == 0) return;
        float a = x + width * from, b = x + width * to;
        int i = vh.currentVertCount;
        Vector2 uv = new Vector2(0.5f, 0.5f);
        vh.AddVert(new Vector3(a + bottom, r.yMin, 0f), tint, uv);
        vh.AddVert(new Vector3(a + top, r.yMax, 0f), tint, uv);
        vh.AddVert(new Vector3(b + top, r.yMax, 0f), tint, uv);
        vh.AddVert(new Vector3(b + bottom, r.yMin, 0f), tint, uv);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i + 2, i + 3, i);
    }
}
