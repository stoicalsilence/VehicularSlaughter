using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates a terrain from layered Perlin noise, then scatters tree prefabs and
/// points of interest across its surface.
///
/// Add this component to an empty GameObject. By default, generation happens
/// when entering Play Mode. Keep the generator GameObject's scale at (1, 1, 1).
/// </summary>
[DisallowMultipleComponent]
public class TerrainGenerator : MonoBehaviour
{
    [Serializable]
    public class PointOfInterest
    {
        [Tooltip("Optional label to help identify this entry in the Inspector.")]
        public string label = "POI";

        public GameObject prefab;

        [Min(0)]
        public int amount = 1;

        [Tooltip("Tilt this prefab to follow the terrain normal. Usually leave this off for buildings.")]
        public bool alignToTerrain = false;

        [Tooltip("Give each instance a random rotation around the vertical axis.")]
        public bool randomYaw = true;
    }

    [Header("Terrain")]
    [Tooltip("Optional existing Terrain to modify. Leave empty to create one automatically.")]
    public Terrain terrain;

    [Tooltip("Create a Terrain if the Terrain field is empty.")]
    public bool createTerrainIfMissing = true;

    [Tooltip("Terrain dimensions in world units (X = width, Y = height, Z = length).")]
    public Vector3 terrainSize = new Vector3(500f, 60f, 500f);

    [Tooltip("The script converts this to a valid Unity heightmap resolution (power of two + 1).")]
    [Range(33, 2049)]
    public int heightmapResolution = 513;

    [Tooltip("Average terrain elevation as a fraction of Terrain height.")]
    [Range(0f, 0.8f)]
    public float baseHeight = 0.12f;

    [Tooltip("0 = flat. Increase this for stronger hills. The default is intentionally gentle.")]
    [Range(0f, 0.6f)]
    public float hilliness = 0.18f;

    [Tooltip("Approximate world-space size of the broad noise features. Larger values make wider hills.")]
    [Min(1f)]
    public float noiseScale = 160f;

    [Range(1, 6)]
    public int noiseOctaves = 4;

    [Range(0.1f, 0.9f)]
    public float persistence = 0.5f;

    [Min(1f)]
    public float lacunarity = 2f;

    [Tooltip("Blend the terrain back toward baseHeight at the outer edges.")]
    public bool flattenEdges = true;

    [Range(0.01f, 0.25f)]
    public float edgeBlendWidth = 0.08f;

    [Header("Generation")]
    [Tooltip("Generate automatically when entering Play Mode.")]
    public bool generateOnStart = true;

    [Tooltip("The same seed produces the same terrain and placement layout.")]
    public int seed = 12345;

    [Tooltip("Choose a new seed each time Play Mode starts.")]
    public bool randomizeSeedOnStart = false;

    [Header("Trees")]
    [Tooltip("Tree prefabs are selected randomly from this list. Empty entries are ignored.")]
    public GameObject[] treePrefabs;

    [Min(0)]
    [Tooltip("Total number of tree instances to try to place, distributed across the tree prefab list.")]
    public int treeCount = 300;

    [Header("Points of Interest")]
    [Tooltip("Each entry has its own prefab and amount. POIs are placed before trees to give them priority.")]
    public PointOfInterest[] pointsOfInterest;

    [Header("Placement Rules")]
    [Min(0f)]
    [Tooltip("Minimum spacing between all spawned trees and POIs, in world units.")]
    public float minimumSpacing = 4f;

    [Range(0f, 60f)]
    [Tooltip("Objects won't be placed on slopes steeper than this angle.")]
    public float maxPlacementSlope = 35f;

    [Min(1)]
    [Tooltip("Random location attempts per object before skipping it. Increase this for dense placement.")]
    public int attemptsPerObject = 40;

    [Range(0f, 0.2f)]
    [Tooltip("Keep objects this fraction of the terrain width/length away from the outer border.")]
    public float placementEdgeMargin = 0.03f;

    [SerializeField, HideInInspector]
    private Transform generatedObjectsRoot;

    [SerializeField, HideInInspector]
    private GameObject generatedTerrainObject;

    private readonly List<Vector3> occupiedPositions = new List<Vector3>();

    [Header("Terrain Textures")]
    [Tooltip("Terrain Layers distributed across the terrain using noise.")]
    public TerrainLayer[] terrainLayers;

    [Min(1f)]
    [Tooltip("World-space size of texture patches. Larger values create larger patches.")]
    public float textureNoiseScale = 90f;

    [Header("Map Boundary")]
    public bool createBoundaryWalls = true;

    [Min(0.1f)]
    public float boundaryThickness = 2f;

    [Min(0f)]
    public float boundaryInset = 1f;

    [Min(0f)]
    public float boundaryVerticalPadding = 10f;

    [SerializeField, HideInInspector]
    private Transform generatedBoundaryRoot;

    [Header("Visual Map Border")]

    public bool generateVisualBorder = true;

    public Material visualBorderMaterial;

    [Min(1f)]
    public float visualBorderHeight = 18f;

    [Min(0f)]
    public float visualBorderHeightVariation = 5f;

    [Min(1f)]
    public float visualBorderThickness = 12f;

    [Min(0f)]
    public float visualBorderDepth = 15f;

    [Min(0f)]
    public float visualBorderOverlap = 0.5f;

    [Min(1f)]
    public float visualBorderSegmentLength = 4f;

    [Min(1f)]
    public float visualBorderNoiseScale = 30f;

    [Min(0f)]
    public float visualBorderFaceRoughness = 1.5f;

    [Min(0.1f)]
    public float visualBorderTextureWorldSize = 5f;

    [SerializeField, HideInInspector]
    private Transform generatedVisualBorderRoot;

    private void Start()
    {
        if (generateOnStart)
        {
            GenerateNow();
        }
    }

    /// <summary>
    /// Generates terrain heights and then spawns POIs followed by trees.
    /// Intended to be called in Play Mode.
    /// </summary>
    [ContextMenu("Generate Now (Play Mode)")]
    public void GenerateNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("TerrainGenerator: Enter Play Mode to generate. Generation is runtime-based so it doesn't require editor-only scripts.", this);
            return;
        }

        if (randomizeSeedOnStart)
        {
            seed = UnityEngine.Random.Range(-1000000, 1000001);
        }

        if (!EnsureTerrain())
        {
            return;
        }

        EnsureGeneratedObjectsRoot();
        ClearSpawnedObjects();
        occupiedPositions.Clear();

        System.Random placementRandom = new System.Random(seed);
        System.Random noiseRandom = new System.Random(unchecked(seed ^ 0x5F3759DF));

        GenerateHeightmap(noiseRandom);

        if (createBoundaryWalls)
            GenerateBoundaryWalls();
        else
            ClearBoundaryWalls();

        // Generate noise-based terrain textures using a separate, deterministic seed.
        System.Random textureRandom =
            new System.Random(unchecked(seed ^ 0x2C9277B5));

        GenerateTerrainTextures(textureRandom);

        terrain.Flush();

        if (generateVisualBorder)
            GenerateVisualBorder();
        else
            ClearVisualBorder();

        int poiPlaced = SpawnPointsOfInterest(placementRandom);
        int treesPlaced = SpawnTrees(placementRandom);

        Debug.Log(
            "TerrainGenerator: Generated terrain with seed " + seed + ". Placed " +
            poiPlaced + " POI(s) and " + treesPlaced + " tree(s).",
            this);
    }

    private void GenerateTerrainTextures(System.Random rng)
    {
        TerrainData data = terrain.terrainData;

        // Keep only valid Terrain Layer references.
        List<TerrainLayer> validLayers = new List<TerrainLayer>();

        if (terrainLayers != null)
        {
            for (int i = 0; i < terrainLayers.Length; i++)
            {
                if (terrainLayers[i] != null)
                {
                    validLayers.Add(terrainLayers[i]);
                }
            }
        }

        // Leave existing terrain textures unchanged if none are assigned.
        if (validLayers.Count == 0)
        {
            Debug.LogWarning(
                "TerrainGenerator: Assign at least one Terrain Layer to generate textures.",
                this);
            return;
        }

        data.terrainLayers = validLayers.ToArray();

        int resolution = data.alphamapResolution;
        int layerCount = validLayers.Count;

        // Alphamap dimensions are [z, x, layer].
        float[,,] alphamaps =
            new float[resolution, resolution, layerCount];

        // One layer means the entire terrain uses that texture.
        if (layerCount == 1)
        {
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    alphamaps[z, x, 0] = 1f;
                }
            }

            data.SetAlphamaps(0, 0, alphamaps);
            return;
        }

        Vector3 size = data.size;

        // Separate offsets give the texture pattern its own noise layout.
        float offsetX = rng.Next(-100000, 100000);
        float offsetZ = rng.Next(-100000, 100000);

        float safeScale = Mathf.Max(1f, textureNoiseScale);

        for (int z = 0; z < resolution; z++)
        {
            float worldZ = z / (float)(resolution - 1) * size.z;

            for (int x = 0; x < resolution; x++)
            {
                float worldX = x / (float)(resolution - 1) * size.x;

                // Reuse the existing layered Perlin noise function.
                float noise = FractalNoise(
                    worldX + offsetX,
                    worldZ + offsetZ,
                    safeScale,
                    Mathf.Clamp(noiseOctaves, 1, 6),
                    Mathf.Clamp(persistence, 0.1f, 0.9f),
                    Mathf.Max(1f, lacunarity));

                // Map the noise value to neighboring Terrain Layers.
                float layerPosition = Mathf.Clamp01(noise) * (layerCount - 1);

                int lowerLayer = Mathf.FloorToInt(layerPosition);
                int upperLayer = Mathf.Min(lowerLayer + 1, layerCount - 1);

                // Smooth the transition between neighboring textures.
                float blend = Mathf.SmoothStep(
                    0f, 1f, layerPosition - lowerLayer);

                alphamaps[z, x, lowerLayer] = 1f - blend;
                alphamaps[z, x, upperLayer] += blend;
            }
        }

        // Apply the texture blending to the Terrain.
        data.SetAlphamaps(0, 0, alphamaps);
    }

    [ContextMenu("Clear Spawned Objects")]
    public void ClearSpawnedObjects()
    {
        if (generatedObjectsRoot == null)
        {
            Transform found = transform.Find("__GeneratedTerrainObjects");
            if (found != null)
            {
                generatedObjectsRoot = found;
            }
        }

        if (generatedObjectsRoot == null)
        {
            return;
        }

        // Iterate backwards because children are removed during the loop.
        for (int i = generatedObjectsRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = generatedObjectsRoot.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private bool EnsureTerrain()
    {
        if (terrain == null && generatedTerrainObject != null)
        {
            terrain = generatedTerrainObject.GetComponent<Terrain>();
        }

        if (terrain == null)
        {
            if (!createTerrainIfMissing)
            {
                Debug.LogError("TerrainGenerator: Assign a Terrain or enable 'Create Terrain If Missing'.", this);
                return false;
            }

            TerrainData newData = new TerrainData();
            newData.heightmapResolution = GetValidHeightmapResolution(heightmapResolution);
            newData.size = GetSafeTerrainSize();

            generatedTerrainObject = Terrain.CreateTerrainGameObject(newData);
            generatedTerrainObject.name = "Generated Terrain";
            generatedTerrainObject.transform.position = transform.position;
            terrain = generatedTerrainObject.GetComponent<Terrain>();
        }
        else
        {
            TerrainData data = terrain.terrainData;
            if (data == null)
            {
                Debug.LogError("TerrainGenerator: The assigned Terrain has no TerrainData.", this);
                return false;
            }

            int validResolution = GetValidHeightmapResolution(heightmapResolution);
            if (data.heightmapResolution != validResolution)
            {
                data.heightmapResolution = validResolution;
            }
            data.size = GetSafeTerrainSize();
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("TerrainGenerator: Failed to create or access TerrainData.", this);
            return false;
        }

        return true;
    }

    private Vector3 GetSafeTerrainSize()
    {
        return new Vector3(
            Mathf.Max(1f, terrainSize.x),
            Mathf.Max(1f, terrainSize.y),
            Mathf.Max(1f, terrainSize.z));
    }

    private static int GetValidHeightmapResolution(int requested)
    {
        int clamped = Mathf.Clamp(requested, 33, 4097);
        return Mathf.ClosestPowerOfTwo(clamped - 1) + 1;
    }

    private void GenerateHeightmap(System.Random noiseRandom)
    {
        TerrainData data = terrain.terrainData;
        int resolution = data.heightmapResolution;
        float[,] heights = new float[resolution, resolution];
        Vector3 size = data.size;

        float offsetX = noiseRandom.Next(-100000, 100000);
        float offsetZ = noiseRandom.Next(-100000, 100000);
        float safeNoiseScale = Mathf.Max(1f, noiseScale);
        int octaves = Mathf.Clamp(noiseOctaves, 1, 6);
        float safePersistence = Mathf.Clamp(persistence, 0.1f, 0.9f);
        float safeLacunarity = Mathf.Max(1f, lacunarity);
        float safeBaseHeight = Mathf.Clamp01(baseHeight);
        float safeHilliness = Mathf.Max(0f, hilliness);
        float safeEdgeWidth = Mathf.Max(0.001f, edgeBlendWidth);

        for (int z = 0; z < resolution; z++)
        {
            float normalizedZ = z / (float)(resolution - 1);
            float worldZ = normalizedZ * size.z;

            for (int x = 0; x < resolution; x++)
            {
                float normalizedX = x / (float)(resolution - 1);
                float worldX = normalizedX * size.x;

                float noise = FractalNoise(
                    worldX + offsetX,
                    worldZ + offsetZ,
                    safeNoiseScale,
                    octaves,
                    safePersistence,
                    safeLacunarity);

                float edgeFactor = 1f;
                if (flattenEdges)
                {
                    float nearestEdge = Mathf.Min(
                        Mathf.Min(normalizedX, 1f - normalizedX),
                        Mathf.Min(normalizedZ, 1f - normalizedZ));
                    edgeFactor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(nearestEdge / safeEdgeWidth));
                }

                float height = safeBaseHeight + ((noise - 0.5f) * safeHilliness * edgeFactor);
                heights[z, x] = Mathf.Clamp01(height);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    private static float FractalNoise(
        float x,
        float z,
        float scale,
        int octaves,
        float persistenceValue,
        float lacunarityValue)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float total = 0f;
        float totalAmplitude = 0f;

        for (int octave = 0; octave < octaves; octave++)
        {
            float sampleX = (x / scale) * frequency;
            float sampleZ = (z / scale) * frequency;
            total += Mathf.PerlinNoise(sampleX, sampleZ) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= persistenceValue;
            frequency *= lacunarityValue;
        }

        return totalAmplitude > 0f ? total / totalAmplitude : 0.5f;
    }

    private void GenerateBoundaryWalls()
    {
        ClearBoundaryWalls();

        if (terrain == null || terrain.terrainData == null)
            return;

        Vector3 size = terrain.terrainData.size;

        float thickness = Mathf.Max(0.1f, boundaryThickness);
        float inset = Mathf.Clamp(
            boundaryInset,
            0f,
            Mathf.Min(size.x, size.z) * 0.25f
        );

        // Extend the walls below and above the entire terrain.
        float padding = Mathf.Max(1f, boundaryVerticalPadding);
        float wallHeight = size.y + padding * 2f;
        float centerY = size.y * 0.5f;

        // Create a separate parent for the boundary colliders.
        GameObject root = new GameObject("__GeneratedTerrainBoundary");
        root.transform.SetParent(terrain.transform, false);

        generatedBoundaryRoot = root.transform;

        // Left side (West)
        CreateBoundaryWall(
            "Boundary_West",
            new Vector3(inset - thickness * 0.5f, centerY, size.z * 0.5f),
            new Vector3(thickness, wallHeight, size.z + thickness * 2f)
        );

        // Right side (East)
        CreateBoundaryWall(
            "Boundary_East",
            new Vector3(size.x - inset + thickness * 0.5f, centerY, size.z * 0.5f),
            new Vector3(thickness, wallHeight, size.z + thickness * 2f)
        );

        // Front side (South)
        CreateBoundaryWall(
            "Boundary_South",
            new Vector3(size.x * 0.5f, centerY, inset - thickness * 0.5f),
            new Vector3(size.x + thickness * 2f, wallHeight, thickness)
        );

        // Back side (North)
        CreateBoundaryWall(
            "Boundary_North",
            new Vector3(size.x * 0.5f, centerY, size.z - inset + thickness * 0.5f),
            new Vector3(size.x + thickness * 2f, wallHeight, thickness)
        );
    }

    private void CreateBoundaryWall(
        string wallName,
        Vector3 localPosition,
        Vector3 colliderSize)
    {
        GameObject wall = new GameObject(wallName);

        wall.transform.SetParent(generatedBoundaryRoot, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localRotation = Quaternion.identity;
        wall.transform.localScale = Vector3.one;

        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = colliderSize;
    }

    private void ClearBoundaryWalls()
    {
        if (generatedBoundaryRoot == null && terrain != null)
        {
            generatedBoundaryRoot =
                terrain.transform.Find("__GeneratedTerrainBoundary");
        }

        if (generatedBoundaryRoot == null)
            return;

        if (Application.isPlaying)
            Destroy(generatedBoundaryRoot.gameObject);
        else
            DestroyImmediate(generatedBoundaryRoot.gameObject);

        generatedBoundaryRoot = null;
    }

    private int SpawnPointsOfInterest(System.Random rng)
    {
        int placed = 0;
        if (pointsOfInterest == null)
        {
            return placed;
        }

        for (int i = 0; i < pointsOfInterest.Length; i++)
        {
            PointOfInterest poi = pointsOfInterest[i];
            if (poi == null || poi.prefab == null || poi.amount <= 0)
            {
                continue;
            }

            int entryPlaced = 0;
            for (int j = 0; j < poi.amount; j++)
            {
                Vector3 position;
                float normalizedX;
                float normalizedZ;
                if (!TryFindPlacement(rng, out position, out normalizedX, out normalizedZ))
                {
                    continue;
                }

                float yaw = poi.randomYaw ? NextFloat(rng, 0f, 360f) : 0f;
                Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
                if (poi.alignToTerrain)
                {
                    Vector3 normal = terrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
                    rotation = Quaternion.FromToRotation(Vector3.up, normal) * rotation;
                }

                Instantiate(poi.prefab, position, rotation, generatedObjectsRoot);
                occupiedPositions.Add(position);
                entryPlaced++;
                placed++;
            }

            if (entryPlaced < poi.amount)
            {
                string entryName = string.IsNullOrEmpty(poi.label) ? poi.prefab.name : poi.label;
                Debug.LogWarning(
                    "TerrainGenerator: Placed " + entryPlaced + " of " + poi.amount +
                    " requested instance(s) for POI '" + entryName + "'. Try reducing spacing/slope limits or increasing the attempt count.",
                    this);
            }
        }

        return placed;
    }

    private int SpawnTrees(System.Random rng)
    {
        int placed = 0;
        if (treePrefabs == null || treePrefabs.Length == 0 || treeCount <= 0)
        {
            return placed;
        }

        List<GameObject> validPrefabs = new List<GameObject>();
        for (int i = 0; i < treePrefabs.Length; i++)
        {
            if (treePrefabs[i] != null)
            {
                validPrefabs.Add(treePrefabs[i]);
            }
        }

        if (validPrefabs.Count == 0)
        {
            return placed;
        }

        for (int i = 0; i < treeCount; i++)
        {
            Vector3 position;
            float normalizedX;
            float normalizedZ;
            if (!TryFindPlacement(rng, out position, out normalizedX, out normalizedZ))
            {
                continue;
            }

            GameObject prefab = validPrefabs[rng.Next(0, validPrefabs.Count)];
            float yaw = NextFloat(rng, 0f, 360f);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            Instantiate(prefab, position, rotation, generatedObjectsRoot);
            occupiedPositions.Add(position);
            placed++;
        }

        if (placed < treeCount)
        {
            Debug.LogWarning(
                "TerrainGenerator: Placed " + placed + " of " + treeCount +
                " requested trees. Try reducing spacing/slope limits or increasing the attempt count.",
                this);
        }

        return placed;
    }

    private bool TryFindPlacement(
        System.Random rng,
        out Vector3 position,
        out float normalizedX,
        out float normalizedZ)
    {
        TerrainData data = terrain.terrainData;
        Vector3 terrainPosition = terrain.transform.position;
        Vector3 size = data.size;
        float margin = Mathf.Clamp(placementEdgeMargin, 0f, 0.45f);
        float minDistanceSqr = minimumSpacing * minimumSpacing;
        int tries = Mathf.Max(1, attemptsPerObject);

        for (int attempt = 0; attempt < tries; attempt++)
        {
            normalizedX = NextFloat(rng, margin, 1f - margin);
            normalizedZ = NextFloat(rng, margin, 1f - margin);

            float worldX = terrainPosition.x + normalizedX * size.x;
            float worldZ = terrainPosition.z + normalizedZ * size.z;
            Vector3 candidate = new Vector3(worldX, 0f, worldZ);
            candidate.y = terrain.SampleHeight(candidate) + terrainPosition.y;

            Vector3 normal = data.GetInterpolatedNormal(normalizedX, normalizedZ);
            float slope = Vector3.Angle(normal, Vector3.up);
            if (slope > maxPlacementSlope)
            {
                continue;
            }

            bool tooClose = false;
            if (minDistanceSqr > 0f)
            {
                for (int i = 0; i < occupiedPositions.Count; i++)
                {
                    Vector3 delta = candidate - occupiedPositions[i];
                    delta.y = 0f;
                    if (delta.sqrMagnitude < minDistanceSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }
            }

            if (tooClose)
            {
                continue;
            }

            position = candidate;
            return true;
        }

        position = Vector3.zero;
        normalizedX = 0f;
        normalizedZ = 0f;
        return false;
    }

    private void EnsureGeneratedObjectsRoot()
    {
        if (generatedObjectsRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("__GeneratedTerrainObjects");
        if (existing != null)
        {
            generatedObjectsRoot = existing;
            return;
        }

        GameObject root = new GameObject("__GeneratedTerrainObjects");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        generatedObjectsRoot = root.transform;
    }

    private static float NextFloat(System.Random rng, float minInclusive, float maxExclusive)
    {
        return minInclusive + (float)rng.NextDouble() * (maxExclusive - minInclusive);
    }


    private void GenerateVisualBorder()
    {
        ClearVisualBorder();

        if (terrain == null || terrain.terrainData == null)
            return;

        if (visualBorderMaterial == null)
        {
            Debug.LogWarning(
                "TerrainGenerator: Assign a material to Visual Border Material.",
                this);
            return;
        }

        TerrainData data = terrain.terrainData;
        Vector3 size = data.size;

        float perimeter = 2f * (size.x + size.z);
        float segmentLength = Mathf.Max(1f, visualBorderSegmentLength);

        // Generate points along all four sides, including corners.
        List<float> distances = new List<float>();

        AddBoundaryDistances(distances, 0f, size.x, segmentLength);
        AddBoundaryDistances(distances, size.x, size.z, segmentLength);
        AddBoundaryDistances(distances, size.x + size.z, size.x, segmentLength);
        AddBoundaryDistances(distances, 2f * size.x + size.z, size.z, segmentLength);

        int count = distances.Count;

        Vector3[] innerBottom = new Vector3[count];
        Vector3[] innerMiddle = new Vector3[count];
        Vector3[] innerTop = new Vector3[count];

        Vector3[] outerBottom = new Vector3[count];
        Vector3[] outerMiddle = new Vector3[count];
        Vector3[] outerTop = new Vector3[count];

        float[] midV = new float[count];
        float[] topV = new float[count];

        float safeDepth = Mathf.Max(0.1f, visualBorderDepth);
        float safeThickness = Mathf.Max(0.5f, visualBorderThickness);
        float safeNoiseScale = Mathf.Max(1f, visualBorderNoiseScale);
        float uvScale = Mathf.Max(0.1f, visualBorderTextureWorldSize);

        float seedOffset = seed * 0.0137f;

        // Generate the irregular cliff profile.
        for (int i = 0; i < count; i++)
        {
            float distance = distances[i];

            Vector3 point;
            Vector3 outward;
            float miter;

            GetVisualBorderPoint(
                distance,
                size,
                out point,
                out outward,
                out miter);

            float normalizedX = point.x / size.x;
            float normalizedZ = point.z / size.z;

            float groundY = data.GetInterpolatedHeight(
                normalizedX,
                normalizedZ);

            // Combine broad and fine noise for an irregular ridgeline.
            float broadNoise = Mathf.PerlinNoise(
                (distance + seedOffset) / safeNoiseScale,
                15.17f + seedOffset * 0.01f);

            float detailNoise = Mathf.PerlinNoise(
                (distance + seedOffset * 2.3f) /
                Mathf.Max(1f, safeNoiseScale * 0.28f),
                43.7f + seedOffset * 0.017f);

            float fineNoise = Mathf.PerlinNoise(
                (distance + seedOffset * 5.7f) /
                Mathf.Max(1f, safeNoiseScale * 0.11f),
                91.2f + seedOffset * 0.031f);

            float heightNoise =
                (broadNoise - 0.5f) * 2f +
                (detailNoise - 0.5f) * 0.5f;

            float wallHeight = Mathf.Max(
                2f,
                visualBorderHeight +
                heightNoise * visualBorderHeightVariation);

            float topY = groundY + wallHeight +
                (fineNoise - 0.5f) * 2f *
                visualBorderHeightVariation * 0.12f;

            float bulge =
                (fineNoise - 0.5f) * 2f *
                visualBorderFaceRoughness;

            float midOffset =
                (detailNoise - 0.5f) * 2f *
                visualBorderFaceRoughness * 0.25f;

            float bottomY = groundY - safeDepth;

            float midY = Mathf.Lerp(groundY, topY, 0.48f) + midOffset;

            midY = Mathf.Clamp(
                midY,
                groundY + 0.2f,
                topY - 0.2f);

            // Mitered corners prevent gaps between the four sides.
            Vector3 innerBase = point -
                outward * (visualBorderOverlap * miter);

            Vector3 outerBase = point +
                outward * (safeThickness * miter);

            Vector3 innerMidBase = innerBase +
                outward * (bulge * 0.2f);

            Vector3 outerMidBase = outerBase +
                outward * bulge;

            Vector3 outerTopBase = outerBase +
                outward * (bulge * 0.15f);

            innerBottom[i] = new Vector3(
                innerBase.x, bottomY, innerBase.z);

            innerMiddle[i] = new Vector3(
                innerMidBase.x, midY, innerMidBase.z);

            innerTop[i] = new Vector3(
                innerBase.x, topY, innerBase.z);

            outerBottom[i] = new Vector3(
                outerBase.x, bottomY, outerBase.z);

            outerMiddle[i] = new Vector3(
                outerMidBase.x, midY, outerMidBase.z);

            outerTop[i] = new Vector3(
                outerTopBase.x, topY, outerTopBase.z);

            midV[i] = (midY - bottomY) / uvScale;
            topV[i] = (topY - bottomY) / uvScale;
        }

        // Build the mesh.
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        float widthUV = safeThickness / uvScale;

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;

            float u0 = distances[i] / uvScale;

            float nextDistance = i == count - 1
                ? perimeter
                : distances[i + 1];

            float u1 = nextDistance / uvScale;

            // Inner cliff face: lower section.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                innerBottom[i], innerBottom[next],
                innerMiddle[next], innerMiddle[i],
                new Vector2(u0, 0f),
                new Vector2(u1, 0f),
                new Vector2(u1, midV[next]),
                new Vector2(u0, midV[i]));

            // Inner cliff face: upper section.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                innerMiddle[i], innerMiddle[next],
                innerTop[next], innerTop[i],
                new Vector2(u0, midV[i]),
                new Vector2(u1, midV[next]),
                new Vector2(u1, topV[next]),
                new Vector2(u0, topV[i]));

            // Outer cliff face: lower section.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                outerBottom[i], outerMiddle[i],
                outerMiddle[next], outerBottom[next],
                new Vector2(u0, 0f),
                new Vector2(u0, midV[i]),
                new Vector2(u1, midV[next]),
                new Vector2(u1, 0f));

            // Outer cliff face: upper section.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                outerMiddle[i], outerTop[i],
                outerTop[next], outerMiddle[next],
                new Vector2(u0, midV[i]),
                new Vector2(u0, topV[i]),
                new Vector2(u1, topV[next]),
                new Vector2(u1, midV[next]));

            // Top surface connecting the two cliff faces.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                innerTop[i], innerTop[next],
                outerTop[next], outerTop[i],
                new Vector2(u0, topV[i]),
                new Vector2(u1, topV[next]),
                new Vector2(u1, topV[next] + widthUV),
                new Vector2(u0, topV[i] + widthUV));

            // Bottom surface.
            AddVisualBorderQuad(
                vertices, triangles, uvs,
                innerBottom[i], outerBottom[i],
                outerBottom[next], innerBottom[next],
                new Vector2(u0, 0f),
                new Vector2(u0, widthUV),
                new Vector2(u1, widthUV),
                new Vector2(u1, 0f));
        }

        Mesh mesh = new Mesh();
        mesh.name = "Generated Rocky Terrain Border";

        // Support larger borders without exceeding 16-bit indices.
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uvs);

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Create the visual border object.
        GameObject borderObject =
            new GameObject("__GeneratedVisualBorder");

        borderObject.transform.SetParent(terrain.transform, false);

        generatedVisualBorderRoot = borderObject.transform;

        MeshFilter filter = borderObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer meshRenderer =
            borderObject.AddComponent<MeshRenderer>();

        meshRenderer.sharedMaterial = visualBorderMaterial;
        meshRenderer.receiveShadows = true;
        meshRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.On;
    }

    private static void AddBoundaryDistances(
        List<float> distances,
        float sideStart,
        float sideLength,
        float targetSegmentLength)
    {
        int segments = Mathf.Max(
            1,
            Mathf.CeilToInt(sideLength / targetSegmentLength));

        for (int i = 0; i < segments; i++)
        {
            distances.Add(
                sideStart + sideLength * ((float)i / segments));
        }
    }

    private static void GetVisualBorderPoint(
        float distance,
        Vector3 size,
        out Vector3 point,
        out Vector3 outward,
        out float miter)
    {
        float right = size.x;
        float topRight = size.x + size.z;
        float topLeft = 2f * size.x + size.z;

        miter = 1f;

        // Bottom-left corner.
        if (Mathf.Abs(distance) < 0.001f)
        {
            point = Vector3.zero;
            outward = new Vector3(-1f, 0f, -1f).normalized;
            miter = 1.41421356f;
        }
        // Bottom-right corner.
        else if (Mathf.Abs(distance - right) < 0.001f)
        {
            point = new Vector3(size.x, 0f, 0f);
            outward = new Vector3(1f, 0f, -1f).normalized;
            miter = 1.41421356f;
        }
        // Top-right corner.
        else if (Mathf.Abs(distance - topRight) < 0.001f)
        {
            point = new Vector3(size.x, 0f, size.z);
            outward = new Vector3(1f, 0f, 1f).normalized;
            miter = 1.41421356f;
        }
        // Top-left corner.
        else if (Mathf.Abs(distance - topLeft) < 0.001f)
        {
            point = new Vector3(0f, 0f, size.z);
            outward = new Vector3(-1f, 0f, 1f).normalized;
            miter = 1.41421356f;
        }
        // Bottom side.
        else if (distance < right)
        {
            point = new Vector3(distance, 0f, 0f);
            outward = Vector3.back;
        }
        // Right side.
        else if (distance < topRight)
        {
            point = new Vector3(
                size.x, 0f, distance - size.x);

            outward = Vector3.right;
        }
        // Top side.
        else if (distance < topLeft)
        {
            point = new Vector3(
                size.x - (distance - topRight),
                0f,
                size.z);

            outward = Vector3.forward;
        }
        // Left side.
        else
        {
            point = new Vector3(
                0f,
                0f,
                size.z - (distance - topLeft));

            outward = Vector3.left;
        }
    }

    private static void AddVisualBorderQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Vector2> uvs,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);

        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private void ClearVisualBorder()
    {
        if (generatedVisualBorderRoot == null && terrain != null)
        {
            generatedVisualBorderRoot =
                terrain.transform.Find("__GeneratedVisualBorder");
        }

        if (generatedVisualBorderRoot == null)
            return;

        MeshFilter filter =
            generatedVisualBorderRoot.GetComponent<MeshFilter>();

        Mesh mesh = filter != null ? filter.sharedMesh : null;

        GameObject borderObject =
            generatedVisualBorderRoot.gameObject;

        generatedVisualBorderRoot = null;

        if (Application.isPlaying)
        {
            if (mesh != null)
                Destroy(mesh);

            Destroy(borderObject);
        }
        else
        {
            if (mesh != null)
                DestroyImmediate(mesh);

            DestroyImmediate(borderObject);
        }
    }

}
