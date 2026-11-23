using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lightweight, Muck-inspired low-poly grass for a Unity Terrain.
/// A small pool of combined meshes follows the player; there are no blade GameObjects,
/// colliders, per-blade Update calls, or raycasts.
///
/// Add to the same GameObject as TerrainGenerator. Assign Player and a material using
/// Custom/MuckGrassURP or Custom/MuckGrassBuiltIn.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class MuckGrass : MonoBehaviour
{
    [Header("References")]
    public TerrainGenerator generator;
    public Terrain targetTerrain;
    public Transform player;
    public Material grassMaterial;

    [Header("Streaming / Performance")]
    [Min(4)] public int chunkSize = 14;
    [Range(1, 5)] public int chunkRadius = 3;
    [Range(0.1f, 4f)] public float tuftsPerSquareMeter = 0.95f;
    [Range(1, 4)] public int bladesPerTuft = 3;
    [Range(1, 32)] public int chunkBuildsPerFrame = 8;
    public int grassSeed = 713;

    [Header("Shape")]
    public Vector2 bladeHeight = new Vector2(0.27f, 0.57f);
    public Vector2 bladeWidth = new Vector2(0.10f, 0.23f);
    [Range(0f, 0.6f)] public float bladeLean = 0.28f;
    [Range(0f, 60f)] public float maximumSlope = 35f;
    [Min(0f)] public float terrainEdgePadding = 2f;

    [Header("Appearance")]
    public Color darkerGrass = new Color(0.13f, 0.32f, 0.08f, 1f);
    public Color lighterGrass = new Color(0.34f, 0.57f, 0.17f, 1f);

    [Header("Terrain-Aware Color & Density")]
    [Tooltip("Blend grass color and density with the Terrain Layers underneath. Your existing grass colors and material tint remain the base palette.")]
    public bool terrainAwareGrass = true;

    [Tooltip("0 = unchanged grass colors; 1 = full layer-based color tint.")]
    [Range(0f, 1f)] public float terrainColorMatchStrength = 0.75f;

    [Tooltip("0 = no change in grass density; 1 = full layer-based density variation.")]
    [Range(0f, 1f)] public float terrainDensityMatchStrength = 0.65f;

    [Tooltip("Multiplicative color tint for Terrain Layer 0, your first green texture. White preserves the existing grass look.")]
    public Color terrainLayer0Tint = Color.white;

    [Tooltip("Multiplicative color tint for Terrain Layer 1, your second green/brown texture.")]
    public Color terrainLayer1Tint = new Color(1.12f, 1.04f, 0.88f, 1f);

    [Tooltip("Multiplicative color tint for Terrain Layer 2, your reddish/brown texture. Adds subtle dry olive tones without turning the grass red.")]
    public Color terrainLayer2Tint = new Color(1.28f, 1.03f, 0.77f, 1f);

    [Tooltip("Relative density on Terrain Layer 0 (1 = no change).")]
    [Range(0f, 1.5f)] public float terrainLayer0Density = 1f;

    [Tooltip("Relative density on Terrain Layer 1 (1 = no change).")]
    [Range(0f, 1.5f)] public float terrainLayer1Density = 0.96f;

    [Tooltip("Relative density on Terrain Layer 2. Lower means more visible reddish soil.")]
    [Range(0f, 1.5f)] public float terrainLayer2Density = 0.72f;

    [Header("Natural Patches")]
    [Tooltip("Larger values produce broader areas of dense and sparse grass.")]
    [Min(1f)] public float patchScale = 11f;
    [Tooltip("0 = even distribution. 1 = strong clumps and clearings.")]
    [Range(0f, 1f)] public float patchVariation = 0.85f;

    [Header("First-Person Visibility")]
    [Tooltip("Grass is full height past Near Fade End, and flattened inside Near Fade Start.")]
    [Min(0f)] public float nearFadeStart = 0.35f;
    [Min(0f)] public float nearFadeEnd = 1.55f;
    [Range(0.1f, 20f)] public float fadeBand = 8f;
    [Range(0f, 0.3f)] public float windStrength = 0.05f;
    [Range(0f, 8f)] public float windSpeed = 1.8f;
    [Range(0f, 2f)] public float windSpatialFrequency = 0.23f;

    [Header("Optional Placement Filters")]
    [Tooltip("-1 = allow all terrain textures; otherwise the TerrainLayer index to grow on.")]
    public int grassTerrainLayerIndex = -1;
    [Range(0.01f, 1f)] public float minimumGrassLayerWeight = 0.45f;
    [Tooltip("Only include obstacle layers here. Keep 0 for the cheapest option. Exclude the Terrain layer.")]
    public LayerMask obstacleLayers = 0;
    [Range(0.1f, 1.5f)] public float obstacleCheckRadius = 0.4f;

    private sealed class GrassChunk
    {
        public GameObject gameObject;
        public Mesh mesh;
        public MeshRenderer renderer;
        public Vector2Int coordinate;
        public readonly List<Vector3> vertices = new List<Vector3>(2048);
        public readonly List<Color> colors = new List<Color>(2048);
        public readonly List<Vector2> uvs = new List<Vector2>(2048);
        public readonly List<int> triangles = new List<int>(2048);
    }

    private readonly Dictionary<Vector2Int, GrassChunk> active =
        new Dictionary<Vector2Int, GrassChunk>();
    private readonly Stack<GrassChunk> recycled = new Stack<GrassChunk>();
    private readonly List<Vector2Int> stale = new List<Vector2Int>();
    private readonly List<Vector2Int> pending = new List<Vector2Int>();

    private int pendingIndex;
    private Vector2Int lastCenter = new Vector2Int(int.MinValue, int.MinValue);
    private Material runtimeMaterial;
    private Terrain initializedTerrain;
    private float[,,] cachedAlphamaps;
    private int cachedAlphamapResolution;

    private static readonly int PlayerPositionId = Shader.PropertyToID("_PlayerPosition");
    private static readonly int NearFadeStartId = Shader.PropertyToID("_NearFadeStart");
    private static readonly int NearFadeEndId = Shader.PropertyToID("_NearFadeEnd");
    private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
    private static readonly int FadeEndId = Shader.PropertyToID("_FadeEnd");
    private static readonly int WindStrengthId = Shader.PropertyToID("_WindStrength");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindScaleId = Shader.PropertyToID("_WindScale");

    private void Start()
    {
        // TerrainGenerator.Start has normally run by now (execution order 200).
        TryInitialize();
    }

    private void Update()
    {
        if (!TryInitialize()) return;

        Vector3 playerPosition = player.position;
        float size = Mathf.Max(4, chunkSize);
        Vector3 local = playerPosition - targetTerrain.transform.position;
        Vector2Int center = new Vector2Int(
            Mathf.FloorToInt(local.x / size),
            Mathf.FloorToInt(local.z / size));

        if (center != lastCenter)
            UpdateStreamingWindow(center);

        // The shader gently shrinks blades to zero near the player's visibility radius.
        float renderRadius = Mathf.Max(1f, (Mathf.Max(1, chunkRadius) - 0.2f) * size);
        runtimeMaterial.SetVector(PlayerPositionId, playerPosition);
        runtimeMaterial.SetFloat(NearFadeStartId, Mathf.Max(0f, nearFadeStart));
        runtimeMaterial.SetFloat(NearFadeEndId, Mathf.Max(nearFadeStart + 0.01f, nearFadeEnd));
        runtimeMaterial.SetFloat(FadeStartId, Mathf.Max(0f, renderRadius - fadeBand));
        runtimeMaterial.SetFloat(FadeEndId, renderRadius);
        runtimeMaterial.SetFloat(WindStrengthId, windStrength);
        runtimeMaterial.SetFloat(WindSpeedId, windSpeed);
        runtimeMaterial.SetFloat(WindScaleId, windSpatialFrequency);

        int budget = Mathf.Max(1, chunkBuildsPerFrame);
        while (budget-- > 0 && pendingIndex < pending.Count)
        {
            Vector2Int coordinate = pending[pendingIndex++];
            if (active.ContainsKey(coordinate)) continue;

            GrassChunk chunk = recycled.Count > 0 ? recycled.Pop() : MakeChunk();
            chunk.coordinate = coordinate;
            active.Add(coordinate, chunk);
            BuildChunk(chunk);
        }

        // Entire chunks outside the circular draw radius are not submitted to the GPU.
        foreach (GrassChunk chunk in active.Values)
        {
            if (!chunk.gameObject.activeSelf) continue;
            float cx = (chunk.coordinate.x + 0.5f) * size;
            float cz = (chunk.coordinate.y + 0.5f) * size;
            float dx = Mathf.Max(0f, Mathf.Abs(local.x - cx) - size * 0.5f);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z - cz) - size * 0.5f);
            float cullRadius = renderRadius + 1f;
            chunk.renderer.enabled = dx * dx + dz * dz < cullRadius * cullRadius;
        }
    }

    /// <summary>
    /// Call after TerrainGenerator.GenerateNow() changes the heightmap or textures.
    /// All chunks will be reused and rebuilt using the latest terrain data.
    /// </summary>
    public void RefreshGrass()
    {
        if (generator == null) generator = GetComponent<TerrainGenerator>();
        if (generator != null && generator.terrain != null)
            targetTerrain = generator.terrain;

        initializedTerrain = null;
        cachedAlphamaps = null;
        lastCenter = new Vector2Int(int.MinValue, int.MinValue);
        pending.Clear();
        pendingIndex = 0;

        foreach (GrassChunk chunk in active.Values)
        {
            chunk.gameObject.SetActive(false);
            recycled.Push(chunk);
        }
        active.Clear();
    }

    private bool TryInitialize()
    {
        if (generator == null) generator = GetComponent<TerrainGenerator>();
        if (targetTerrain == null && generator != null)
            targetTerrain = generator.terrain;
        if (player == null && Camera.main != null)
            player = Camera.main.transform; // Convenience fallback; assign the player for precise placement.

        if (targetTerrain == null || targetTerrain.terrainData == null ||
            player == null || grassMaterial == null)
            return false;

        if (targetTerrain != initializedTerrain)
        {
            RefreshGrass();
            initializedTerrain = targetTerrain;
            UpdateAlphamapCache();
        }

        if (runtimeMaterial == null)
        {
            runtimeMaterial = new Material(grassMaterial);
            runtimeMaterial.name = grassMaterial.name + " (MuckGrass Runtime)";
        }
        return true;
    }

    /// <summary>
    /// Existing MuckGrass components retain their serialized Inspector values after
    /// a script update. Run this from the component context menu to apply the v2 look.
    /// Existing references, terrain filters, seed and render distances are preserved.
    /// </summary>
    [ContextMenu("Apply Softer Grass Preset (V2)")]
    public void ApplySofterGrassPreset()
    {
        tuftsPerSquareMeter = 0.95f;
        bladesPerTuft = 3;
        bladeHeight = new Vector2(0.27f, 0.57f);
        bladeWidth = new Vector2(0.10f, 0.23f);
        bladeLean = 0.28f;
        darkerGrass = new Color(0.13f, 0.32f, 0.08f, 1f);
        lighterGrass = new Color(0.34f, 0.57f, 0.17f, 1f);
        patchScale = 11f;
        patchVariation = 0.85f;
        nearFadeStart = 0.35f;
        nearFadeEnd = 1.55f;
        RefreshGrass();
    }

    /// <summary>
    /// Rebuilds pooled grass meshes and resamples the latest terrain texture weights.
    /// Useful after adjusting terrain splatmaps during Play Mode.
    /// </summary>
    [ContextMenu("Rebuild Grass From Terrain (Play Mode)")]
    public void RebuildGrassFromTerrain()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("MuckGrass: Rebuild grass in Play Mode, after terrain generation.", this);
            return;
        }
        RefreshGrass();
    }

    private void UpdateAlphamapCache()
    {
        cachedAlphamaps = null;
        cachedAlphamapResolution = 0;
        // Also cache when terrain-aware coloring is enabled, even if no
        // placement layer filter is selected (-1).
        if (!terrainAwareGrass && grassTerrainLayerIndex < 0) return;

        TerrainData data = targetTerrain.terrainData;
        if (grassTerrainLayerIndex >= data.alphamapLayers)
        {
            Debug.LogWarning("MuckGrass: Grass Terrain Layer Index is out of range; no grass will be generated.", this);
            return;
        }

        cachedAlphamapResolution = data.alphamapResolution;
        cachedAlphamaps = data.GetAlphamaps(0, 0,
            cachedAlphamapResolution, cachedAlphamapResolution);
    }

    private void UpdateStreamingWindow(Vector2Int center)
    {
        lastCenter = center;
        int radius = Mathf.Max(1, chunkRadius);

        stale.Clear();
        foreach (KeyValuePair<Vector2Int, GrassChunk> entry in active)
        {
            if (Mathf.Abs(entry.Key.x - center.x) > radius ||
                Mathf.Abs(entry.Key.y - center.y) > radius)
                stale.Add(entry.Key);
        }

        for (int i = 0; i < stale.Count; i++)
        {
            GrassChunk chunk = active[stale[i]];
            active.Remove(stale[i]);
            chunk.gameObject.SetActive(false);
            recycled.Push(chunk);
        }

        pending.Clear();
        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int coord = center + new Vector2Int(x, z);
                if (!active.ContainsKey(coord)) pending.Add(coord);
            }
        }
        // Near chunks are filled first, so grass appears close to the player immediately.
        pending.Sort((a, b) =>
        {
            int da = (a.x - center.x) * (a.x - center.x) +
                     (a.y - center.y) * (a.y - center.y);
            int db = (b.x - center.x) * (b.x - center.x) +
                     (b.y - center.y) * (b.y - center.y);
            return da.CompareTo(db);
        });
        pendingIndex = 0;
    }

    private GrassChunk MakeChunk()
    {
        GrassChunk chunk = new GrassChunk();
        chunk.gameObject = new GameObject("__GrassChunk");
        chunk.gameObject.transform.SetParent(targetTerrain.transform, false);
        chunk.gameObject.layer = targetTerrain.gameObject.layer;

        MeshFilter filter = chunk.gameObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = chunk.gameObject.AddComponent<MeshRenderer>();
        chunk.renderer = renderer;
        chunk.mesh = new Mesh { name = "Muck Grass Chunk" };
        chunk.mesh.MarkDynamic();
        filter.sharedMesh = chunk.mesh;
        renderer.sharedMaterial = runtimeMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return chunk;
    }

    private void BuildChunk(GrassChunk chunk)
    {
        int size = Mathf.Max(4, chunkSize);
        chunk.gameObject.transform.SetParent(targetTerrain.transform, false);
        chunk.gameObject.transform.localPosition = new Vector3(
            chunk.coordinate.x * size, 0f, chunk.coordinate.y * size);
        chunk.gameObject.name = "__Grass_" + chunk.coordinate.x + "_" + chunk.coordinate.y;

        chunk.vertices.Clear();
        chunk.colors.Clear();
        chunk.uvs.Clear();
        chunk.triangles.Clear();

        TerrainData data = targetTerrain.terrainData;
        Vector3 terrainOrigin = targetTerrain.transform.position;
        Vector3 terrainSize = data.size;
        float edge = Mathf.Max(0f, terrainEdgePadding);

        int count = Mathf.RoundToInt(size * size * Mathf.Max(0.1f, tuftsPerSquareMeter));
        int hash = unchecked(grassSeed ^ (chunk.coordinate.x * 73856093) ^
                             (chunk.coordinate.y * 19349663));
        System.Random rng = new System.Random(hash);

        float minH = Mathf.Max(0.02f, Mathf.Min(bladeHeight.x, bladeHeight.y));
        float maxH = Mathf.Max(minH, Mathf.Max(bladeHeight.x, bladeHeight.y));
        float minW = Mathf.Max(0.01f, Mathf.Min(bladeWidth.x, bladeWidth.y));
        float maxW = Mathf.Max(minW, Mathf.Max(bladeWidth.x, bladeWidth.y));
        int blades = Mathf.Clamp(bladesPerTuft, 1, 4);

        for (int i = 0; i < count; i++)
        {
            float x = (float)rng.NextDouble() * size;
            float z = (float)rng.NextDouble() * size;
            float terrainX = chunk.coordinate.x * size + x;
            float terrainZ = chunk.coordinate.y * size + z;

            if (terrainX < edge || terrainZ < edge ||
                terrainX > terrainSize.x - edge || terrainZ > terrainSize.z - edge)
                continue;

            float nx = terrainX / terrainSize.x;
            float nz = terrainZ / terrainSize.z;
            if (data.GetSteepness(nx, nz) > maximumSlope) continue;
            if (!IsAllowedTexture(nx, nz)) continue;

            // Stable world-space patchiness. No moving patterns or individual objects.
            // Perlin sampling only happens when a chunk is (re)built, never per frame.
            float patch = Mathf.PerlinNoise(
                (terrainX + grassSeed * 0.1297f) / Mathf.Max(1f, patchScale),
                (terrainZ - grassSeed * 0.1793f) / Mathf.Max(1f, patchScale));
            float patchDensity = Mathf.Lerp(1f,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.70f, patch)),
                patchVariation);

            // Sample Terrain Layer blend only when building a chunk, not per frame.
            // Layer 0 stays green; layer 1 becomes more olive; layer 2 is drier.
            // Interpolating weights prevents abrupt changes at texture boundaries.
            Color terrainTint = Color.white;
            float terrainDensity = 1f;
            if (terrainAwareGrass)
                SampleTerrainAppearance(nx, nz, out terrainTint, out terrainDensity);

            patchDensity *= Mathf.Lerp(1f, terrainDensity,
                Mathf.Clamp01(terrainDensityMatchStrength));
            if ((float)rng.NextDouble() > patchDensity) continue;

            Vector3 worldGround = new Vector3(
                terrainOrigin.x + terrainX, terrainOrigin.y, terrainOrigin.z + terrainZ);
            float y = targetTerrain.SampleHeight(worldGround);
            if (obstacleLayers.value != 0 && Physics.CheckSphere(
                    worldGround + Vector3.up * (y + 0.25f), obstacleCheckRadius,
                    obstacleLayers, QueryTriggerInteraction.Ignore))
                continue;

            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            Color tuft = Color.Lerp(darkerGrass, lighterGrass,
                0.1f + 0.8f * (float)rng.NextDouble());
            Color effectiveTint = Color.Lerp(Color.white, terrainTint,
                Mathf.Clamp01(terrainColorMatchStrength));

            for (int blade = 0; blade < blades; blade++)
            {
                float dir = angle + blade * (Mathf.PI * 2f / blades) +
                            ((float)rng.NextDouble() - 0.5f) * 0.45f;
                float height = Mathf.Lerp(minH, maxH, (float)rng.NextDouble());
                // Slightly shorter blades at patch edges soften the transitions.
                height *= Mathf.Lerp(0.72f, 1f, patchDensity);
                float width = Mathf.Lerp(minW, maxW, (float)rng.NextDouble());
                float offset = (float)rng.NextDouble() * 0.07f;
                Color bladeColor = Color.Lerp(tuft, darkerGrass,
                    (float)rng.NextDouble() * 0.15f);
                bladeColor.r *= effectiveTint.r;
                bladeColor.g *= effectiveTint.g;
                bladeColor.b *= effectiveTint.b;
                AddBlade(chunk, x, y, z, dir, height, width,
                    Mathf.Max(0f, bladeLean), offset, bladeColor);
            }
        }

        chunk.mesh.Clear();
        if (chunk.vertices.Count == 0)
        {
            chunk.gameObject.SetActive(false);
            return;
        }

        chunk.mesh.indexFormat = chunk.vertices.Count > 65535
            ? IndexFormat.UInt32 : IndexFormat.UInt16;
        chunk.mesh.SetVertices(chunk.vertices);
        chunk.mesh.SetColors(chunk.colors);
        chunk.mesh.SetUVs(0, chunk.uvs);
        chunk.mesh.SetTriangles(chunk.triangles, 0);
        chunk.mesh.RecalculateBounds();
        // Keep wind-animated vertices within mesh bounds for correct frustum culling.
        Bounds bounds = chunk.mesh.bounds;
        bounds.Expand(new Vector3(1f, maxH * 2f + 1f, 1f));
        chunk.mesh.bounds = bounds;
        chunk.gameObject.SetActive(true);
    }

    /// <summary>
    /// Bilinearly samples up to the first three Terrain Layer weights (splatmap).
    /// Additional Terrain Layers, if present, use a neutral white tint/density.
    /// No allocations, material copies or extra draw calls per grass blade.
    /// </summary>
    private void SampleTerrainAppearance(float x, float z, out Color tint,
        out float density)
    {
        tint = Color.white;
        density = 1f;
        if (cachedAlphamaps == null || cachedAlphamapResolution < 1) return;

        float px = Mathf.Clamp01(x) * (cachedAlphamapResolution - 1);
        float pz = Mathf.Clamp01(z) * (cachedAlphamapResolution - 1);
        int x0 = Mathf.FloorToInt(px);
        int z0 = Mathf.FloorToInt(pz);
        int x1 = Mathf.Min(x0 + 1, cachedAlphamapResolution - 1);
        int z1 = Mathf.Min(z0 + 1, cachedAlphamapResolution - 1);
        float fx = px - x0;
        float fz = pz - z0;

        float w0 = InterpolatedLayerWeight(0, x0, x1, z0, z1, fx, fz);
        float w1 = InterpolatedLayerWeight(1, x0, x1, z0, z1, fx, fz);
        float w2 = InterpolatedLayerWeight(2, x0, x1, z0, z1, fx, fz);
        float other = Mathf.Max(0f, 1f - w0 - w1 - w2);

        // These are MULTIPLIERS for your custom dark/light colors (not replacements).
        tint = terrainLayer0Tint * w0 + terrainLayer1Tint * w1 +
               terrainLayer2Tint * w2 + Color.white * other;
        tint.a = 1f;
        density = terrainLayer0Density * w0 + terrainLayer1Density * w1 +
                  terrainLayer2Density * w2 + other;
    }

    private float InterpolatedLayerWeight(int layer, int x0, int x1,
        int z0, int z1, float fx, float fz)
    {
        if (layer >= cachedAlphamaps.GetLength(2)) return 0f;
        float bottom = Mathf.Lerp(cachedAlphamaps[z0, x0, layer],
                                  cachedAlphamaps[z0, x1, layer], fx);
        float top = Mathf.Lerp(cachedAlphamaps[z1, x0, layer],
                               cachedAlphamaps[z1, x1, layer], fx);
        return Mathf.Lerp(bottom, top, fz);
    }

    private bool IsAllowedTexture(float x, float z)
    {
        if (grassTerrainLayerIndex < 0) return true;
        if (cachedAlphamaps == null || cachedAlphamapResolution <= 0 ||
            grassTerrainLayerIndex >= cachedAlphamaps.GetLength(2)) return false;

        int px = Mathf.Clamp(Mathf.RoundToInt(x * (cachedAlphamapResolution - 1)),
            0, cachedAlphamapResolution - 1);
        int pz = Mathf.Clamp(Mathf.RoundToInt(z * (cachedAlphamapResolution - 1)),
            0, cachedAlphamapResolution - 1);
        return cachedAlphamaps[pz, px, grassTerrainLayerIndex] >= minimumGrassLayerWeight;
    }

    private static void AddBlade(GrassChunk chunk, float x, float y, float z,
        float angle, float height, float width, float lean, float offset, Color color)
    {
        float dx = Mathf.Cos(angle);
        float dz = Mathf.Sin(angle);
        float tx = -dz * width * 0.5f;
        float tz = dx * width * 0.5f;
        float bx = x + dx * offset;
        float bz = z + dz * offset;
        int start = chunk.vertices.Count;

        // A single double-sided triangle gives the broad, pointed silhouette of Muck.
        // There are no normals, bones, textures, or duplicate rear-facing triangles.
        chunk.vertices.Add(new Vector3(bx - tx, y - 0.02f, bz - tz));
        chunk.vertices.Add(new Vector3(bx + tx, y - 0.02f, bz + tz));
        chunk.vertices.Add(new Vector3(bx + dx * height * lean, y + height,
            bz + dz * height * lean));

        Color bottom = new Color(color.r * 0.70f, color.g * 0.75f, color.b * 0.70f, 1f);
        chunk.colors.Add(bottom);
        chunk.colors.Add(bottom);
        chunk.colors.Add(color);

        // uv.x = 0 at base / 1 at tip; uv.y = blade height for GPU distance shrink.
        chunk.uvs.Add(new Vector2(0f, height));
        chunk.uvs.Add(new Vector2(0f, height));
        chunk.uvs.Add(new Vector2(1f, height));
        chunk.triangles.Add(start);
        chunk.triangles.Add(start + 1);
        chunk.triangles.Add(start + 2);
    }

    private void OnDisable()
    {
        ReleaseAll();
    }

    private void OnDestroy()
    {
        ReleaseAll();
    }

    private void ReleaseAll()
    {
        foreach (GrassChunk chunk in active.Values) DestroyChunk(chunk);
        foreach (GrassChunk chunk in recycled) DestroyChunk(chunk);
        active.Clear();
        recycled.Clear();
        stale.Clear();
        pending.Clear();
        pendingIndex = 0;
        lastCenter = new Vector2Int(int.MinValue, int.MinValue);
        cachedAlphamaps = null;
        initializedTerrain = null;
        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
            runtimeMaterial = null;
        }
    }

    private void DestroyChunk(GrassChunk chunk)
    {
        if (chunk.mesh != null) Destroy(chunk.mesh);
        if (chunk.gameObject != null) Destroy(chunk.gameObject);
    }
}
