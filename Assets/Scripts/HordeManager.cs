
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class HordeManager : MonoBehaviour
{
    public static HordeManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private HordeEnemy enemyPrefab;
    [SerializeField] private XPBlobPool xpBlobPool;
    [SerializeField] private DeathParticlePool deathParticlePool;

    [Header("Enemy Pool")]
    [SerializeField] private int prewarmCount = 100;
    [SerializeField] private int maxPoolSize = 512;

    [Header("Wave Spawning")]
    [SerializeField] private bool spawnWaves = true;
    [SerializeField] private int enemiesPerWave = 30;
    [SerializeField] private float spawnInterval = 15f;
    [SerializeField] private float spawnRadius = 30f;
    [SerializeField] private float minSpawnRadius = 15f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastHeight = 100f;
    [SerializeField] private float enemySpawnHeight = 1f;

    [Header("Central Update")]
    [SerializeField] private float managerTickInterval = 0.05f;

    [Header("Distance Based Updates")]
    [SerializeField] private float nearDistance = 25f;
    [SerializeField] private float mediumDistance = 60f;

    [SerializeField] private float nearUpdateInterval = 0.05f;
    [SerializeField] private float mediumUpdateInterval = 0.1f;
    [SerializeField] private float farUpdateInterval = 0.25f;

    // Active enemies.
    private readonly List<HordeEnemy> activeEnemies =
        new List<HordeEnemy>();

    // Allows O(1) registration and removal.
    private readonly Dictionary<HordeEnemy, int> enemyIndices =
        new Dictionary<HordeEnemy, int>();

    // Inactive enemies.
    private readonly Stack<HordeEnemy> enemyPool =
        new Stack<HordeEnemy>();

    // Prevents an enemy from being added to the pool twice.
    private readonly HashSet<HordeEnemy> pooledEnemies =
        new HashSet<HordeEnemy>();

    // Inactive parent for pooled enemies.
    private Transform poolRoot;

    private float tickTimer;

    public Transform Player => player;

    public int ActiveEnemyCount => activeEnemies.Count;
    public int PooledEnemyCount => enemyPool.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Multiple HordeManagers detected!");
            enabled = false;
            return;
        }

        Instance = this;

        CreatePoolRoot();
        PrewarmPool();
    }

    private void OnEnable()
    {
        if (Instance != this || !spawnWaves)
            return;

        InvokeRepeating(
            nameof(SpawnWave),
            0f,
            Mathf.Max(0.1f, spawnInterval)
        );
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(SpawnWave));
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------
    // ENEMY UPDATE SYSTEM
    // ------------------------------------------------

    private void FixedUpdate()
    {
        if (player == null || activeEnemies.Count == 0)
        {
            tickTimer = 0f;
            return;
        }

        tickTimer += Time.fixedDeltaTime;

        float tickInterval = Mathf.Max(0.02f, managerTickInterval);

        if (tickTimer < tickInterval)
            return;

        float elapsed = tickTimer;
        tickTimer = 0f;

        Vector3 playerPosition = player.position;

        float nearSqr = nearDistance * nearDistance;
        float mediumSqr = mediumDistance * mediumDistance;

        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            HordeEnemy enemy = activeEnemies[i];

            if (enemy == null)
            {
                RemoveAt(i);
                continue;
            }

            float distanceSqr =
                (enemy.transform.position - playerPosition).sqrMagnitude;

            float updateInterval;

            if (distanceSqr <= nearSqr)
                updateInterval = nearUpdateInterval;
            else if (distanceSqr <= mediumSqr)
                updateInterval = mediumUpdateInterval;
            else
                updateInterval = farUpdateInterval;

            enemy.ManagedTick(
                elapsed,
                updateInterval,
                playerPosition
            );
        }
    }

    // ------------------------------------------------
    // ENEMY REGISTRATION
    // ------------------------------------------------

    // Called automatically by HordeEnemy.OnEnable().
    internal void RegisterEnemy(HordeEnemy enemy)
    {
        if (enemy == null || enemyIndices.ContainsKey(enemy))
            return;

        enemyIndices.Add(enemy, activeEnemies.Count);
        activeEnemies.Add(enemy);
    }

    // Called automatically by HordeEnemy.OnDisable().
    internal void UnregisterEnemy(HordeEnemy enemy)
    {
        if (object.ReferenceEquals(enemy, null))
            return;

        if (!enemyIndices.TryGetValue(enemy, out int index))
            return;

        RemoveAt(index);
    }

    private void RemoveAt(int index)
    {
        int lastIndex = activeEnemies.Count - 1;

        HordeEnemy removed = activeEnemies[index];
        HordeEnemy last = activeEnemies[lastIndex];

        activeEnemies[index] = last;
        activeEnemies.RemoveAt(lastIndex);

        if (last != null && last != removed)
            enemyIndices[last] = index;

        if (!object.ReferenceEquals(removed, null))
            enemyIndices.Remove(removed);
    }

    // ------------------------------------------------
    // OBJECT POOL
    // ------------------------------------------------

    private void CreatePoolRoot()
    {
        GameObject root = new GameObject("Pooled Horde Enemies");

        root.transform.SetParent(transform, false);
        root.SetActive(false);

        poolRoot = root.transform;
    }

    private HordeEnemy CreateEnemy()
    {
        HordeEnemy enemy = Instantiate(
            enemyPrefab,
            poolRoot
        );

        // Configure dependencies before activating the enemy.
        // The parent is inactive, so Awake/OnEnable haven't run yet.
        enemy.gameObject.SetActive(true);
        enemy.xpBlobPool = xpBlobPool;
        enemy.deathParticlePool = deathParticlePool;

        // Moving to an active parent initializes the enemy.
        enemy.transform.SetParent(transform, false);

        enemy.PrepareForPool();
        enemy.gameObject.SetActive(false);

        enemy.transform.SetParent(poolRoot, false);

        return enemy;
    }

    private void PrewarmPool()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("HordeManager: No enemy prefab assigned!");
            return;
        }

        int count = Mathf.Min(
            Mathf.Max(0, prewarmCount),
            Mathf.Max(0, maxPoolSize)
        );

        for (int i = 0; i < count; i++)
        {
            HordeEnemy enemy = CreateEnemy();

            enemyPool.Push(enemy);
            pooledEnemies.Add(enemy);
        }
    }

    public HordeEnemy SpawnEnemy(Vector3 position)
    {
        return SpawnEnemy(position, Quaternion.identity);
    }

    public HordeEnemy SpawnEnemy(
        Vector3 position,
        Quaternion rotation)
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("HordeManager: Missing enemy prefab.");
            return null;
        }

        HordeEnemy enemy;

        if (enemyPool.Count > 0)
        {
            enemy = enemyPool.Pop();
            pooledEnemies.Remove(enemy);
        }
        else
        {
            enemy = CreateEnemy();
        }

        enemy.transform.SetParent(null, true);

        enemy.transform.SetPositionAndRotation(
            position,
            rotation
        );

        // Always assign dependencies and reset, including newly
        // instantiated enemies that weren't previously pooled.
        enemy.xpBlobPool = xpBlobPool;
        enemy.deathParticlePool = deathParticlePool;

        enemy.ResetForSpawn();

        enemy.gameObject.SetActive(true);

        return enemy;
    }

    public void DespawnEnemy(HordeEnemy enemy)
    {
        if (enemy == null || pooledEnemies.Contains(enemy))
            return;

        enemy.PrepareForPool();
        enemy.gameObject.SetActive(false);

        // Don't let the inactive pool grow indefinitely.
        if (enemyPool.Count >= maxPoolSize)
        {
            Destroy(enemy.gameObject);
            return;
        }

        enemy.transform.SetParent(poolRoot, false);

        enemyPool.Push(enemy);
        pooledEnemies.Add(enemy);
    }

    // ------------------------------------------------
    // WAVE SPAWNING
    // ------------------------------------------------

    private void SpawnWave()
    {
        if (player == null || enemyPrefab == null)
            return;

        for (int i = 0; i < enemiesPerWave; i++)
        {
            if (TryGetSpawnPosition(out Vector3 spawnPosition))
            {
                SpawnEnemy(spawnPosition);
            }
        }
    }

    private bool TryGetSpawnPosition(out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        Vector2 direction = Random.insideUnitCircle;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;

        direction.Normalize();

        float minRadius = Mathf.Max(0f, minSpawnRadius);
        float maxRadius = Mathf.Max(minRadius, spawnRadius);

        float radius = Random.Range(minRadius, maxRadius);

        float height = Mathf.Max(0.1f, raycastHeight);

        Vector3 rayOrigin = player.position + new Vector3(
            direction.x * radius,
            height,
            direction.y * radius
        );

        if (!Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            height * 2f,
            groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        spawnPosition = hit.point + Vector3.up * enemySpawnHeight;

        return true;
    }
}
