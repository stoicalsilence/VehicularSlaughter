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
    [SerializeField] private XPBlobPool xpBlobPool;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private DeathParticlePool deathParticlePool;
    public PlayerLevelSystem playerLevelSystem;
    public PlayerLevelRewardSystem playerLevelRewardSystem;

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
    public float crateSpawnInterval;

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
        if (deltaTime <= 0f || player == null || enemyPrefab == null) return;

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

        CreatePoolRoot();
        PrewarmPool();
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

            if (!hasPlayer)
                continue;

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
        enemy.playerInventory = playerInventory;

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
        if (!IsRunning || activeEnemies.Count >= runActiveLimit)
            return null;

        if (enemyPrefab == null)
        {
            Debug.LogError("HordeManager: Missing enemy prefab.");
            return null;
        }

        HordeEnemy enemy = null;
        // A pooled GameObject may have been destroyed by another system.
        while (enemyPool.Count > 0 && enemy == null)
        {
            enemy = enemyPool.Pop();
            pooledEnemies.Remove(enemy);
        }
        if (enemy == null)
            enemy = CreateEnemy();

        enemy.transform.SetParent(null, true);

        enemy.transform.SetPositionAndRotation(
            position,
            rotation
        );

        // Always assign dependencies and reset, including newly
        // instantiated enemies that weren't previously pooled.
        enemy.xpBlobPool = xpBlobPool;
        enemy.deathParticlePool = deathParticlePool;

        enemy.playerInventory = playerInventory;
        enemy.ResetForSpawn(
            EnemyHealthMultiplier,
            EnemySpeedMultiplier,
            EnemyDamageMultiplier
        );

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
        if (startAutomatically) BeginRun();
        else RefreshHUD(0f, 0f);

        InvokeRepeating(nameof(SpawnCrates), 3, crateSpawnInterval);
    }

    public void BeginRun()
    {
        if (Instance != this || hasStarted || !isActiveAndEnabled) return;
        if (player == null || enemyPrefab == null)
        {
            Debug.LogError("HordeManager: Assign the Player target and Enemy Prefab.", this);
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
            Announce("SWARM ACTIVE!", swarmColor, (newWarning || startedWithoutWarning) ? swarmWarningSound : null, 4);
        else if (newWarning)
            Announce("SWARM INCOMING", swarmColor, swarmWarningSound, 4);
        else if (wasActive && !IsSwarmActive)
            Announce("SWARM SUBSIDING", fountainColor, null, 1);
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
                xpFountainScript.rewardSystem= playerLevelRewardSystem;
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
            swarmStatusText.text = swarmMode == 2 ? $"SWARM ACTIVE  {FormatTime(swarmSeconds)}" :
                swarmMode == 1 ? $"SWARM INCOMING  {FormatTime(swarmSeconds)}" : "";
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
            terrainGenerator.SpawnRuntimePOI(cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);
            terrainGenerator.SpawnRuntimePOI(cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);

            for(int i = 0; i < 10; i++)
            {
                int r = Random.Range(0, 100);

                if(r < (playerStats.itemLuck))
                {
                    terrainGenerator.SpawnRuntimePOI(cratePrefab, player.position, fountainMinDistance, fountainMaxDistance, true);
                }
            }
        }
    }

}
