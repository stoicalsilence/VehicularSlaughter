using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Decorative clouds for TerrainGenerator. Generation is invoked after the map is built.
/// Attach to the same unscaled GameObject as TerrainGenerator.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TerrainGenerator))]
public class CloudSpawner : MonoBehaviour
{
    [Header("Cloud Prefabs")]
    [SerializeField] private GameObject[] cloudPrefabs;
    [SerializeField, Min(0)] private int cloudCount = 30;

    [Header("Cloud Distribution")]
    [Tooltip("Extra distance beyond each terrain edge, in world units.")]
    [SerializeField, Min(0f)] private float edgePadding = 100f;
    [Tooltip("Height of the cloud bottom above the terrain's maximum possible elevation.")]
    [SerializeField] private Vector2 heightAboveTerrain = new Vector2(25f, 55f);
    [Tooltip("Target largest horizontal renderer-bounds dimension, in world units.")]
    [SerializeField] private Vector2 cloudWidth = new Vector2(25f, 55f);
    [SerializeField] private bool randomYaw = true;

    [Header("Wind")]
    [SerializeField] private bool animateClouds = true;
    [Tooltip("World-space X and Z direction. Magnitude is ignored.")]
    [SerializeField] private Vector2 windDirection = new Vector2(1f, 0.25f);
    [SerializeField, Min(0f)] private float windSpeed = 1.5f;

    [Header("Rendering")]
    [SerializeField] private bool castShadows = false;
    [SerializeField] private bool receiveShadows = false;
    [Tooltip("Disable prefab colliders so clouds do not affect gameplay physics.")]
    [SerializeField] private bool disableColliders = true;

    private Transform cloudRoot;
    private Vector2 boundsMin;
    private Vector2 boundsMax;

    public void GenerateClouds(Terrain terrain, int terrainSeed)
    {
        if (!Application.isPlaying) return;
        ClearClouds();
        if (!isActiveAndEnabled || terrain == null || terrain.terrainData == null || cloudCount <= 0)
            return;

        List<GameObject> validPrefabs = new List<GameObject>();
        if (cloudPrefabs != null)
        {
            foreach (GameObject prefab in cloudPrefabs)
                if (prefab != null) validPrefabs.Add(prefab);
        }
        if (validPrefabs.Count == 0) return;

        // Separate random stream: clouds never affect tree or POI placement.
        System.Random rng = new System.Random(unchecked(terrainSeed ^ 0x3A51C0D5));
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        float padding = Mathf.Max(0f, edgePadding);
        boundsMin = new Vector2(origin.x - padding, origin.z - padding);
        boundsMax = new Vector2(origin.x + size.x + padding, origin.z + size.z + padding);
        Vector2 span = boundsMax - boundsMin;

        cloudRoot = new GameObject("__GeneratedClouds").transform;
        cloudRoot.SetParent(transform, false);

        // Jittered grid: distribute clouds fairly evenly, avoiding a single dense clump.
        int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(cloudCount * span.x / span.y)), 1, cloudCount);
        int rows = Mathf.CeilToInt(cloudCount / (float)columns);
        List<int> cells = new List<int>(columns * rows);
        for (int i = 0; i < columns * rows; i++) cells.Add(i);
        for (int i = cells.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int temp = cells[i];
            cells[i] = cells[j];
            cells[j] = temp;
        }

        float minHeight = Mathf.Max(0f, Mathf.Min(heightAboveTerrain.x, heightAboveTerrain.y));
        float maxHeight = Mathf.Max(minHeight, Mathf.Max(heightAboveTerrain.x, heightAboveTerrain.y));
        float minWidth = Mathf.Max(0.1f, Mathf.Min(cloudWidth.x, cloudWidth.y));
        float maxWidth = Mathf.Max(minWidth, Mathf.Max(cloudWidth.x, cloudWidth.y));

        for (int i = 0; i < cloudCount; i++)
        {
            int cell = cells[i];
            int x = cell % columns;
            int z = cell / columns;
            float worldX = boundsMin.x + (x + Range(rng, 0.15f, 0.85f)) / columns * span.x;
            float worldZ = boundsMin.y + (z + Range(rng, 0.15f, 0.85f)) / rows * span.y;
            float altitude = origin.y + size.y + Range(rng, minHeight, maxHeight);
            float yaw = randomYaw ? Range(rng, 0f, 360f) : 0f;
            float targetWidth = Range(rng, minWidth, maxWidth);
            GameObject prefab = validPrefabs[rng.Next(validPrefabs.Count)];

            GameObject cloud = Instantiate(prefab, new Vector3(worldX, altitude, worldZ),
                Quaternion.Euler(0f, yaw, 0f), cloudRoot);
            cloud.name = "Cloud_" + (i + 1).ToString("D2") + "_" + prefab.name;

            Renderer[] renderers = cloud.GetComponentsInChildren<Renderer>(true);
            if (TryGetBounds(renderers, out Bounds bounds))
            {
                float width = Mathf.Max(bounds.size.x, bounds.size.z);
                if (width > 0.0001f)
                    cloud.transform.localScale *= targetWidth / width;

                // Center the model on the spawn position and align its bottom to the altitude.
                if (TryGetBounds(renderers, out bounds))
                    cloud.transform.position += new Vector3(worldX - bounds.center.x,
                        altitude - bounds.min.y, worldZ - bounds.center.z);
            }

            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = receiveShadows;
            }
            if (disableColliders)
                foreach (Collider collider in cloud.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
        }
    }

    private void Update()
    {
        if (!animateClouds || cloudRoot == null || windSpeed <= 0f || windDirection.sqrMagnitude < 0.0001f)
            return;

        Vector2 velocity = windDirection.normalized * windSpeed * Time.deltaTime;
        Vector2 span = boundsMax - boundsMin;
        if (span.x <= 0f || span.y <= 0f) return;

        // Move the entire layer together, preserving the original distribution.
        // Wrap only after the cloud pivot has left the extended map rectangle.
        for (int i = 0; i < cloudRoot.childCount; i++)
        {
            Transform cloud = cloudRoot.GetChild(i);
            Vector3 p = cloud.position;
            p.x = boundsMin.x + Mathf.Repeat(p.x + velocity.x - boundsMin.x, span.x);
            p.z = boundsMin.y + Mathf.Repeat(p.z + velocity.y - boundsMin.y, span.y);
            cloud.position = p;
        }
    }

    [ContextMenu("Regenerate Clouds (Play Mode)")]
    public void RegenerateClouds()
    {
        if (!Application.isPlaying) return;
        TerrainGenerator generator = GetComponent<TerrainGenerator>();
        GenerateClouds(generator.terrain, generator.seed);
    }

    [ContextMenu("Clear Clouds")]
    public void ClearClouds()
    {
        if (cloudRoot == null) return;
        GameObject oldRoot = cloudRoot.gameObject;
        cloudRoot = null;
        // Destroy is deferred in Play Mode; hide the previous clouds immediately.
        oldRoot.SetActive(false);
        if (Application.isPlaying) Destroy(oldRoot);
        else DestroyImmediate(oldRoot);
    }

    private void OnDestroy()
    {
        ClearClouds();
    }

    private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static float Range(System.Random rng, float min, float max)
    {
        return min + (float)rng.NextDouble() * (max - min);
    }
}
