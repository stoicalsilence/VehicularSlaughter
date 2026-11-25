using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free, angled uGUI bar. Values are normalized to [0, 1].</summary>
[AddComponentMenu("UI/HUD Progress Bar")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class HUDProgressBar : MaskableGraphic
{
    public enum FillDirection { LeftToRight, RightToLeft }

    [Header("Geometry (Canvas units)")]
    [Tooltip("Positive: the top endpoint is farther right than the bottom endpoint.")]
    public float leftSlant = 10f;
    public float rightSlant = 10f;
    public FillDirection direction = FillDirection.LeftToRight;
    [Min(0f)] public float borderThickness = 0f;

    [Header("Appearance")]
    public Color trackColor = new Color32(53, 63, 69, 255);
    public Color fillColor = new Color32(233, 83, 80, 255);
    public Color damageColor = new Color32(239, 179, 91, 255);
    public Color borderColor = new Color32(137, 153, 159, 255);

    [Header("Animation (seconds and normalized units per second)")]
    [Tooltip("Zero snaps the foreground immediately on damage.")]
    [Min(0f)] public float damageFillSpeed = 7f;
    [Tooltip("Zero snaps the foreground immediately on healing.")]
    [Min(0f)] public float healingFillSpeed = 2.5f;
    public bool showDamageTrail = true;
    [Tooltip("Keep existing damage feedback while healing (useful for passive regeneration). Disable to clear the trail on every healing update.")]
    public bool preserveDamageTrailDuringHealing = true;
    [Min(0f)] public float damageHoldSeconds = 0.28f;
    [Tooltip("Zero snaps the trail after its hold time.")]
    [Min(0f)] public float damageTrailSpeed = 0.85f;
    public bool useUnscaledTime = false;

    [Header("Editor preview / initial standalone value")]
    [SerializeField, Range(0f, 1f)] private float previewValue = 1f;
    [SerializeField, Range(0f, 1f)] private float previewTrail = 1f;

    private bool initialized;
    private float target;
    private float displayed;
    private float trail;
    private float holdRemaining;
    private bool healing;
    private static readonly Vector2 UV = new Vector2(0.5f, 0.5f);
    private const float Epsilon = 0.0001f;

    public float Value => initialized ? target : Sanitize(previewValue);
    public float DisplayedValue => initialized ? displayed : Sanitize(previewValue);
    public float DamageTrailValue => initialized ? trail : Sanitize(previewValue);
    public bool IsAnimating => initialized && (displayed != target || trail != displayed);

    /// <summary>Set a new value. The first sample always snaps; subsequent changes animate.</summary>
    public void SetValue(float normalizedValue, bool immediate = false)
    {
        float next = Sanitize(normalizedValue);
        if (!initialized || immediate)
        {
            initialized = true;
            displayed = target = trail = next;
            holdRemaining = 0f;
            healing = false;
            SetVerticesDirty();
            return;
        }
        if (next == target) return;

        if (next < target)
        {
            healing = false;
            if (showDamageTrail)
            {
                // Repeated hits preserve the remaining trail and restart its hold.
                trail = Mathf.Max(trail, Mathf.Max(displayed, target));
                holdRemaining = Mathf.Max(0f, damageHoldSeconds);
            }
            else
            {
                trail = displayed;
                holdRemaining = 0f;
            }
        }
        else
        {
            healing = true;
            if (!preserveDamageTrailDuringHealing)
            {
                trail = displayed;
                holdRemaining = 0f;
            }
        }
        target = next;
        SetVerticesDirty();
    }

    public void SetValue(float current, float maximum, bool immediate = false)
    {
        SetValue(Normalize(current, maximum), immediate);
    }

    public void SnapToValue(float normalizedValue) => SetValue(normalizedValue, true);

    public static float Normalize(float current, float maximum)
    {
        return maximum > 0f ? Sanitize(current / maximum) : 0f;
    }

    // Call after changing public appearance / geometry fields at runtime.
    public void Refresh() => SetVerticesDirty();

    private static float Sanitize(float value)
    {
        return float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
    }

    private static float Advance(float current, float destination, float speed, float dt)
    {
        return speed <= 0f ? destination : Mathf.MoveTowards(current, destination, speed * dt);
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        float oldFill = displayed;
        float oldTrail = trail;

        displayed = Advance(displayed, target,
            displayed < target ? healingFillSpeed : damageFillSpeed, dt);

        if (!showDamageTrail || (healing && !preserveDamageTrailDuringHealing))
        {
            trail = displayed;
            holdRemaining = 0f;
        }
        else
        {
            // Only the portion of this frame after the hold expires drains the trail.
            float drainDt = Mathf.Max(0f, dt - holdRemaining);
            holdRemaining = Mathf.Max(0f, holdRemaining - dt);
            if (drainDt > 0f)
                trail = Advance(trail, displayed, damageTrailSpeed, drainDt);
            trail = Mathf.Max(trail, displayed);
        }

        if (oldFill != displayed || oldTrail != trail)
            SetVerticesDirty();
    }

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
        if (r.width <= Epsilon || r.height <= Epsilon) return;

        // Positive slants shift the top to the right. Clamp them proportionally
        // for tiny RectTransforms so that the polygon never crosses itself.
        float l = leftSlant, s = rightSlant;
        float total = Mathf.Max(Mathf.Max(0f, l) + Mathf.Max(0f, -s),
            Mathf.Max(0f, -l) + Mathf.Max(0f, s));
        float available = r.width - Epsilon;
        if (total > available)
        {
            float scale = available / total;
            l *= scale;
            s *= scale;
        }
        Quad outer = new Quad(
            new Vector2(r.xMin + Mathf.Max(0f, l), r.yMax),
            new Vector2(r.xMax - Mathf.Max(0f, -s), r.yMax),
            new Vector2(r.xMax - Mathf.Max(0f, s), r.yMin),
            new Vector2(r.xMin + Mathf.Max(0f, -l), r.yMin));

        Quad inner = outer;
        float leftNormal = Mathf.Sqrt(1f + (l / r.height) * (l / r.height));
        float rightNormal = Mathf.Sqrt(1f + (s / r.height) * (s / r.height));
        float narrow = Mathf.Min(outer.tr.x - outer.tl.x, outer.br.x - outer.bl.x);
        float limit = Mathf.Min(r.height * 0.499f, narrow * 0.999f / (leftNormal + rightNormal));
        float t = Mathf.Clamp(borderThickness, 0f, limit);
        if (t > Epsilon)
        {
            float f = t / r.height;
            Vector2 lt = Vector2.Lerp(outer.tl, outer.bl, f);
            Vector2 rt = Vector2.Lerp(outer.tr, outer.br, f);
            Vector2 lb = Vector2.Lerp(outer.bl, outer.tl, f);
            Vector2 rb = Vector2.Lerp(outer.br, outer.tr, f);
            lt.x += t * leftNormal; lb.x += t * leftNormal;
            rt.x -= t * rightNormal; rb.x -= t * rightNormal;
            inner = new Quad(lt, rt, rb, lb);
            Color32 rim = borderColor * color;
            AddQuad(vh, outer.tl, outer.tr, inner.tr, inner.tl, rim);
            AddQuad(vh, outer.tr, outer.br, inner.br, inner.tr, rim);
            AddQuad(vh, outer.br, outer.bl, inner.bl, inner.br, rim);
            AddQuad(vh, outer.bl, outer.tl, inner.tl, inner.bl, rim);
        }

        bool playing = Application.isPlaying;
        float fValue = playing && initialized ? displayed : Sanitize(previewValue);
        float dValue = playing ? (initialized ? trail : fValue) : Sanitize(previewTrail);
        dValue = showDamageTrail ? Mathf.Max(fValue, dValue) : fValue;
        AddSegment(vh, inner, 0f, fValue, fillColor * color);
        AddSegment(vh, inner, fValue, dValue, damageColor * color);
        AddSegment(vh, inner, dValue, 1f, trackColor * color);
    }

    private void AddSegment(VertexHelper vh, Quad q, float from, float to, Color32 tint)
    {
        if (to <= from || tint.a == 0) return;
        if (direction == FillDirection.RightToLeft)
        {
            float oldFrom = from;
            from = 1f - to;
            to = 1f - oldFrom;
        }
        // Interpolating both edges retains the parallelogram's angle as it fills.
        AddQuad(vh, Vector2.Lerp(q.tl, q.tr, from), Vector2.Lerp(q.tl, q.tr, to),
            Vector2.Lerp(q.bl, q.br, to), Vector2.Lerp(q.bl, q.br, from), tint);
    }

    private struct Quad
    {
        public Vector2 tl, tr, br, bl;
        public Quad(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl)
        { this.tl = tl; this.tr = tr; this.br = br; this.bl = bl; }
    }

    private static void AddQuad(VertexHelper vh, Vector2 tl, Vector2 tr,
        Vector2 br, Vector2 bl, Color32 tint)
    {
        if (tint.a == 0) return;
        int i = vh.currentVertCount;
        vh.AddVert(tl, tint, UV); vh.AddVert(tr, tint, UV);
        vh.AddVert(br, tint, UV); vh.AddVert(bl, tint, UV);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(HUDProgressBar))]
[UnityEditor.CanEditMultipleObjects]
public sealed class HUDProgressBarEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        UnityEditor.EditorGUILayout.HelpBox(
            "Resize using RectTransform, not Transform scale. Equal slants give a " +
            "parallelogram with a constant fill angle. Preview Value and Preview Trail " +
            "work in Edit Mode. Keep Graphic Color white for the exact style colors.",
            UnityEditor.MessageType.Info);
        if (Application.isPlaying && targets.Length == 1)
        {
            HUDProgressBar bar = (HUDProgressBar)target;
            UnityEditor.EditorGUILayout.LabelField("Live value", bar.Value.ToString("P1"));
            UnityEditor.EditorGUILayout.LabelField("Displayed", bar.DisplayedValue.ToString("P1"));
            UnityEditor.EditorGUILayout.LabelField("Damage trail", bar.DamageTrailValue.ToString("P1"));
            if (GUILayout.Button("Test damage (-25%)"))
            { float value = bar.Value; bar.SetValue(value); bar.SetValue(value - 0.25f); }
            if (GUILayout.Button("Test healing (+25%)"))
            { float value = bar.Value; bar.SetValue(value); bar.SetValue(value + 0.25f); }
            if (GUILayout.Button("Reset to full")) bar.SnapToValue(1f);
            if (bar.IsAnimating) Repaint();
        }
    }
}
#endif
