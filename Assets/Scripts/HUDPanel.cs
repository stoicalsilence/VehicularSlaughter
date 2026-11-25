using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free chamfered panel for a uGUI Canvas.</summary>
[AddComponentMenu("UI/HUD Panel")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HUDPanel : MaskableGraphic
{
    [Flags]
    public enum CornerAccents
    {
        None = 0, TopLeft = 1, TopRight = 2,
        BottomRight = 4, BottomLeft = 8, All = 15
    }

    [Header("Corners (Canvas units)")]
    [Min(0)] public float topLeft = 12f;
    [Min(0)] public float topRight = 8f;
    [Min(0)] public float bottomRight = 12f;
    [Min(0)] public float bottomLeft = 8f;

    [Header("Appearance")]
    public Color fillColor = new Color32(25, 38, 45, 220);
    public Color borderColor = new Color32(137, 153, 159, 255);
    [Min(0)] public float borderThickness = 1.5f;

    [Header("Corner accents")]
    public Color accentColor = new Color32(112, 231, 237, 255);
    public CornerAccents accents = CornerAccents.TopLeft | CornerAccents.BottomLeft;
    [Min(0)] public float accentLength = 24f;

    // Reuse buffers. No per-rebuild managed array allocation.
    private readonly Vector2[] outer = new Vector2[8];
    private readonly Vector2[] inner = new Vector2[8];
    private static readonly Vector2 UV = new Vector2(0.5f, 0.5f);
    private const float Epsilon = 0.001f;

    // Call after changing public style fields from a runtime script.
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
        Rect rect = GetPixelAdjustedRect();
        float half = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (half <= Epsilon) return;

        float tl = Mathf.Clamp(topLeft, 0, half);
        float tr = Mathf.Clamp(topRight, 0, half);
        float br = Mathf.Clamp(bottomRight, 0, half);
        float bl = Mathf.Clamp(bottomLeft, 0, half);
        MakePolygon(rect, tl, tr, br, bl, outer);

        Color32 fill = fillColor * color;
        Color32 rim = borderColor * color;
        Color32 highlight = accentColor * color;
        float t = Mathf.Clamp(borderThickness, 0, half - Epsilon);
        if (t <= Epsilon)
        {
            AddFace(vh, outer, fill);
            return;
        }

        Rect inset = Rect.MinMaxRect(rect.xMin + t, rect.yMin + t,
                                     rect.xMax - t, rect.yMax - t);
        // Offset a 45-degree chamfer inward by the same normal distance
        // as the horizontal and vertical edges.
        float d = (2f - Mathf.Sqrt(2f)) * t;
        float innerHalf = Mathf.Min(inset.width, inset.height) * 0.5f;
        MakePolygon(inset, Mathf.Clamp(tl - d, 0, innerHalf),
                           Mathf.Clamp(tr - d, 0, innerHalf),
                           Mathf.Clamp(br - d, 0, innerHalf),
                           Mathf.Clamp(bl - d, 0, innerHalf), inner);
        AddFace(vh, inner, fill);

        // Even edges are straight; odd edges are the corner chamfers.
        // Emit a non-overlapping ring with optional colored end segments.
        for (int i = 0; i < 8; i++)
        {
            int j = (i + 1) % 8;
            float length = Vector2.Distance(outer[i], outer[j]);
            if ((i & 1) != 0)
            {
                AddStrip(vh, i, 0, 1, Has(CornerAt((i + 1) / 2)) ? highlight : rim);
                continue;
            }
            if (length <= Epsilon)
            {
                // Degenerate outer edge can still have a nonzero inner edge.
                AddStrip(vh, i, 0, 1, rim);
                continue;
            }
            float start = Has(CornerAt(i / 2)) ? Mathf.Max(0, accentLength) / length : 0;
            float end = Has(CornerAt(i / 2 + 1)) ? Mathf.Max(0, accentLength) / length : 0;
            start = Mathf.Clamp01(start);
            end = Mathf.Clamp01(end);
            if (start + end >= 1f)
                AddStrip(vh, i, 0, 1, highlight);
            else
            {
                AddStrip(vh, i, 0, start, highlight);
                AddStrip(vh, i, start, 1 - end, rim);
                AddStrip(vh, i, 1 - end, 1, highlight);
            }
        }
    }

    private bool Has(CornerAccents corner) => (accents & corner) != 0;
    private static CornerAccents CornerAt(int index) => (CornerAccents)(1 << (index % 4));

    // Clockwise, starting just to the right of the upper-left corner.
    private static void MakePolygon(Rect r, float tl, float tr, float br, float bl, Vector2[] p)
    {
        p[0] = new Vector2(r.xMin + tl, r.yMax);
        p[1] = new Vector2(r.xMax - tr, r.yMax);
        p[2] = new Vector2(r.xMax, r.yMax - tr);
        p[3] = new Vector2(r.xMax, r.yMin + br);
        p[4] = new Vector2(r.xMax - br, r.yMin);
        p[5] = new Vector2(r.xMin + bl, r.yMin);
        p[6] = new Vector2(r.xMin, r.yMin + bl);
        p[7] = new Vector2(r.xMin, r.yMax - tl);
    }

    private static void AddFace(VertexHelper vh, Vector2[] p, Color32 c)
    {
        int first = vh.currentVertCount;
        for (int i = 0; i < 8; i++) vh.AddVert(p[i], c, UV);
        for (int i = 1; i < 7; i++) vh.AddTriangle(first, first + i, first + i + 1);
    }

    private void AddStrip(VertexHelper vh, int i, float from, float to, Color32 c)
    {
        if (to <= from) return;
        int j = (i + 1) % 8;
        int first = vh.currentVertCount;
        vh.AddVert(Vector2.Lerp(outer[i], outer[j], from), c, UV);
        vh.AddVert(Vector2.Lerp(outer[i], outer[j], to), c, UV);
        vh.AddVert(Vector2.Lerp(inner[i], inner[j], to), c, UV);
        vh.AddVert(Vector2.Lerp(inner[i], inner[j], from), c, UV);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}

#if UNITY_EDITOR
// Graphic's standard Inspector does not necessarily show derived style fields.
// This explicit inspector exposes them and the inherited serialized properties.
[UnityEditor.CustomEditor(typeof(HUDPanel))]
[UnityEditor.CanEditMultipleObjects]
public sealed class HUDPanelEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        UnityEditor.EditorGUILayout.HelpBox(
            "Corner sizes and accent lengths are in Canvas units. " +
            "Disable Raycast Target for decorative panels. " +
            "Call Refresh() after runtime style changes.",
            UnityEditor.MessageType.Info);
    }
}
#endif
