using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Add to a scenery prefab root (for example, a tree). An emitter on a colliding car
/// calls TriggerHologram. The component temporarily overlays existing MeshRenderers;
/// it does not replace the original scenery's materials.
/// </summary>
[DisallowMultipleComponent]
public class HologramImpactReceiver : MonoBehaviour
{
    [Header("Scenery")]
    [Tooltip("The transform containing the visual MeshRenderers. Defaults to this GameObject.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Use a material with Custom/HologramImpact_URP or Custom/HologramImpact_Builtin.")]
    [SerializeField] private Material hologramMaterial;

    [Header("Timing")]
    [Min(0.1f)] [SerializeField] private float effectDuration = 2.5f;
    [Min(0.01f)] [SerializeField] private float fadeInDuration = 0.08f;
    [Min(0.01f)] [SerializeField] private float fadeOutDuration = 0.8f;
    [Min(0f)] [SerializeField] private float retriggerCooldown = 0.3f;

    [Header("Performance")]
    [Tooltip("Maximum number of source MeshRenderers to duplicate for one impact.")]
    [Min(1)] [SerializeField] private int maxMeshRenderers = 24;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexSTId = Shader.PropertyToID("_MainTex_ST");
    private static readonly int OpacityId = Shader.PropertyToID("_HoloOpacity");
    private static readonly int ImpactPointId = Shader.PropertyToID("_ImpactPoint");
    private static readonly int ImpactStartId = Shader.PropertyToID("_ImpactStartTime");
    private static readonly int StrengthId = Shader.PropertyToID("_ImpactStrength");

    private sealed class OverlayPart
    {
        public MeshRenderer renderer;
        public MaterialPropertyBlock[] blocks;
    }

    private readonly List<OverlayPart> parts = new List<OverlayPart>();
    private float impactStartTime = float.NegativeInfinity;
    private float nextAllowedHitTime;
    private Vector3 impactPoint;
    private float impactStrength = 1f;
    private bool isActive;

    private void Awake()
    {
        if (visualRoot == null)
            visualRoot = transform;
    }

    /// <summary>Can also be called by an explosion, weapon, or gameplay event.</summary>
    public void TriggerHologram(Vector3 worldImpactPoint, float strength = 1f)
    {
        if (!isActiveAndEnabled || hologramMaterial == null || Time.time < nextAllowedHitTime)
            return;

        if (!isActive)
        {
            CreateOverlay();
            if (parts.Count == 0)
                return;
            isActive = true;
        }

        impactPoint = worldImpactPoint;
        impactStrength = Mathf.Clamp01(strength);
        impactStartTime = Time.time;
        nextAllowedHitTime = Time.time + retriggerCooldown;
        UpdateOverlay(0f);
    }

    private void Update()
    {
        if (!isActive)
            return;

        float elapsed = Time.time - impactStartTime;
        if (elapsed >= effectDuration)
        {
            ClearOverlay();
            return;
        }

        float fadeIn = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeInDuration));
        float fadeOut = Mathf.Clamp01((effectDuration - elapsed) / Mathf.Max(0.01f, fadeOutDuration));
        UpdateOverlay(fadeIn * fadeOut);
    }

    private void CreateOverlay()
    {
        Transform root = visualRoot != null ? visualRoot : transform;
        MeshRenderer[] sources = root.GetComponentsInChildren<MeshRenderer>(false);
        int count = 0;

        foreach (MeshRenderer source in sources)
        {
            if (count >= maxMeshRenderers)
                break;
            if (!source.enabled || !source.gameObject.activeInHierarchy)
                continue;

            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                continue;

            GameObject overlayObject = new GameObject("Hologram Impact Overlay");
            overlayObject.layer = source.gameObject.layer;
            overlayObject.transform.SetParent(source.transform, false);
            overlayObject.transform.localPosition = Vector3.zero;
            overlayObject.transform.localRotation = Quaternion.identity;
            overlayObject.transform.localScale = Vector3.one;

            MeshFilter filter = overlayObject.AddComponent<MeshFilter>();
            filter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.lightProbeUsage = LightProbeUsage.Off;
            overlayRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            overlayRenderer.sortingLayerID = source.sortingLayerID;
            overlayRenderer.sortingOrder = source.sortingOrder + 1;

            Material[] originals = source.sharedMaterials;
            int slotCount = Mathf.Max(1, originals.Length);
            Material[] overlays = new Material[slotCount];
            MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[slotCount];

            for (int slot = 0; slot < slotCount; slot++)
            {
                overlays[slot] = hologramMaterial;
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                Material original = slot < originals.Length ? originals[slot] : null;
                CopyTextureSettings(original, block);
                blocks[slot] = block;
            }

            overlayRenderer.sharedMaterials = overlays;
            parts.Add(new OverlayPart { renderer = overlayRenderer, blocks = blocks });
            count++;
        }
    }

    private static void CopyTextureSettings(Material original, MaterialPropertyBlock block)
    {
        if (original == null)
            return;

        string textureName = original.HasProperty("_BaseMap") ? "_BaseMap" :
                             original.HasProperty("_MainTex") ? "_MainTex" : null;
        if (textureName == null)
            return;

        Texture texture = original.GetTexture(textureName);
        if (texture != null)
            block.SetTexture(MainTexId, texture);

        Vector2 scale = original.GetTextureScale(textureName);
        Vector2 offset = original.GetTextureOffset(textureName);
        block.SetVector(MainTexSTId, new Vector4(scale.x, scale.y, offset.x, offset.y));
    }

    private void UpdateOverlay(float opacity)
    {
        Vector4 point = new Vector4(impactPoint.x, impactPoint.y, impactPoint.z, 1f);
        foreach (OverlayPart part in parts)
        {
            if (part.renderer == null)
                continue;

            for (int slot = 0; slot < part.blocks.Length; slot++)
            {
                MaterialPropertyBlock block = part.blocks[slot];
                block.SetFloat(OpacityId, opacity);
                block.SetVector(ImpactPointId, point);
                block.SetFloat(ImpactStartId, impactStartTime);
                block.SetFloat(StrengthId, impactStrength);
                part.renderer.SetPropertyBlock(block, slot);
            }
        }
    }

    private void ClearOverlay()
    {
        foreach (OverlayPart part in parts)
        {
            if (part.renderer != null)
                Destroy(part.renderer.gameObject);
        }
        parts.Clear();
        isActive = false;
    }

    private void OnDisable()
    {
        ClearOverlay();
    }
}
