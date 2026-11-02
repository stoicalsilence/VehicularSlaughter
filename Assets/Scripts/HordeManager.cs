
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class HordeManager : MonoBehaviour
{
    public static HordeManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private HordeEnemy enemyPrefab;

    [Header("Object Pool")]
    [SerializeField, Min(0)] private int prewarmCount = 100;

    [Header("Central Update")]
    [SerializeField, Min(0.02f)]
    private float managerTickInterval = 0.05f;

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

    // Inactive pooled enemies.
    private readonly Queue<HordeEnemy> enemyPool =
        new Queue<HordeEnemy>();

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
    }

    private void Start()
    {
        PrewarmPool();
    }

    private void FixedUpdate()
    {
        if (player == null || activeEnemies.Count == 0)
            return;

        tickTimer += Time.fixedDeltaTime;

        if (tickTimer < managerTickInterval)
            return;

        float elapsed = tickTimer;
        tickTimer = 0f;

        Vector3 playerPosition = player.position;

        float nearSqr = nearDistance * nearDistance;
        float mediumSqr = mediumDistance * mediumDistance;

        // Iterate backwards to make removal safe.
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
        if (ReferenceEquals(enemy, null))
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

        if (!ReferenceEquals(removed, null))
            enemyIndices.Remove(removed);
    }

    // ------------------------------------------------
    // OBJECT POOL
    // ------------------------------------------------

    private void PrewarmPool()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("HordeManager: No enemy prefab assigned.");
            return;
        }

        for (int i = 0; i < prewarmCount; i++)
        {
            HordeEnemy enemy = Instantiate(enemyPrefab, transform);

            enemy.gameObject.SetActive(false);
            enemyPool.Enqueue(enemy);
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
            enemy = enemyPool.Dequeue();

            enemy.transform.SetParent(null);
            enemy.transform.SetPositionAndRotation(position, rotation);

            enemy.ResetForSpawn();
            enemy.gameObject.SetActive(true);
        }
        else
        {
            enemy = Instantiate(enemyPrefab, position, rotation);
        }

        return enemy;
    }

    public void DespawnEnemy(HordeEnemy enemy)
    {
        if (enemy == null || !enemy.gameObject.activeSelf)
            return;

        enemy.PrepareForPool();

        enemy.gameObject.SetActive(false);
        enemy.transform.SetParent(transform, true);

        enemyPool.Enqueue(enemy);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
