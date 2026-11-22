using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One shared projectile updater for every Ranged prefab. A projectile is a transform
/// and cached data, not a Rigidbody or a MonoBehaviour with its own Update.
/// Uses swept non-allocating physics against world/player colliders, never Enemy layer 7.
/// </summary>
[DisallowMultipleComponent]
public sealed class HordeProjectilePool : MonoBehaviour
{
    [Header("Visual Only (no Rigidbody / Collider / update script)")]
    [SerializeField] private Transform projectileVisualPrefab;

    [Header("Projectile Motion")]
    [SerializeField, Min(0.1f)] private float projectileSpeed = 24f;
    [SerializeField, Min(0.01f)] private float projectileRadius = 0.22f;
    [SerializeField, Min(0.05f)] private float projectileLifetime = 2.5f;
    [Tooltip("Colliders for player/world. Enemy Layer 7 is always excluded.")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Header("Global Projectile Budget")]
    [SerializeField, Min(0)] private int prewarmCount = 24;
    [SerializeField, Min(1)] private int maxActiveProjectiles = 128;

    private sealed class Flight
    {
        internal Transform visual;
        internal TrailRenderer trail;
        internal Vector3 direction;
        internal float damage;
        internal float lifetimeLeft;
        internal HordeEnemy owner;
        internal uint ownerGeneration;
    }

    private readonly List<Flight> active = new List<Flight>(64);
    private readonly Stack<Flight> inactive = new Stack<Flight>(64);
    private PlayerHealth playerHealth;
    private PlayerVehicleInteractor vehicleInteractor;
    private Transform playerRoot;
    private int effectiveCollisionMask;

    public bool IsReady => projectileVisualPrefab != null && playerHealth != null;
    public int ActiveProjectiles => active.Count;

    public void SetPlayer(PlayerHealth health)
    {
        playerHealth = health;
        playerRoot = health != null ? health.transform : null;
        vehicleInteractor = health != null ? health.GetComponent<PlayerVehicleInteractor>() : null;
    }

    // Use the car body while driving: the player's CharacterController is disabled.
    // The interactor is cached once; no GetComponent lookups per shot.
    public Vector3 GetAimPoint(Vector3 playerPosition, float playerHeight)
    {
        if (vehicleInteractor != null && vehicleInteractor.IsInVehicle &&
            vehicleInteractor.currentCarController != null)
        {
            CarController car = vehicleInteractor.currentCarController;
            Rigidbody body = car.GetComponent<Rigidbody>();
            // World center of mass is generally inside the vehicle's solid body.
            // Slight upward bias avoids aiming into the road under the car.
            return (body != null ? body.worldCenterOfMass : car.transform.position)
                + Vector3.up * 0.45f;
        }
        return playerPosition + Vector3.up * playerHeight;
    }

    private void Awake()
    {
        effectiveCollisionMask = collisionLayers.value & ~(1 << 7);
        if (projectileVisualPrefab == null) return;
        int count = Mathf.Min(Mathf.Max(0, prewarmCount), Mathf.Max(1, maxActiveProjectiles));
        for (int i = 0; i < count; i++) inactive.Push(CreateFlight());
    }

    private Flight CreateFlight()
    {
        Transform visual = Instantiate(projectileVisualPrefab, transform);
        visual.gameObject.SetActive(false);
        return new Flight
        {
            visual = visual,
            trail = visual.GetComponentInChildren<TrailRenderer>(true)
        };
    }

    public bool TryFire(Vector3 origin, Vector3 direction, float damage, HordeEnemy shooter)
    {
        if (!isActiveAndEnabled || !IsReady || damage <= 0f || shooter == null ||
            active.Count >= Mathf.Max(1, maxActiveProjectiles) ||
            direction.sqrMagnitude < 0.0001f)
            return false;

        Flight shot = inactive.Count > 0 ? inactive.Pop() : CreateFlight();
        shot.direction = direction.normalized;
        shot.damage = damage;
        shot.lifetimeLeft = Mathf.Max(0.05f, projectileLifetime);
        shot.owner = shooter;
        shot.ownerGeneration = shooter.SpawnGeneration;
        shot.visual.SetPositionAndRotation(origin, Quaternion.LookRotation(shot.direction));
        shot.visual.gameObject.SetActive(true);
        if (shot.trail != null) shot.trail.Clear();
        active.Add(shot);
        return true;
    }

    private void FixedUpdate()
    {
        if (active.Count == 0) return;

        float dt = Time.fixedDeltaTime;
        float travel = Mathf.Max(0.1f, projectileSpeed) * dt;
        float radius = Mathf.Max(0.01f, projectileRadius);
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Flight shot = active[i];
            Vector3 position = shot.visual.position;
            shot.lifetimeLeft -= dt;

            if (Physics.SphereCast(position, radius, shot.direction, out RaycastHit hit,
                travel, effectiveCollisionMask, QueryTriggerInteraction.Ignore))
            {
                // A shooter may have died and been reused by the enemy pool.
                // Its old projectile must not credit the replacement enemy.
                HordeEnemy attacker = shot.owner;
                if (attacker == null || !attacker.isActiveAndEnabled ||
                    attacker.IsDead || attacker.SpawnGeneration != shot.ownerGeneration)
                    attacker = null;

                // Car colliders are NOT children of the player root. Detect them
                // independently, including child body-panel colliders.
                CarController car = hit.collider.GetComponentInParent<CarController>();
                if (car != null)
                {
                    car.TakeVehicleDamage(shot.damage, attacker);
                }
                else if (playerRoot != null && hit.collider.transform.IsChildOf(playerRoot))
                {
                    playerHealth.TakeDamage(shot.damage, attacker);
                }
                RecycleAt(i);
                continue;
            }
            if (shot.lifetimeLeft <= 0f)
            {
                RecycleAt(i);
                continue;
            }
            shot.visual.position = position + shot.direction * travel;
        }
    }

    private void RecycleAt(int index)
    {
        Flight shot = active[index];
        int lastIndex = active.Count - 1;
        active[index] = active[lastIndex];
        active.RemoveAt(lastIndex);
        shot.owner = null;
        shot.visual.gameObject.SetActive(false);
        inactive.Push(shot);
    }

    private void OnDisable()
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Flight shot = active[i];
            shot.owner = null;
            shot.visual.gameObject.SetActive(false);
            inactive.Push(shot);
        }
        active.Clear();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxActiveProjectiles = Mathf.Max(1, maxActiveProjectiles);
        projectileSpeed = Mathf.Max(0.1f, projectileSpeed);
        projectileRadius = Mathf.Max(0.01f, projectileRadius);
        projectileLifetime = Mathf.Max(0.05f, projectileLifetime);
        // Inspector tweaks are reflected during Play Mode too.
        effectiveCollisionMask = collisionLayers.value & ~(1 << 7);
    }
#endif
}
