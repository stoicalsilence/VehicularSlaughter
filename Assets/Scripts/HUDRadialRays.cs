using UnityEngine;
using UnityEngine.UI;

/// <summary>One-shot, texture-free radial celebration for uGUI. No raycasts or gameplay state.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CanvasRenderer))]
[AddComponentMenu("UI/HUD Radial Rays")]
public sealed class HUDRadialRays : MaskableGraphic
{
    [SerializeField, Range(4, 32)] private int rayCount = 14;
    [SerializeField, Range(.05f, .9f)] private float rayWidth = .38f;
    [SerializeField, Range(.1f, 1f)] private float radiusFraction = .95f;
    [SerializeField] private float rotationDegreesPerSecond = 16f;
    private float elapsed, duration, intensity;
    private bool unscaled;
    private Color tint;
    protected override void Awake() { base.Awake(); raycastTarget = false; }
    public void Play(Color accent, float opacity = .16f, float seconds = .7f, bool useUnscaledTime = true)
    {
        if (!isActiveAndEnabled) return;
        tint = accent; intensity = Mathf.Clamp01(opacity); duration = Mathf.Max(.01f, seconds);
        elapsed = 0f; unscaled = useUnscaledTime; SetVerticesDirty();
    }
    public void Clear() { duration = elapsed = 0f; SetVerticesDirty(); }
    protected override void OnDisable() { duration = elapsed = 0f; base.OnDisable(); }
    private void LateUpdate()
    {
        if (duration <= 0f) return;
        float dt = unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        elapsed += dt;
        if (elapsed >= duration) duration = 0f;
        SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (duration <= 0f || intensity <= 0f) return;
        Rect r = rectTransform.rect;
        float t = Mathf.Clamp01(elapsed / duration);
        float envelope = Mathf.Min(1f, t / .12f) * (1f - t) * (1f - t);
        Color c = tint * color; c.a *= intensity * envelope;
        Color outer = c; outer.a = 0f;
        float radius = Mathf.Min(r.width, r.height) * .5f * radiusFraction * Mathf.Lerp(.72f, 1f, t);
        if (radius <= 0f) return;
        int n = Mathf.Clamp(rayCount, 4, 32);
        float step = Mathf.PI * 2f / n, half = step * rayWidth * .5f;
        float angle = elapsed * rotationDegreesPerSecond * Mathf.Deg2Rad;
        Vector2 center = r.center;
        // Two radial bands: bright mid-radius, transparent outer edge. No overlapping wedges.
        for (int i = 0; i < n; i++)
        {
            float a = angle + i * step;
            Vector2 l = new Vector2(Mathf.Cos(a-half), Mathf.Sin(a-half));
            Vector2 h = new Vector2(Mathf.Cos(a+half), Mathf.Sin(a+half));
            int k = vh.currentVertCount;
            vh.AddVert(center, c, Vector2.zero);
            vh.AddVert(center + l * radius * .55f, c, Vector2.zero);
            vh.AddVert(center + h * radius * .55f, c, Vector2.zero);
            vh.AddVert(center + l * radius, outer, Vector2.zero);
            vh.AddVert(center + h * radius, outer, Vector2.zero);
            vh.AddTriangle(k,k+1,k+2); vh.AddTriangle(k+1,k+3,k+4); vh.AddTriangle(k+1,k+4,k+2);
        }
    }
}
