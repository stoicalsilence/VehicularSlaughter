using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ScatterBombs : MonoBehaviour
{
    private const string ItemName = "Scatter Bombs";
    private const float ChancePerStack = 0.05f;

    [Header("References")]
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private GameObject explosionPrefab;
    [Tooltip("Optional visual-only bomb prefab. No Rigidbody or Collider needed.")]
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private LayerMask groundLayers;

    [Header("Scatter")]
    [SerializeField, Min(0f)] private float scatterRadius = 3.5f;
    [SerializeField, Min(0f)] private float arcHeight = 1.5f;
    [SerializeField, Min(0.05f)] private float minFuse = 0.55f;
    [SerializeField, Min(0.05f)] private float maxFuse = 0.95f;
    [SerializeField, Min(0f)] private float spawnHeight = 0.6f;
    [SerializeField, Min(0f)] private float landingOffset = 0.15f;
    [SerializeField, Min(0.1f)] private float groundSearchDepth = 30f;

    [Header("Explosion")]
    [SerializeField, Min(0f)] private float damage = 15f;
    [SerializeField, Min(0.01f)] private float damageRadius = 2f;
    [SerializeField, Min(0f)] private float knockback = 3f;
    [SerializeField, Min(0.01f)] private float explosionScale = 0.3f;
    [SerializeField, Min(0.01f)] private float explosionLifetime = 3f;
    [SerializeField, Min(0f)] private float damageCenterHeight = 0.5f;
    [Tooltip("Allow kills caused by these explosions to trigger Scatter Bombs again.")]
    [SerializeField] private bool allowChainReactions = true;

    [Header("Performance")]
    [SerializeField, Min(1)] private int maxExplosionsPerFrame = 12;
    [SerializeField, Min(0)] private int maxPooledVisuals = 128;

    private struct Bomb
    {
        public Vector3 start;
        public Vector3 end;
        public Vector3 explosionPosition;
        public float startTime;
        public float duration;
        public GameObject visual;
    }

    private readonly List<Bomb> activeBombs = new List<Bomb>(128);
    private readonly Stack<GameObject> visualPool = new Stack<GameObject>(128);
    private readonly List<Vector3> readyExplosions = new List<Vector3>(16);
    private int stacks;
    private bool isDetonating;

    private void Awake()
    {
        if (playerInventory == null)
            playerInventory = GetComponent<PlayerInventory>();
    }

    private void OnEnable()
    {
        if (playerInventory == null || explosionPrefab == null)
        {
            Debug.LogError("ScatterBombs: Assign PlayerInventory and explosion prefab.", this);
            enabled = false;
            return;
        }

        playerInventory.InventoryChanged += RefreshStacks;
        playerInventory.OnItemKill += OnEnemyKilled;
        RefreshStacks();
    }

    private void RefreshStacks()
    {
        stacks = playerInventory.GetStacksByName(ItemName);
    }

    private void OnEnemyKilled(GameObject enemy)
    {
        if (stacks <= 0 || enemy == null)
            return;

        if (isDetonating && !allowChainReactions)
            return;

        float chance = Mathf.Clamp01(stacks * ChancePerStack);
        if (chance < 1f && Random.value >= chance)
            return;

        // Snapshot the position before the enemy is returned to its pool.
        Vector3 origin = enemy.transform.position;
        int bombCount = stacks;
        float now = Time.time;

        for (int i = 0; i < bombCount; i++)
            SpawnBomb(origin, now);
    }

    private void SpawnBomb(Vector3 origin, float now)
    {
        Vector2 offset = Random.insideUnitCircle * scatterRadius;
        Vector3 landing = origin + new Vector3(offset.x, 0f, offset.y);
        Vector3 rayOrigin = landing + Vector3.up * 2f;

        // One ground query per spawned bomb, never one per frame.
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                groundSearchDepth + 2f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            landing = hit.point;
        }
        // If no ground is found, keep the original death height.

        Vector3 start = origin + Vector3.up * spawnHeight;
        float fuseMin = Mathf.Max(0.05f, minFuse);
        float fuseMax = Mathf.Max(fuseMin, maxFuse);

        activeBombs.Add(new Bomb
        {
            start = start,
            end = landing + Vector3.up * landingOffset,
            explosionPosition = landing,
            startTime = now,
            duration = Random.Range(fuseMin, fuseMax),
            visual = RentVisual(start)
        });
    }

    private void Update()
    {
        if (activeBombs.Count == 0 || Time.timeScale <= 0f)
            return;

        float now = Time.time;
        int remainingBudget = Mathf.Max(1, maxExplosionsPerFrame);

        // Compact surviving bombs in place, keeping their original order.
        // Resolve damage only after the list is stable, because explosions can
        // synchronously trigger new kills and append more bombs.
        readyExplosions.Clear();
        int count = activeBombs.Count;
        int writeIndex = 0;

        for (int i = 0; i < count; i++)
        {
            Bomb bomb = activeBombs[i];
            float t = Mathf.Clamp01((now - bomb.startTime) / bomb.duration);

            if (bomb.visual != null)
            {
                Vector3 position = Vector3.Lerp(bomb.start, bomb.end, t);
                position.y += 4f * arcHeight * t * (1f - t);
                bomb.visual.transform.position = position;
            }

            if (t >= 1f && remainingBudget > 0)
            {
                remainingBudget--;
                readyExplosions.Add(bomb.explosionPosition);
                ReturnVisual(bomb.visual);
            }
            else
            {
                activeBombs[writeIndex++] = bomb;
            }
        }

        if (writeIndex < count)
            activeBombs.RemoveRange(writeIndex, count - writeIndex);

        for (int i = 0; i < readyExplosions.Count; i++)
        {
            Vector3 position = readyExplosions[i];
            isDetonating = true;
            try
            {
                ExplosionUtility.Explode(
                    explosionPrefab,
                    position,
                    position + Vector3.up * damageCenterHeight,
                    damageRadius,
                    damage,
                    knockback,
                    explosionScale,
                    explosionLifetime);
            }
            finally
            {
                isDetonating = false;
            }

            // A damage callback may disable this component and clear the list.
            if (!isActiveAndEnabled)
                return;
        }
    }

    private GameObject RentVisual(Vector3 position)
    {
        if (bombPrefab == null)
            return null;

        GameObject visual = null;
        while (visualPool.Count > 0 && visual == null)
            visual = visualPool.Pop();

        if (visual == null)
            visual = Instantiate(bombPrefab, position, Quaternion.identity);
        else
            visual.transform.SetPositionAndRotation(position, Quaternion.identity);

        visual.SetActive(true);
        return visual;
    }

    private void ReturnVisual(GameObject visual)
    {
        if (visual == null)
            return;

        visual.SetActive(false);
        if (visualPool.Count < maxPooledVisuals)
            visualPool.Push(visual);
        else
            Destroy(visual);
    }

    private void OnDisable()
    {
        if (playerInventory != null)
        {
            playerInventory.InventoryChanged -= RefreshStacks;
            playerInventory.OnItemKill -= OnEnemyKilled;
        }

        for (int i = 0; i < activeBombs.Count; i++)
            ReturnVisual(activeBombs[i].visual);

        activeBombs.Clear();
        readyExplosions.Clear();
        stacks = 0;
    }

    private void OnDestroy()
    {
        while (visualPool.Count > 0)
        {
            GameObject visual = visualPool.Pop();
            if (visual != null)
                Destroy(visual);
        }
    }
}
