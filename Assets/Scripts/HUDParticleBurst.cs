using UnityEngine;
using UnityEngine.UI;

/// <summary>Bounded, texture-free particle bursts rendered in a uGUI Canvas.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Particle Burst")]
public sealed class HUDParticleBurst : MaskableGraphic
{
    private const int Capacity = 128;

    [Header("Emission")]
    [SerializeField, Range(1, 64)] private int particlesPerBurst = 24;
    [SerializeField, Min(0.05f)] private float lifetime = 0.55f;
    [SerializeField, Range(0f, 1f)] private float lifetimeVariation = 0.25f;
    [SerializeField, Min(0f)] private float riseSpeed = 100f;
    [SerializeField, Min(0f)] private float horizontalSpeed = 42f;
    [SerializeField, Min(0f)] private float downwardAcceleration = 85f;
    [SerializeField, Min(0.5f)] private float particleSize = 3.5f;
    [SerializeField, Range(0f, 1f)] private float streakChance = 0.35f;
    [SerializeField, Min(1f)] private float streakLength = 3.2f;
    [Header("Appearance")]
    [SerializeField] private Color primaryColor = new Color32(112, 201, 244, 255);
    [SerializeField] private Color secondaryColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color highlightColor = new Color32(255, 255, 255, 255);
    [SerializeField, Range(0f, 1f)] private float fadeStart = 0.18f;

    private struct Particle
    {
        public Vector2 position, velocity;
        public float age, life, size, angle, spin, stretch, gravity;
        public Color tint;
        public bool unscaled;
    }

    private readonly Particle[] particles = new Particle[Capacity];
    private readonly Vector3[] sourceCorners = new Vector3[4];
    private System.Random rng;
    private int count;

    public int ActiveParticleCount => count;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    /// <summary>Emit across the upper edge of source, in this graphic's local space.
    /// Both graphics should belong to the same Canvas/render space.</summary>
    public void Emit(RectTransform source, bool useUnscaledTime = false)
    {
        if (!isActiveAndEnabled || source == null) return;
        if (rng == null) rng = new System.Random(GetInstanceID());
        source.GetWorldCorners(sourceCorners); // BL, TL, TR, BR
        Vector2 left = rectTransform.InverseTransformPoint(sourceCorners[1]);
        Vector2 right = rectTransform.InverseTransformPoint(sourceCorners[2]);
        int amount = Mathf.Clamp(particlesPerBurst, 1, 64);
        // Drop oldest particles if repeated bursts fill the bounded pool.
        int overflow = Mathf.Max(0, count + amount - Capacity);
        if (overflow > 0)
        {
            System.Array.Copy(particles, overflow, particles, 0, count - overflow);
            count -= overflow;
        }
        for (int i = 0; i < amount; i++)
        {
            float u = (i + Random01()) / amount;
            float colorChoice = Random01();
            bool streak = Random01() < streakChance;
            Particle p = new Particle
            {
                position = Vector2.Lerp(left, right, u),
                velocity = new Vector2(RandomRange(-horizontalSpeed, horizontalSpeed) +
                    (u - 0.5f) * horizontalSpeed * 0.45f, riseSpeed * RandomRange(0.55f, 1.15f)),
                life = Mathf.Max(0.05f, lifetime * RandomRange(1f - lifetimeVariation, 1f + lifetimeVariation)),
                size = Mathf.Max(0.5f, particleSize * RandomRange(0.6f, 1.35f)),
                angle = streak ? RandomRange(-0.45f, 0.45f) : RandomRange(0f, Mathf.PI * 2f),
                spin = RandomRange(-2.2f, 2.2f),
                stretch = streak ? Mathf.Max(1f, streakLength) : 1f,
                gravity = Mathf.Max(0f, downwardAcceleration),
                tint = colorChoice < 0.24f ? highlightColor : colorChoice < 0.62f ? primaryColor : secondaryColor,
                unscaled = useUnscaledTime
            };
            particles[count++] = p;
        }
        SetVerticesDirty();
    }

    public void Clear()
    {
        if (count == 0) return;
        count = 0;
        SetVerticesDirty();
    }

    private void LateUpdate()
    {
        if (count == 0) return;
        float scaled = Time.deltaTime, unscaled = Time.unscaledDeltaTime;
        bool changed = false;
        // Compact in place, preserving emission order for overflow handling.
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            Particle p = particles[i];
            float dt = p.unscaled ? unscaled : scaled;
            if (dt > 0f)
            {
                changed = true;
                p.age += dt;
                if (p.age >= p.life) continue;
                p.position += p.velocity * dt + Vector2.down * (0.5f * p.gravity * dt * dt);
                p.velocity.y -= p.gravity * dt;
                p.angle += p.spin * dt;
            }
            particles[write++] = p;
        }
        count = write;
        if (changed) SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        count = 0;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (int i = 0; i < count; i++)
        {
            Particle p = particles[i];
            float t = Mathf.Clamp01(p.age / p.life);
            float fade = 1f - Mathf.InverseLerp(Mathf.Clamp(fadeStart, 0f, 0.99f), 1f, t);
            fade *= fade;
            float size = p.size * Mathf.Lerp(1f, 0.4f, t);
            Color tint = p.tint * color;
            tint.a *= fade;
            Color32 c = tint;
            float sin = Mathf.Sin(p.angle), cos = Mathf.Cos(p.angle);
            Vector2 x = new Vector2(cos, sin) * (size * 0.5f);
            Vector2 y = new Vector2(-sin, cos) * (size * 0.5f * p.stretch);
            int start = vh.currentVertCount;
            // Four vertices form a diamond (or a narrow geometric spark).
            vh.AddVert(p.position - y, c, Vector2.zero);
            vh.AddVert(p.position - x, c, Vector2.zero);
            vh.AddVert(p.position + y, c, Vector2.zero);
            vh.AddVert(p.position + x, c, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    private float Random01() => (float)rng.NextDouble();
    private float RandomRange(float min, float max) => Mathf.Lerp(min, max, Random01());
}
