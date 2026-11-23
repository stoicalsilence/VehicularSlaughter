using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Independent, pooled mesh snapshot. Never modifies the enemy's materials.</summary>
[DisallowMultipleComponent]
public sealed class HordeDeathGhost : MonoBehaviour
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexSTId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int UseVertexColorId = Shader.PropertyToID("_UseVertexColor");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    private static readonly int EdgeBrightnessId = Shader.PropertyToID("_EdgeBrightness");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int RiseBiasId = Shader.PropertyToID("_RiseBias");
    private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
    private static readonly int OriginYId = Shader.PropertyToID("_DeathOriginY");

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Mesh skinnedSnapshot;
    private Material[] assignedMaterials;
    private MaterialPropertyBlock[] blocks;
    private HordeDeathDissolvePool owner;
    private float elapsed;
    private float lifetime;
    private float fallSpeed;
    private int slotCount;
    private bool playing;

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    internal bool Begin(Renderer source, Material[] baseMaterials, Material dissolveMaterial,
        bool emp, Color empTint, float originY, float seed, Color edgeColor,
        float duration, float noiseScale, float edgeWidth, float brightness,
        float riseBias, float heightScale, float worldFallSpeed, HordeDeathDissolvePool pool)
    {
        if (source == null) return false;
        Mesh mesh = null;
        SkinnedMeshRenderer skinned = source as SkinnedMeshRenderer;
        if (skinned != null)
        {
            if (skinnedSnapshot == null)
            {
                skinnedSnapshot = new Mesh();
                skinnedSnapshot.name = "Pooled Death Skinned Snapshot";
                skinnedSnapshot.MarkDynamic();
            }
            skinned.BakeMesh(skinnedSnapshot);
            mesh = skinnedSnapshot;
        }
        else
        {
            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            if (sourceFilter != null) mesh = sourceFilter.sharedMesh;
        }
        if (mesh == null || mesh.subMeshCount == 0) return false;

        // Original mesh filters are often nested under a scaled / rotated enemy.
        // Reconstruct their world transform as independent pooled objects.
        transform.SetParent(null, false);
        transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        transform.localScale = source.transform.lossyScale;
        transform.SetParent(pool.transform, true);
        meshFilter.sharedMesh = mesh;

        int count = Mathf.Max(1, mesh.subMeshCount);
        EnsureSlots(count, dissolveMaterial);

        for (int i = 0; i < count; ++i)
        {
            Material mat = (baseMaterials != null && baseMaterials.Length > 0)
                ? baseMaterials[Mathf.Min(i, baseMaterials.Length - 1)] : null;

            string texName = FindTextureProperty(mat);
            Texture mainTex = texName != null ? mat.GetTexture(texName) : Texture2D.whiteTexture;
            if (mainTex == null) mainTex = Texture2D.whiteTexture;
            Vector2 scale = texName != null ? mat.GetTextureScale(texName) : Vector2.one;
            Vector2 offset = texName != null ? mat.GetTextureOffset(texName) : Vector2.zero;

            Color tint = Color.white;
            if (mat != null)
            {
                if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");
                else if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_TintColor")) tint = mat.GetColor("_TintColor");
            }
            if (emp) tint *= empTint;

            MaterialPropertyBlock b = blocks[i];
            b.Clear();
            b.SetTexture(MainTexId, mainTex);
            b.SetVector(MainTexSTId, new Vector4(scale.x, scale.y, offset.x, offset.y));
            b.SetColor(ColorId, tint);
            b.SetFloat(UseVertexColorId,
                mesh.HasVertexAttribute(VertexAttribute.Color) ? 1f : 0f);
            b.SetColor(EdgeColorId, edgeColor);
            b.SetFloat(EdgeWidthId, edgeWidth);
            b.SetFloat(EdgeBrightnessId, brightness);
            b.SetFloat(NoiseScaleId, noiseScale);
            b.SetFloat(RiseBiasId, riseBias);
            b.SetFloat(HeightScaleId, heightScale);
            b.SetFloat(OriginYId, originY);
            b.SetFloat(SeedId, seed);
            b.SetFloat(DissolveId, 0f);
            meshRenderer.SetPropertyBlock(b, i);
        }

        owner = pool;
        elapsed = 0f;
        lifetime = Mathf.Max(0.05f, duration);
        fallSpeed = Mathf.Max(0f, worldFallSpeed);
        slotCount = count;
        playing = true;
        gameObject.SetActive(true);
        return true;
    }

    private static string FindTextureProperty(Material mat)
    {
        if (mat == null) return null;
        if (mat.HasProperty("_MainTex")) return "_MainTex";
        if (mat.HasProperty("_BaseMap")) return "_BaseMap";
        if (mat.HasProperty("_BaseTex")) return "_BaseTex";
        return null;
    }

    private void EnsureSlots(int count, Material shaderMaterial)
    {
        if (assignedMaterials == null || assignedMaterials.Length != count)
        {
            assignedMaterials = new Material[count];
            blocks = new MaterialPropertyBlock[count];
            for (int i = 0; i < count; ++i)
                blocks[i] = new MaterialPropertyBlock();
        }
        for (int i = 0; i < count; i++) assignedMaterials[i] = shaderMaterial;
        meshRenderer.sharedMaterials = assignedMaterials;
    }

    private void Update()
    {
        if (!playing) return;
        float dt = Time.deltaTime;
        elapsed += dt;
        // Only the independent flying death ghost descends. The live enemy has
        // already despawned, so grounded XP and combat behavior remain intact.
        if (fallSpeed > 0f)
            transform.position += Vector3.down * (fallSpeed * dt);
        float progress = Mathf.Clamp01(elapsed / lifetime);
        // Rapid initial flash of edge, then a smooth, complete disintegration.
        progress = progress * progress * (3f - 2f * progress);
        for (int i = 0; i < slotCount; i++)
        {
            MaterialPropertyBlock b = blocks[i];
            b.SetFloat(DissolveId, progress);
            meshRenderer.SetPropertyBlock(b, i);
        }
        if (elapsed >= lifetime)
        {
            playing = false;
            HordeDeathDissolvePool pool = owner;
            owner = null;
            if (pool != null) pool.Release(this);
            else gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (skinnedSnapshot != null) Destroy(skinnedSnapshot);
    }
}
