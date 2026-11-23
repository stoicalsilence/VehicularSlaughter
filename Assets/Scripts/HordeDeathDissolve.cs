using System;
using UnityEngine;

/// <summary>
/// No edits to HordeEnemy / HordeManager / HordeBakedAnimator are needed.
/// On a legitimate kill, copies the currently visible mesh frame to an independent
/// pooled death ghost before HordeEnemy is immediately recycled.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HordeEnemy))]
public sealed class HordeDeathDissolve : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float duration = 0.36f;
    [SerializeField, Min(0.1f)] private float eliteDurationMultiplier = 1.35f;

    [Header("Flying Enemy Deaths")]
    [Tooltip("Flying enemies use a distinct glowing edge. Ground enemies are unchanged.")]
    [SerializeField] private bool useFlyingEdgeColor = true;
    [SerializeField, ColorUsage(true, true)] private Color flyingEdge = new Color(0.26f, 0.87f, 1f, 1f);
    [SerializeField, Min(0.1f)] private float flyingDurationMultiplier = 1.10f;
    [Tooltip("How quickly the detached death visual sinks in world space. Does not move the live enemy or XP drop.")]
    [SerializeField, Min(0f)] private float flyingGhostFallSpeed = 1.2f;

    [Header("Appearance")]
    [SerializeField, Range(0.5f, 24f)] private float noiseScale = 7f;
    [SerializeField, Range(0.01f, 0.3f)] private float glowingEdgeWidth = 0.09f;
    [SerializeField, Range(0f, 8f)] private float edgeBrightness = 3f;
    [SerializeField, Range(0f, 1f)] private float upwardDissolveBias = 0.28f;
    [SerializeField, Range(0.05f, 4f)] private float heightScale = 0.65f;
    [SerializeField, ColorUsage(true, true)] private Color normalEdge = new Color(1f, 0.3f, 0.055f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color eliteEdge = new Color(1f, 0.76f, 0.12f, 1f);
    [SerializeField, ColorUsage(true, true)] private Color empEdge = new Color(0.05f, 0.8f, 1f, 1f);
    [SerializeField] private Color empBodyTint = new Color(0.25f, 0.75f, 1f, 1f);

    [Header("Integration")]
    [Tooltip("When a death ghost was successfully spawned, suppress the usual DeathParticlePool burst. Death audio, XP, crate drops and kill credit remain unchanged.")]
    [SerializeField] private bool replaceRegularDeathParticles = true;
    [Tooltip("EMP enemies normally keep their distinct explosion instead of dissolving. EMP vehicle self-detonations never invoke the kill event.")]
    [SerializeField] private bool dissolveEMPEnemies = false;
    [Tooltip("Optional meshes to ignore (e.g. separate ranged-attack charge telegraphs).")]
    [SerializeField] private Renderer[] excludeRenderers = new Renderer[0];

    private HordeEnemy enemy;
    private RendererRecord[] records;
    private DeathParticlePool suppressedParticlePool;
    private bool particlesSuppressed;

    private struct RendererRecord
    {
        public Renderer renderer;
        public Material[] baseMaterials;
    }

    private void Awake()
    {
        enemy = GetComponent<HordeEnemy>();

        // Cache the ORIGINAL normal materials. On a killing hit a different
        // white hit-flash material may be assigned to the live renderer.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        HordeRangedAttack ranged = GetComponent<HordeRangedAttack>();
        HordeFlyingAttack flying = GetComponent<HordeFlyingAttack>();
        records = new RendererRecord[renderers.Length];
        for (int i = 0; i < renderers.Length; ++i)
        {
            Renderer r = renderers[i];
            // Match HordeEnemy.CacheVariantRenderers(): warning/charge visuals
            // are not part of the body and must not leave death ghosts behind.
            if (r == null ||
                (ranged != null && ranged.IsChargeVisual(r.transform)) ||
                (flying != null && flying.IsWarningVisual(r.transform)))
                continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                continue;
            records[i].renderer = r;
            records[i].baseMaterials = r.sharedMaterials;
        }
    }

    private void OnEnable()
    {
        if (enemy == null) enemy = GetComponent<HordeEnemy>();
        if (enemy != null) enemy.KilledAtPosition += OnEnemyKilled;
    }

    private void OnDisable()
    {
        if (enemy != null) enemy.KilledAtPosition -= OnEnemyKilled;
        RestoreParticlePool();
    }

    private void OnDestroy()
    {
        RestoreParticlePool();
    }

    private void OnEnemyKilled(HordeEnemy killed, Vector3 deathPosition)
    {
        if (killed != enemy || !isActiveAndEnabled || records == null) return;
        if (killed.IsEMP && !dissolveEMPEnemies) return;

        HordeDeathDissolvePool pool = HordeDeathDissolvePool.GetOrCreate();
        if (pool == null) return;

        bool isFlying = killed.IsFlying;
        Color edge = killed.IsEMP ? empEdge : killed.IsElite ? eliteEdge :
            isFlying && useFlyingEdgeColor ? flyingEdge : normalEdge;
        float length = Mathf.Max(0.05f, duration *
            (killed.IsElite ? eliteDurationMultiplier : 1f) *
            (isFlying ? flyingDurationMultiplier : 1f));
        float fallSpeed = isFlying ? flyingGhostFallSpeed : 0f;
        float seed = UnityEngine.Random.Range(1f, 900f);
        int created = 0;

        for (int i = 0; i < records.Length; i++)
        {
            Renderer r = records[i].renderer;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || IsExcluded(r))
                continue;

            if (pool.TrySpawn(r, records[i].baseMaterials, killed.IsEMP,
                empBodyTint, deathPosition.y, seed + i * 0.173f, edge,
                length, noiseScale, glowingEdgeWidth, edgeBrightness,
                upwardDissolveBias, heightScale, fallSpeed))
                created++;
        }

        // This callback runs inside HordeEnemy.Die(), BEFORE its particle call.
        // Temporarily nulling this public field avoids changing any original script.
        // OnDisable restores it when the live enemy is returned to its pool.
        if (created > 0 && replaceRegularDeathParticles && !killed.IsEMP)
        {
            suppressedParticlePool = killed.deathParticlePool;
            particlesSuppressed = true;
            killed.deathParticlePool = null;
        }
    }

    private bool IsExcluded(Renderer candidate)
    {
        if (excludeRenderers == null) return false;
        for (int i = 0; i < excludeRenderers.Length; i++)
            if (candidate == excludeRenderers[i]) return true;
        return false;
    }

    private void RestoreParticlePool()
    {
        if (!particlesSuppressed) return;
        particlesSuppressed = false;
        if (enemy != null) enemy.deathParticlePool = suppressedParticlePool;
        suppressedParticlePool = null;
    }
}
