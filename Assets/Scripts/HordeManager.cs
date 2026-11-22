using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class HordeManager : MonoBehaviour
{
    public static HordeManager Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private HordeEnemy enemyPrefab;
    [Header("Horde Footstep Audio")]
    [SerializeField] private HordeFootstepAudio footstepAudio;
    [Header("Enemy Archetypes (prefabs, not modifiers)")]
    [Tooltip("Relative spawn weight of the original melee prefab. Keeps the existing Enemy Prefab reference.")]
    [SerializeField, Min(0f)] private float defaultEnemyWeight = 1f;
    [Serializable]
    private sealed class AdditionalEnemyType
    {
        public HordeEnemy prefab;
        [Tooltip("Relative weight. 0 prevents this type from appearing in random spawning.")]
        [Min(0f)] public float spawnWeight = 0.14f;
        [Min(0f)] public float unlockSeconds = 45f;
        [Tooltip("Maximum simultaneously active enemies of this prefab. 0 disables it.")]
        [Min(0)] public int maxActive = 28;
        [Min(0)] public int prewarm = 12;
    }
    [SerializeField] private AdditionalEnemyType[] additionalEnemyTypes = new AdditionalEnemyType[0];
    [SerializeField] private HordeProjectilePool rangedProjectilePool;
    [SerializeField] private XPBlobPool xpBlobPool;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private DeathParticlePool deathParticlePool;
    public PlayerLevelSystem playerLevelSystem;
    public PlayerLevelRewardSystem playerLevelRewardSystem;

    [Header("Elite Variant")]
    [SerializeField, Range(0f, 1f)] private float eliteSpawnChance = 0.03f;
    [SerializeField, Min(1f)] private float eliteScale = 1.45f;
    [SerializeField, Min(1f)] private float eliteHealthMultiplier = 2.5f;
    [SerializeField, Min(1f)] private float eliteSpeedMultiplier = 1.25f;
    [SerializeField, Min(1f)] private float eliteDamageMultiplier = 1.75f;
    [SerializeField, Range(0f, 1f)] private float eliteCrateDropChance = 0.12f;
    [Tooltip("Optional override; otherwise uses the existing normal crate prefab.")]
    [SerializeField] private GameObject eliteCratePrefab;
    [SerializeField, Min(0f)] private float eliteCrateGroundOffset = 0.25f;

    [Header("EMP Variant")]
    [Tooltip("Persistent radio-controlled car. Assign explicitly; resolved once at Start if empty.")]
    [SerializeField] private CarController empTargetCar;
    [SerializeField, Range(0f, 1f)] private float empSpawnChance = 0.02f;
    [SerializeField, Min(0f)] private float empStartSeconds = 90f;
    [SerializeField, Min(0)] private int maxActiveEMP = 2;
    [SerializeField, Min(0f)] private float empSpawnCooldown = 10f;
    [SerializeField, Min(1f)] private float empSpeedMultiplier = 2.5f;
    [SerializeField, Min(0.01f)] private float empHealthMultiplier = 0.7f;
    [SerializeField, Min(0f)] private float empMinDistanceFromCar = 15f;
    [SerializeField, Min(0f)] private float empMaxDistanceFromCar = 55f;
    [Tooltip("Avoid spawning saboteurs at the car when the player is far away.")]
    [SerializeField, Min(0f)] private float empMaxPlayerDistanceFromCar = 45f;
    [SerializeField] private ParticleSystem empImpactParticlesPrefab;
    [Tooltip("Lifetime must cover all particle children (including start delays).")]
    [SerializeField, Min(0.1f)] private float empImpactEffectLifetime = 3f;
    private int activeEMPCount;
    private float nextEMPSpawnSeconds;
    public int ActiveEMPCount => activeEMPCount;
    // Total shooting-archetype count (not a modifier). Retained for compatibility.
    private int activeRangedCount;
    public int ActiveRangedCount => activeRangedCount;

    [Header("Enemy Pool")]
    [SerializeField] private int prewarmCount = 100;
    [SerializeField] private int maxPoolSize = 512;

    [Header("Continuous Spawning")]
    [SerializeField] private bool spawnWaves = true;
    [Tooltip("Base spawn rate numerator. 30 enemies / 15 seconds = 2 enemies per second.")]
    [SerializeField] private int enemiesPerWave = 30;
    [Tooltip("Base spawn rate denominator in seconds; not a discrete wave interval.")]
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

    // ==================================================
    // RUN CONFIGURATION AND STATE
    // ==================================================

    [Serializable]
    private sealed class SwarmEvent
    {
        [Min(0f)] public float startSeconds;
        [Min(0.1f)] public float duration = 30f;
        [Min(1f)] public float spawnMultiplier = 3f;

        public SwarmEvent(float start, float length, float multiplier)
        {
            startSeconds = start;
            duration = length;
            spawnMultiplier = multiplier;
        }
    }

    [Header("Survival Run References")]
    [SerializeField] private TerrainGenerator terrainGenerator;
    [SerializeField] private PlayerStats playerStats;
    [Tooltip("Optional. Otherwise uses the Player transform.")]
    [SerializeField] private Transform fountainSpawnAnchor;

    [Header("Run Clock")]
    [SerializeField] private bool startAutomatically = true;
    [Tooltip("Countdown duration and time to reach maximum time-based threat. Reaching zero does not end gameplay.")]
    [SerializeField, Min(1f)] private float runDuration = 900f;

    [Header("Threat at countdown zero (before player difficulty)")]
    [SerializeField, Min(1f)] private float endSpawnMultiplier = 4f;
    [SerializeField, Min(1f)] private float endHealthMultiplier = 3f;
    [SerializeField, Min(1f)] private float endSpeedMultiplier = 1.35f;
    [SerializeField, Min(1f)] private float endDamageMultiplier = 2f;
    [SerializeField, Min(0.01f)] private float timeScalingExponent = 1.35f;

    [Header("Extra effect of the player's difficulty above 100%")]
    [SerializeField, Min(0f)] private float difficultySpawnInfluence = 1f;
    [SerializeField, Min(0f)] private float difficultyHealthInfluence = 1f;
    [SerializeField, Min(0f)] private float difficultySpeedInfluence = 0.2f;
    [SerializeField, Min(0f)] private float difficultyDamageInfluence = 0.65f;

    [Header("Spawn performance budget")]
    [SerializeField, Min(1)] private int normalActiveEnemyLimit = 250;
    [SerializeField, Min(1)] private int swarmActiveEnemyLimit = 350;
    [SerializeField, Min(1)] private int maxSpawnsPerFrame = 12;
    [SerializeField, Min(0.1f)] private float maxEnemiesPerSecond = 60f;

    [Header("Swarm Schedule (elapsed seconds)")]
    [SerializeField, Min(0f)] private float swarmWarningSeconds = 8f;
    [SerializeField]
    private SwarmEvent[] swarms =
    {
        new SwarmEvent(210f, 25f, 3f),
        new SwarmEvent(390f, 30f, 3.2f),
        new SwarmEvent(570f, 35f, 3.4f),
        new SwarmEvent(720f, 35f, 3.6f),
        new SwarmEvent(870f, 30f, 4.5f)
    };

    [Header("Temporary XP Fountain")]
    [SerializeField] private GameObject xpFountainPrefab;
    [SerializeField] private float[] fountainSpawnTimes = { 120f, 300f, 480f, 660f, 795f };
    [SerializeField, Min(1f)] private float fountainLifetime = 75f;
    [SerializeField, Min(0f)] private float fountainMinDistance = 35f;
    [SerializeField, Min(0f)] private float fountainMaxDistance = 90f;
    [SerializeField, Min(0.1f)] private float fountainRetryInterval = 5f;
    [SerializeField, Min(0f)] private float fountainExpiryWarning = 15f;

    [Tooltip("Optional name of the TMP countdown child inside the XP Fountain prefab.")]
    [SerializeField] private string fountainCountdownObjectName = "FountainCountdown";

    [Header("HUD (TextMeshProUGUI)")]
    [SerializeField] private TextMeshProUGUI runTimerText;
    [SerializeField] private TextMeshProUGUI difficultyText;
    [SerializeField] private TextMeshProUGUI swarmStatusText;
    [SerializeField] private TextMeshProUGUI fountainStatusText;
    [SerializeField] private TextMeshProUGUI announcementText;

    [Header("Announcements")]
    [SerializeField] private Color swarmColor = new Color(1f, 0.3f, 0.2f);
    [SerializeField] private Color fountainColor = new Color(0.3f, 0.95f, 1f);
    [SerializeField] private AudioClip swarmWarningSound;
    [SerializeField] private AudioClip fountainSpawnSound;
    [SerializeField] private AudioClip empExplosionSound;
    [SerializeField] private AudioClip eliteDropCrateSound;

    public bool IsRunning { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public float RemainingSeconds => Mathf.Max(0f, runDuration - ElapsedSeconds);
    public float PlayerDifficulty
    {
        get
        {
            float value = playerStats != null ? playerStats.difficulty : 1f;
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(1f, value);
        }
    }
    public float SpawnRateMultiplier { get; private set; } = 1f;
    public float EnemyHealthMultiplier { get; private set; } = 1f;
    public float EnemySpeedMultiplier { get; private set; } = 1f;
    public float EnemyDamageMultiplier { get; private set; } = 1f;
    public float CurrentSwarmMultiplier { get; private set; } = 1f;
    public bool IsSwarmActive { get; private set; }
    public GameObject ActiveFountain { get; private set; }
    public float FountainSecondsRemaining => ActiveFountain != null
        ? Mathf.Max(0f, fountainExpiresAt - ElapsedSeconds) : 0f;

    public event Action<GameObject> FountainSpawned;
    public event Action FountainDespawned;

    private bool hasStarted;
    private bool[] swarmWarned;
    private float[] sortedFountainTimes;
    private int nextFountainIndex;
    private float fountainExpiresAt;
    private float nextFountainAttempt;
    private bool fountainPending;
    private bool fountainExpiryWarned;
    private bool fountainWasActive;
    private TextMeshProUGUI fountainDisplay;
    private int lastWorldFountainSeconds = -1;
    private Sequence announcementSequence;
    private Vector3 announcementBaseScale = Vector3.one;
    private int lastTimer = -1;
    private int lastDifficulty = -1;
    private int lastThreat = -1;
    private int lastSwarmSeconds = -1;
    private int lastSwarmMode = -1;
    private int lastFountainSeconds = -1;
    private int lastFountainMode = -1;

    public GameObject cratePrefab;
    public GameObject corruptedCratePrefab;
    public float crateSpawnInterval;
    public float corruptedCrateSpawnChance = 40;

    // Active enemies.
    private readonly List<HordeEnemy> activeEnemies =
        new List<HordeEnemy>();

    // Allows O(1) registration and removal.
    private readonly Dictionary<HordeEnemy, int> enemyIndices =
        new Dictionary<HordeEnemy, int>();

    // Every prefab has a separate inactive stack; variants of THAT prefab share its stack.
    // Consequently an Elite Ranged returns to the Ranged pool, not the Regular pool.
    private sealed class EnemyPool
    {
        internal readonly HordeEnemy prefab;
        internal readonly float weight;
        internal readonly float unlockSeconds;
        internal readonly int maxActive;
        internal readonly int prewarm;
        internal readonly bool isRanged;
        internal int activeCount;
        internal readonly Stack<HordeEnemy> inactive = new Stack<HordeEnemy>();

        internal EnemyPool(HordeEnemy prefab, float weight, float unlockSeconds,
            int maxActive, int prewarm)
        {
            this.prefab = prefab;
            // Prefab Awake has not necessarily run: inspect its component once here.
            HordeRangedAttack ranged = prefab.GetComponent<HordeRangedAttack>();
            this.isRanged = ranged != null && ranged.enabled;
            this.weight = Mathf.Max(0f, weight);
            this.unlockSeconds = Mathf.Max(0f, unlockSeconds);
            this.maxActive = Mathf.Max(0, maxActive);
            this.prewarm = Mathf.Max(0, prewarm);
        }
    }
    private readonly List<EnemyPool> enemyPools = new List<EnemyPool>();
    private readonly Dictionary<HordeEnemy, EnemyPool> owningPools =
        new Dictionary<HordeEnemy, EnemyPool>();
    private readonly HashSet<HordeEnemy> pooledEnemies =
        new HashSet<HordeEnemy>();
    private int inactiveEnemyCount;

    // Inactive parent for pooled enemies.
    private Transform poolRoot;

    private float tickTimer;

    private float spawnCredit;
    private int runActiveLimit = int.MaxValue;

    public bool WaveSpawningEnabled => spawnWaves;
    public float BaseEnemiesPerSecond => Mathf.Max(0, enemiesPerWave) / Mathf.Max(0.1f, spawnInterval);

    // Fractional spawn credit keeps the original average spawn rate but avoids large
    // simultaneous waves. Credit is discarded when capped or when spawn attempts fail.
    private void TickRunSpawning(float deltaTime, float rateMultiplier, int activeLimit, int maxPerFrame)
    {
        if (!IsRunning || !isActiveAndEnabled) return;
        runActiveLimit = Mathf.Max(1, activeLimit);
        if (!spawnWaves)
        {
            spawnCredit = 0f;
            return;
        }
        if (deltaTime <= 0f || player == null || enemyPools.Count == 0) return;

        int available = runActiveLimit - activeEnemies.Count;
        if (available <= 0)
        {
            spawnCredit = 0f;
            return;
        }

        int frameLimit = Mathf.Max(1, maxPerFrame);
        spawnCredit = Mathf.Min(frameLimit, spawnCredit + deltaTime * BaseEnemiesPerSecond * Mathf.Max(0f, rateMultiplier));
        int count = Mathf.Min(Mathf.FloorToInt(spawnCredit), available);
        spawnCredit -= count;
        for (int i = 0; i < count; i++)
        {
            if (TryGetSpawnPosition(out Vector3 position))
                SpawnEnemy(position);
        }

        if (activeEnemies.Count >= runActiveLimit) spawnCredit = 0f;
    }

    public Transform Player => player;

    public int ActiveEnemyCount => activeEnemies.Count;
    public int PooledEnemyCount => inactiveEnemyCount;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Multiple HordeManagers detected!");
            enabled = false;
            return;
        }

        Instance = this;
        if (footstepAudio == null)
            footstepAudio = GetComponent<HordeFootstepAudio>();

        if (playerStats == null && player != null)
            playerStats = player.GetComponentInParent<PlayerStats>();
        if (playerInventory == null && player != null)
            playerInventory = player.GetComponentInParent<PlayerInventory>();

        // HUD objects must not be the manager itself or one of its ancestors.
        if (announcementText != null && transform.IsChildOf(announcementText.transform))
        {
            Debug.LogWarning("HordeManager: Announcement Text cannot contain the manager.", this);
            announcementText = null;
        }
        if (announcementText != null)
        {
            announcementBaseScale = announcementText.rectTransform.localScale;
            announcementText.gameObject.SetActive(false);
        }

        if (rangedProjectilePool != null && player != null)
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health == null) health = player.GetComponentInParent<PlayerHealth>();
            if (health == null) health = player.GetComponentInChildren<PlayerHealth>(true);
            rangedProjectilePool.SetPlayer(health);
        }

        CreatePoolRoot();
        BuildEnemyPools();
        PrewarmPools();
    }


    private void OnDisable()
    {
        if (Instance != this) return;
        // This is a one-shot scene run. Ordinary menu pauses use Time.timeScale instead.
        IsRunning = false;
        spawnCredit = 0f;
        fountainPending = false;
        IsSwarmActive = false;
        CurrentSwarmMultiplier = 1f;
        RemoveFountain();
        KillAnnouncement();
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
        if (activeEnemies.Count == 0)
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

        bool hasPlayer = player != null;

        Vector3 playerPosition = hasPlayer
            ? player.position
            : Vector3.zero;

        bool hasCar = empTargetCar != null && empTargetCar.gameObject.activeInHierarchy;
        Vector3 carPosition = hasCar ? empTargetCar.transform.position : Vector3.zero;
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

            enemy.ManagedAnimationTick(elapsed);

            // EMPs chase the actual car, even when nobody is driving it.
            if (enemy.IsEMP && !hasCar)
            {
                DespawnEnemy(enemy);
                continue;
            }
            if (!enemy.IsEMP && !hasPlayer) continue;
            Vector3 targetPosition = enemy.IsEMP ? carPosition : playerPosition;
            float distanceSqr = (enemy.transform.position - targetPosition).sqrMagnitude;

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
                targetPosition
            );
        }

        // Reuse the existing enemy list and manager tick; no per-enemy audio Update().
        if (IsRunning && hasPlayer && footstepAudio != null)
            footstepAudio.ManagedTick(activeEnemies, player, elapsed);
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
        if (owningPools.TryGetValue(enemy, out EnemyPool pool)) pool.activeCount++;
        if (enemy.IsEMP) activeEMPCount++;
        if (enemy.IsRanged) activeRangedCount++;
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
        if (!object.ReferenceEquals(removed, null))
        {
            if (removed.IsEMP) activeEMPCount = Mathf.Max(0, activeEMPCount - 1);
            if (removed.IsRanged) activeRangedCount = Mathf.Max(0, activeRangedCount - 1);
            if (owningPools.TryGetValue(removed, out EnemyPool pool))
                pool.activeCount = Mathf.Max(0, pool.activeCount - 1);
        }

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

    // The original enemy prefab stays the default melee enemy. Add Ranged and later
    // Chunky through Additional Enemy Types; they only need the common HordeEnemy.
    private void BuildEnemyPools()
    {
        enemyPools.Clear();
        if (enemyPrefab != null)
            enemyPools.Add(new EnemyPool(enemyPrefab, defaultEnemyWeight, 0f,
                int.MaxValue, prewarmCount));

        if (additionalEnemyTypes == null) return;
        for (int i = 0; i < additionalEnemyTypes.Length; i++)
        {
            AdditionalEnemyType entry = additionalEnemyTypes[i];
            if (entry == null || entry.prefab == null) continue;
            bool duplicate = false;
            for (int j = 0; j < enemyPools.Count; j++)
                if (enemyPools[j].prefab == entry.prefab) { duplicate = true; break; }
            if (duplicate)
            {
                Debug.LogWarning("HordeManager: Duplicate enemy prefab in Additional Enemy Types was skipped.", this);
                continue;
            }
            enemyPools.Add(new EnemyPool(entry.prefab, entry.spawnWeight,
                entry.unlockSeconds, entry.maxActive, entry.prewarm));
        }
    }

    private HordeEnemy CreateEnemy(EnemyPool pool)
    {
        HordeEnemy enemy = Instantiate(pool.prefab, poolRoot);
        // The parent is inactive. Register ownership before OnEnable can fire.
        owningPools[enemy] = pool;
        enemy.gameObject.SetActive(true);
        enemy.xpBlobPool = xpBlobPool;
        enemy.deathParticlePool = deathParticlePool;
        enemy.playerInventory = playerInventory;
        // Activates the instance for first-time Awake/initialization.
        enemy.transform.SetParent(transform, false);
        enemy.PrepareForPool();
        enemy.gameObject.SetActive(false);
        enemy.transform.SetParent(poolRoot, false);
        return enemy;
    }

    private void PrewarmPools()
    {
        int budget = Mathf.Max(0, maxPoolSize);
        for (int i = 0; i < enemyPools.Count && inactiveEnemyCount < budget; i++)
        {
            EnemyPool pool = enemyPools[i];
            for (int j = 0; j < pool.prewarm && inactiveEnemyCount < budget; j++)
            {
                HordeEnemy enemy = CreateEnemy(pool);
                pool.inactive.Push(enemy);
                pooledEnemies.Add(enemy);
                inactiveEnemyCount++;
            }
        }
    }

    // Weighted across eligible prefabs, with NO allocations at spawn time.
    // The per-prefab active cap is checked BEFORE rolling the weight.
    private EnemyPool ChooseEnemyPool()
    {
        float sum = 0f;
        bool projectileReady = rangedProjectilePool != null &&
            rangedProjectilePool.isActiveAndEnabled && rangedProjectilePool.IsReady;

        for (int i = 0; i < enemyPools.Count; i++)
        {
            EnemyPool pool = enemyPools[i];
            if (pool.weight <= 0f || pool.activeCount >= pool.maxActive ||
                ElapsedSeconds < pool.unlockSeconds ||
                (pool.isRanged && !projectileReady)) continue;
            sum += pool.weight;
        }
        if (sum <= 0f) return null;

        float roll = Random.value * sum;
        for (int i = 0; i < enemyPools.Count; i++)
        {
            EnemyPool pool = enemyPools[i];
            if (pool.weight <= 0f || pool.activeCount >= pool.maxActive ||
                ElapsedSeconds < pool.unlockSeconds ||
                (pool.isRanged && !projectileReady)) continue;
            roll -= pool.weight;
            if (roll <= 0f) return pool;
        }
        return null;
    }

    public HordeEnemy SpawnEnemy(Vector3 position)
    {
        return SpawnEnemy(position, Quaternion.identity);
    }

    public HordeEnemy SpawnEnemy(Vector3 position, Quaternion rotation)
    {
        if (!IsRunning || activeEnemies.Count >= runActiveLimit) return null;
        EnemyPool pool = ChooseEnemyPool();
        return pool == null ? null : SpawnFromPool(pool, position, rotation);
    }

    // Optional explicit-spawn API for events/boss introductions.
    public HordeEnemy SpawnEnemyOfType(HordeEnemy prefab, Vector3 position, Quaternion rotation)
    {
        if (!IsRunning || activeEnemies.Count >= runActiveLimit) return null;
        for (int i = 0; i < enemyPools.Count; i++)
        {
            EnemyPool pool = enemyPools[i];
            if (pool.prefab == prefab && pool.activeCount < pool.maxActive)
            {
                if (pool.isRanged && (rangedProjectilePool == null ||
                    !rangedProjectilePool.isActiveAndEnabled || !rangedProjectilePool.IsReady))
                    return null;
                return SpawnFromPool(pool, position, rotation);
            }
        }
        return null;
    }

    private HordeEnemy SpawnFromPool(EnemyPool pool, Vector3 position, Quaternion rotation)
    {
        HordeEnemy enemy = null;
        while (pool.inactive.Count > 0 && enemy == null)
        {
            enemy = pool.inactive.Pop();
            inactiveEnemyCount = Mathf.Max(0, inactiveEnemyCount - 1);
            if (!object.ReferenceEquals(enemy, null)) pooledEnemies.Remove(enemy);
            if (enemy == null && !object.ReferenceEquals(enemy, null))
                owningPools.Remove(enemy); // externally destroyed pooled instance
        }
        if (enemy == null) enemy = CreateEnemy(pool);
        enemy.transform.SetParent(null, true);
        enemy.transform.SetPositionAndRotation(position, rotation);
        enemy.xpBlobPool = xpBlobPool;
        enemy.deathParticlePool = deathParticlePool;
        enemy.playerInventory = playerInventory;

        // Type is fixed by prefab. Elite/EMP are optional spawn-time modifiers.
        HordeEnemyVariant modifier = RollVariant(enemy, position);
        float health = EnemyHealthMultiplier;
        float speed = EnemySpeedMultiplier;
        float damage = EnemyDamageMultiplier;
        float scale = 1f;
        float motion = 1f;
        if (modifier == HordeEnemyVariant.Elite)
        {
            health *= Mathf.Max(1f, eliteHealthMultiplier);
            motion = Mathf.Max(1f, eliteSpeedMultiplier);
            speed *= motion;
            damage *= Mathf.Max(1f, eliteDamageMultiplier);
            scale = Mathf.Max(1f, eliteScale);
        }
        else if (modifier == HordeEnemyVariant.EMP)
        {
            health *= Mathf.Max(0.01f, empHealthMultiplier);
            motion = Mathf.Max(1f, empSpeedMultiplier);
            speed *= motion;
            damage = 0f;
            nextEMPSpawnSeconds = ElapsedSeconds + Mathf.Max(0f, empSpawnCooldown);
        }
        enemy.ResetForSpawn(health, speed, damage, modifier, scale, motion);
        enemy.gameObject.SetActive(true);
        return enemy;
    }

    private static bool RollChance(float chance)
    {
        return chance >= 1f || (chance > 0f && Random.value < chance);
    }

    private HordeEnemyVariant RollVariant(HordeEnemy enemy, Vector3 spawnPosition)
    {
        // Keep these mutually exclusive FOR NOW (as in the original game).
        // Both rolls are independent of the prefab/archetype selection.
        if (enemy.CanBeEMP && CanSpawnEMP(spawnPosition) && RollChance(empSpawnChance))
            return HordeEnemyVariant.EMP;
        if (enemy.CanBeElite && RollChance(eliteSpawnChance))
            return HordeEnemyVariant.Elite;
        return HordeEnemyVariant.Normal;
    }

    internal Vector3 GetRangedAimPoint(Vector3 playerPosition, float targetHeight)
    {
        return rangedProjectilePool != null
            ? rangedProjectilePool.GetAimPoint(playerPosition, targetHeight)
            : playerPosition + Vector3.up * targetHeight;
    }

    internal bool FireRangedProjectile(Vector3 origin, Vector3 direction, float damage,
        HordeEnemy shooter)
    {
        return rangedProjectilePool != null &&
            rangedProjectilePool.TryFire(origin, direction, damage, shooter);
    }

    private bool CanSpawnEMP(Vector3 spawnPosition)
    {
        if (empTargetCar == null || !empTargetCar.isActiveAndEnabled ||
            empTargetCar.IsVehicleWrecked || empTargetCar.IsEmpDisabled || player == null ||
            ElapsedSeconds < Mathf.Max(0f, empStartSeconds) || ElapsedSeconds < nextEMPSpawnSeconds ||
            activeEMPCount >= Mathf.Max(0, maxActiveEMP)) return false;

        Vector3 carPosition = empTargetCar.transform.position;
        float playerLimit = Mathf.Max(0f, empMaxPlayerDistanceFromCar);
        if ((player.position - carPosition).sqrMagnitude > playerLimit * playerLimit) return false;
        float min = Mathf.Max(0f, empMinDistanceFromCar);
        float max = Mathf.Max(min, empMaxDistanceFromCar);
        float distanceSqr = (spawnPosition - carPosition).sqrMagnitude;
        return distanceSqr >= min * min && distanceSqr <= max * max;
    }

    internal void PlayEMPImpact(Vector3 position)
    {
        // Play EMP sound independently of particles.
        if (empExplosionSound != null)
        {
            AudioManager.Play3D(
                empExplosionSound,
                position,
                1
            );
        }

        // Spawn EMP explosion particles.
        if (empImpactParticlesPrefab == null)
            return;

        ParticleSystem effect = Instantiate(
            empImpactParticlesPrefab,
            position,
            Quaternion.identity
        );

        effect.Play(true);

        Destroy(
            effect.gameObject,
            Mathf.Max(0.1f, empImpactEffectLifetime)
        );
    }

    internal void TryDropEliteCrate(Vector3 position)
    {
        GameObject prefab = eliteCratePrefab != null ? eliteCratePrefab : cratePrefab;
        if (prefab == null || !RollChance(eliteCrateDropChance)) return;
        // A climbing enemy may die well above the ground. Drop at a walkable surface.
        float height = Mathf.Max(1f, raycastHeight);
        if (!Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down,
            out RaycastHit hit, height, groundLayer, QueryTriggerInteraction.Ignore)) return;
        if (terrainGenerator != null && !terrainGenerator.IsInsidePlayableArea(hit.point)) return;
        Instantiate(prefab, hit.point + Vector3.up * eliteCrateGroundOffset, Quaternion.identity);
        AudioManager.Play3D(eliteDropCrateSound, position, 1, 1, 1, 50);
    }

    public void DespawnEnemy(HordeEnemy enemy)
    {
        if (enemy == null || pooledEnemies.Contains(enemy)) return;

        enemy.PrepareForPool();
        enemy.gameObject.SetActive(false);
        if (!owningPools.TryGetValue(enemy, out EnemyPool pool) ||
            inactiveEnemyCount >= Mathf.Max(0, maxPoolSize))
        {
            owningPools.Remove(enemy);
            Destroy(enemy.gameObject);
            return;
        }
        enemy.transform.SetParent(poolRoot, false);
        pool.inactive.Push(enemy);
        pooledEnemies.Add(enemy);
        inactiveEnemyCount++;
    }

    // ------------------------------------------------
    // SPAWN POSITION
    // ------------------------------------------------


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

        if (terrainGenerator != null && !terrainGenerator.IsInsidePlayableArea(hit.point))
            return false;

        spawnPosition = hit.point + Vector3.up * enemySpawnHeight;

        return true;
    }

    // ==================================================
    // RUN CLOCK, DIFFICULTY, SWARMS, FOUNTAINS AND HUD
    // ==================================================

    private void Start()
    {
        if (Instance != this) return;
        if (empTargetCar == null && player != null)
        {
            PlayerRadio radio = player.GetComponentInChildren<PlayerRadio>(true);
            if (radio == null) radio = player.GetComponentInParent<PlayerRadio>();
            if (radio != null) empTargetCar = radio.carController;
        }
        if (empTargetCar == null) empTargetCar = FindObjectOfType<CarController>();
        if (empTargetCar == null)
        {
            for (int i = 0; i < enemyPools.Count; i++)
                if (enemyPools[i].prefab.CanBeEMP)
                {
                    Debug.LogWarning("HordeManager: No car assigned for EMP enemies.", this);
                    break;
                }
        }
        for (int i = 0; i < enemyPools.Count; i++)
            if (enemyPools[i].isRanged &&
                (rangedProjectilePool == null || !rangedProjectilePool.IsReady))
            {
                Debug.LogWarning("HordeManager: Ranged type configured without a ready projectile pool.", this);
                break;
            }
        if (startAutomatically) BeginRun();
        else RefreshHUD(0f, 0f);

        InvokeRepeating(nameof(SpawnCrates), 3, crateSpawnInterval);
    }

    public void BeginRun()
    {
        if (Instance != this || hasStarted || !isActiveAndEnabled) return;
        if (player == null || enemyPools.Count == 0)
        {
            Debug.LogError("HordeManager: Assign Player and at least one enemy prefab.", this);
            return;
        }
        if (playerStats == null)
            playerStats = player.GetComponentInParent<PlayerStats>();
        if (playerInventory == null)
            playerInventory = player.GetComponentInParent<PlayerInventory>();
        if (playerStats == null)
            Debug.LogWarning("HordeManager: PlayerStats not found. Difficulty remains at 100%.", this);
        if (terrainGenerator == null || xpFountainPrefab == null)
            Debug.LogWarning("HordeManager: TerrainGenerator or fountain prefab missing. Fountain events disabled.", this);

        hasStarted = true;
        ElapsedSeconds = 0f;
        IsRunning = true;
        swarmWarned = new bool[swarms != null ? swarms.Length : 0];
        sortedFountainTimes = fountainSpawnTimes != null ? (float[])fountainSpawnTimes.Clone() : new float[0];
        Array.Sort(sortedFountainTimes);
        nextFountainIndex = 0;
        spawnCredit = 0f;
        runActiveLimit = Mathf.Max(1, normalActiveEnemyLimit);
        CalculateThreat();
        RefreshHUD(0f, 0f);
    }

    private void Update()
    {
        if (!IsRunning) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return; // upgrade menus, item menus and ordinary pauses freeze all events
        // The countdown stops at zero, but elapsed time and gameplay continue.
        // Another system can decide what happens when the countdown expires.
        ElapsedSeconds += dt;

        CalculateThreat();
        UpdateSwarms(out float swarmRemaining, out float warningRemaining);
        UpdateFountains();
        if (!IsRunning) return; // a fountain event listener may have disabled this manager
        float rate = SpawnRateMultiplier * CurrentSwarmMultiplier;
        if (BaseEnemiesPerSecond > 0f)
            rate = Mathf.Min(rate, Mathf.Max(0.1f, maxEnemiesPerSecond) / BaseEnemiesPerSecond);
        TickRunSpawning(dt, rate,
            IsSwarmActive ? Mathf.Max(normalActiveEnemyLimit, swarmActiveEnemyLimit) : normalActiveEnemyLimit,
            maxSpawnsPerFrame);

        RefreshHUD(swarmRemaining, warningRemaining);
    }

    private void CalculateThreat()
    {
        float progress = Mathf.Clamp01(ElapsedSeconds / Mathf.Max(1f, runDuration));
        float ramp = Mathf.Pow(progress, Mathf.Max(0.01f, timeScalingExponent));
        float extra = PlayerDifficulty - 1f;
        SpawnRateMultiplier = Scale(endSpawnMultiplier, ramp, extra, difficultySpawnInfluence);
        EnemyHealthMultiplier = Scale(endHealthMultiplier, ramp, extra, difficultyHealthInfluence);
        EnemySpeedMultiplier = Scale(endSpeedMultiplier, ramp, extra, difficultySpeedInfluence);
        EnemyDamageMultiplier = Scale(endDamageMultiplier, ramp, extra, difficultyDamageInfluence);
    }

    private static float Scale(float end, float ramp, float extra, float influence)
    {
        return Mathf.Lerp(1f, Mathf.Max(1f, end), ramp) * (1f + extra * Mathf.Max(0f, influence));
    }

    private void UpdateSwarms(out float activeRemaining, out float warningRemaining)
    {
        bool wasActive = IsSwarmActive;
        IsSwarmActive = false;
        CurrentSwarmMultiplier = 1f;
        activeRemaining = 0f;
        warningRemaining = 0f;
        bool newWarning = false;
        bool startedWithoutWarning = false;
        if (swarms != null)
        {
            if (swarmWarned.Length != swarms.Length)
                Array.Resize(ref swarmWarned, swarms.Length);
            for (int i = 0; i < swarms.Length; i++)
            {
                SwarmEvent swarm = swarms[i];
                if (swarm == null || swarm.startSeconds < 0f) continue;
                float end = swarm.startSeconds + Mathf.Max(0.1f, swarm.duration);
                if (ElapsedSeconds >= end) continue;
                if (ElapsedSeconds >= swarm.startSeconds)
                {
                    if (!swarmWarned[i]) startedWithoutWarning = true;
                    swarmWarned[i] = true;
                    IsSwarmActive = true;
                    CurrentSwarmMultiplier = Mathf.Max(CurrentSwarmMultiplier, swarm.spawnMultiplier);
                    activeRemaining = Mathf.Max(activeRemaining, end - ElapsedSeconds);
                }
                else if (ElapsedSeconds >= swarm.startSeconds - Mathf.Max(0f, swarmWarningSeconds))
                {
                    float until = swarm.startSeconds - ElapsedSeconds;
                    warningRemaining = warningRemaining <= 0f ? until : Mathf.Min(warningRemaining, until);
                    if (!swarmWarned[i]) { swarmWarned[i] = true; newWarning = true; }
                }
            }
        }
        if (IsSwarmActive && !wasActive)
            Announce("HORDE INCOMING!", swarmColor, (newWarning || startedWithoutWarning) ? swarmWarningSound : null, 4);
        else if (newWarning)
            Announce("HORDE INCOMING", swarmColor, swarmWarningSound, 4);
        else if (wasActive && !IsSwarmActive)
            Announce("HORDE SUBSIDING", fountainColor, null, 1);
    }

    private void UpdateFountains()
    {
        if (terrainGenerator == null || xpFountainPrefab == null) return;

        // If a fountain consumes itself on interaction, don't spawn a replacement for this window.
        if (fountainWasActive && (ActiveFountain == null || !ActiveFountain.activeSelf)) RemoveFountain();
        if (!IsRunning) return;
        if (ActiveFountain != null && ElapsedSeconds >= fountainExpiresAt)
        {
            RemoveFountain();
            if (!IsRunning) return;
            Announce("XP FOUNTAIN CLOSED", fountainColor, null, 1);
        }

        while (nextFountainIndex < sortedFountainTimes.Length && ElapsedSeconds >= sortedFountainTimes[nextFountainIndex])
        {
            float start = sortedFountainTimes[nextFountainIndex++];
            if (start < 0f) continue;
            float expiry = start + Mathf.Max(1f, fountainLifetime);
            if (ElapsedSeconds >= expiry) continue; // never replay expired events on a hitch
            RemoveFountain();
            if (!IsRunning) return;
            fountainExpiresAt = expiry;
            fountainPending = true;
            fountainExpiryWarned = false;
            nextFountainAttempt = ElapsedSeconds;
        }

        if (fountainPending && ElapsedSeconds >= fountainExpiresAt) fountainPending = false;
        if (fountainPending && ElapsedSeconds >= nextFountainAttempt)
        {
            nextFountainAttempt = ElapsedSeconds + Mathf.Max(0.1f, fountainRetryInterval);
            Transform anchor = fountainSpawnAnchor != null ? fountainSpawnAnchor : player;
            if (anchor != null)
            {
                ActiveFountain = terrainGenerator.SpawnRuntimePOI(xpFountainPrefab, anchor.position, fountainMinDistance, fountainMaxDistance);
                XPFountain xpFountainScript = ActiveFountain.GetComponent<XPFountain>();
                xpFountainScript.levelSystem = playerLevelSystem;
                xpFountainScript.rewardSystem = playerLevelRewardSystem;
                xpFountainScript.SpawnIn();
                if (ActiveFountain != null)
                {
                    fountainPending = false;
                    fountainWasActive = true;
                    BindFountainDisplay();
                    UpdateFountainDisplay();
                    Announce("XP FOUNTAIN APPEARED", fountainColor, fountainSpawnSound, 2);
                    FountainSpawned?.Invoke(ActiveFountain);
                    if (!IsRunning) return;
                }
            }
        }

        if (ActiveFountain != null)
        {
            UpdateFountainDisplay();
            if (!fountainExpiryWarned && FountainSecondsRemaining <= fountainExpiryWarning)
            {
                fountainExpiryWarned = true;
                Announce("XP FOUNTAIN CLOSING SOON", fountainColor, null, 2);
            }
        }
    }

    private void RemoveFountain()
    {
        bool notify = fountainWasActive || ActiveFountain != null;
        if (ActiveFountain != null)
        {
            XPFountain xpFountainScript = ActiveFountain.GetComponent<XPFountain>();
            xpFountainScript.Leave();
        }
        ActiveFountain = null;
        fountainDisplay = null;
        lastWorldFountainSeconds = -1;
        fountainWasActive = false;
        if (notify) FountainDespawned?.Invoke();
    }

    private int announcementPriority;
    private void Announce(string message, Color color, AudioClip clip, int priority)
    {
        AudioManager.Play2D(clip);
        if (announcementText == null || (announcementSequence != null && priority < announcementPriority)) return;
        KillAnnouncement();
        announcementPriority = priority;
        announcementText.gameObject.SetActive(true);
        announcementText.text = message;
        announcementText.color = color;
        announcementText.alpha = 0f;
        announcementText.rectTransform.localScale = announcementBaseScale * 0.75f;
        // Generic DOTween alpha tween avoids depending on DOTween's optional TMP module.
        announcementSequence = DOTween.Sequence().SetUpdate(false);
        announcementSequence.Append(announcementText.rectTransform.DOScale(announcementBaseScale, 0.25f).SetEase(Ease.OutBack));
        announcementSequence.Join(DOTween.To(() => announcementText.alpha, x => announcementText.alpha = x, 1f, 0.18f));
        announcementSequence.AppendInterval(2f);
        announcementSequence.Append(DOTween.To(() => announcementText.alpha, x => announcementText.alpha = x, 0f, 0.3f));
        announcementSequence.OnComplete(() =>
        {
            announcementSequence = null;
            announcementPriority = 0;
            if (announcementText != null) announcementText.gameObject.SetActive(false);
        });
    }

    private void KillAnnouncement()
    {
        announcementSequence?.Kill();
        announcementSequence = null;
        announcementPriority = 0;
        if (announcementText != null)
        {
            announcementText.rectTransform.localScale = announcementBaseScale;
            announcementText.gameObject.SetActive(false);
        }
    }

    private void RefreshHUD(float swarmRemaining, float warningRemaining)
    {
        int seconds = Mathf.CeilToInt(RemainingSeconds);
        if (runTimerText != null && seconds != lastTimer)
        {
            lastTimer = seconds;
            runTimerText.text = FormatTime(seconds);
        }
        int difficulty = Mathf.RoundToInt(PlayerDifficulty * 100f);
        int threat = Mathf.RoundToInt(SpawnRateMultiplier * 100f);
        if (difficultyText != null && (difficulty != lastDifficulty || threat != lastThreat))
        {
            lastDifficulty = difficulty; lastThreat = threat;
            difficultyText.text = $"DIFFICULTY {difficulty}%  |  THREAT {threat}%";
        }

        int swarmMode = !IsRunning ? 0 : IsSwarmActive ? 2 : warningRemaining > 0f ? 1 : 0;
        int swarmSeconds = Mathf.CeilToInt(IsSwarmActive ? swarmRemaining : warningRemaining);
        if (swarmStatusText != null && (swarmMode != lastSwarmMode || swarmSeconds != lastSwarmSeconds))
        {
            lastSwarmMode = swarmMode; lastSwarmSeconds = swarmSeconds;
            swarmStatusText.text = swarmMode == 2 ? $"HORDE ACTIVE  {FormatTime(swarmSeconds)}" :
                swarmMode == 1 ? $"HORDE INCOMING  {FormatTime(swarmSeconds)}" : "";
        }

        int fountainMode = IsRunning && ActiveFountain != null ? 1 : 0;
        int fountainSeconds = Mathf.CeilToInt(FountainSecondsRemaining);
        if (fountainStatusText != null && (fountainMode != lastFountainMode || fountainSeconds != lastFountainSeconds))
        {
            lastFountainMode = fountainMode; lastFountainSeconds = fountainSeconds;
            fountainStatusText.text = fountainMode == 1 ? $"XP FOUNTAIN  {FormatTime(fountainSeconds)}" : "";
        }
    }

    private static string FormatTime(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

    private void BindFountainDisplay()
    {
        fountainDisplay = null;
        lastWorldFountainSeconds = -1;
        if (ActiveFountain == null || string.IsNullOrEmpty(fountainCountdownObjectName)) return;

        // Only bind the explicitly named child; never overwrite the fountain's other labels.
        foreach (TextMeshProUGUI text in ActiveFountain.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (string.Equals(text.gameObject.name, fountainCountdownObjectName, StringComparison.Ordinal))
            {
                fountainDisplay = text;
                break;
            }
        }
    }

    private void UpdateFountainDisplay()
    {
        if (fountainDisplay == null) return;
        int seconds = Mathf.CeilToInt(FountainSecondsRemaining);
        if (seconds == lastWorldFountainSeconds) return;
        lastWorldFountainSeconds = seconds;
        fountainDisplay.text = FormatTime(seconds);
    }

    public void SpawnCrates()
    {
        if (RemainingSeconds > 5)
        {
            terrainGenerator.SpawnRuntimePOI(Random.Range(0, 100) < corruptedCrateSpawnChance ? corruptedCratePrefab : cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);
            terrainGenerator.SpawnRuntimePOI(Random.Range(0, 100) < corruptedCrateSpawnChance ? corruptedCratePrefab : cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);

            //for (int i = 0; i < 10; i++)
            //{
            //    int r = Random.Range(0, 100);

            //    if (r < (playerStats.itemLuck))
            //    {
            //        terrainGenerator.SpawnRuntimePOI(Random.Range(0, 100) < corruptedCrateSpawnChance ? corruptedCratePrefab : cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);
            //    }
            //}
        }
    }

}
