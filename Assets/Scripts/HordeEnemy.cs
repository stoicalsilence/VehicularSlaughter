using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum HordeEnemyVariant { Normal, Elite, EMP }

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class HordeEnemy : MonoBehaviour, ITurretDamageable
{
    [Header("Variants")]
    [SerializeField] private bool allowEliteVariant = true;
    [SerializeField] private bool allowEmpVariant;
    [Tooltip("Shared, solid-blue material. Must support this enemy's baked animation shader if used.")]
    [SerializeField] private Material empBlueMaterial;

    public HordeEnemyVariant Variant { get; private set; }
    public bool IsElite => Variant == HordeEnemyVariant.Elite;
    public bool IsEMP => Variant == HordeEnemyVariant.EMP;
    public bool CanBeElite => allowEliteVariant;
    public bool CanBeEMP => allowEmpVariant && empBlueMaterial != null && !IsFlying;
    // Combat behavior is a prefab component, NOT a mutually exclusive variant.
    public bool IsRanged => rangedAttack != null && rangedAttack.enabled;
    public bool IsFlying => flyingAttack != null && flyingAttack.enabled;
    // Chunky is also a prefab component. Only collision/contact hooks call it.
    public HordeChunkyImpact ChunkyImpact { get; private set; }
    // Prevent recycled enemies from inheriting ownership of a projectile in flight.
    public uint SpawnGeneration { get; private set; }

    private HordeRangedAttack rangedAttack;
    private HordeFlyingAttack flyingAttack;

    private Vector3 baseLocalScale;
    private CollisionDetectionMode baseCollisionMode;
    private bool empVisualApplied;
    private sealed class VariantRendererState
    {
        public Renderer renderer;
        public Material[] normalMaterials;
        public Material[] empMaterials;
        public Material[] flashMaterials;
    }
    private readonly List<VariantRendererState> variantRenderers = new List<VariantRendererState>();

    private float EffectiveStopDistance => IsEMP ? 0f : IsRanged ? rangedAttack.HoldDistance : stopDistance;
    private float VariantMotionMultiplier => IsEMP || IsElite ? Mathf.Max(1f, variantSpeedMultiplier) : 1f;
    private float variantSpeedMultiplier = 1f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4.5f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private float turnSpeed = 720f;
    [SerializeField] private float stopDistance = 1.2f;

    [Header("Enemy Separation")]
    [SerializeField] private LayerMask enemyLayers;
    [SerializeField] private float separationRadius = 1.5f;
    [SerializeField] private float separationStrength = 2.5f;

    [Header("Wall Climbing")]
    [SerializeField] private LayerMask climbableLayers;

    [SerializeField] private float wallProbeDistance = 1.2f;
    [SerializeField] private float maxWallNormalY = 0.35f;

    [SerializeField] private float climbSpeed = 4f;
    [SerializeField] private float climbAcceleration = 20f;
    [SerializeField] private float wallStickSpeed = 1.5f;

    [Range(0f, 1f)]
    [SerializeField] private float minimumClimbUpDot = 0.45f;

    public XPBlobPool xpBlobPool;
    [SerializeField] private float xpDropValue = 10f;

    // Components
    private Rigidbody rb;
    private CapsuleCollider capsule;

    // Rigidbody gravity state.
    private bool defaultGravity;

    // Climbing state.
    private bool isClimbing;
    private Vector3 wallNormal;
    private Vector3 climbForward;

    // AI update scheduling.
    private float simulationTimer;

    // Reusable physics buffer. Avoids allocations.
    private readonly Collider[] separationBuffer =
        new Collider[24];

    private Vector3 Velocity
    {
        get
        {
            return rb.velocity;
        }
        set
        {
            rb.velocity = value;
        }
    }

    [Header("Health")]
    [SerializeField, Min(1f)]
    private float maxHealth = 30f;

    [Header("Contact Damage")]
    [SerializeField, Min(0f)]
    public float contactDamage = 10f;

    private float currentHealth;
    private bool isDead;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => Mathf.Max(1f, maxHealth * HealthMultiplier);
    public bool IsDead => isDead;

    // Only a turret that explicitly marks this enemy subscribes.
    // Clear that mark before this pooled instance can be reused.
    public event System.Action<HordeEnemy> TurretTargetInvalidated;

    // Fired only for genuine lethal damage, before this pooled enemy is invalidated.
    // EMP self-detonation against a car intentionally does not trigger this event.
    public event System.Action<HordeEnemy, Vector3> KilledAtPosition;


    [Header("Hit Feedback")]
    [SerializeField]
    private float hitFlashDuration = 0.08f;

    [Tooltip("Shared solid-white material, preferably Unlit with no texture.")]
    [SerializeField] private Material hitFlashMaterial;

    [Tooltip("Allow blue EMP enemies to briefly flash white when hit.")]
    [SerializeField] private bool flashEMPEnemies = true;

    private Coroutine hitFlashRoutine;
    private float hitFlashEndsAt;


    [Header("Death")]
    public DeathParticlePool deathParticlePool;
    public PlayerInventory playerInventory;

    [SerializeField]
    private AudioClip deathSound;

    [SerializeField, Range(0f, 1f)]
    private float deathSoundVolume = 1f;

    [Header("Hit Audio")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip altHitSound;

    [SerializeField, Range(0f, 1f)]
    private float hitSoundVolume = 0.8f;

    [Header("Knockback")]
    [SerializeField, Range(0f, 1f)]
    private float knockbackResistancePercent = 0f;

    [SerializeField, Min(0f)]
    private float knockbackDuration = 0.25f;

    private float knockbackTimer;

    [Header("Vehicle Impact")]
    [SerializeField]
    [Tooltip("When disabled, this enemy cannot be damaged or pushed by the car, but it can still damage the car on impact.")]
    private bool canBeRunOver = true;

    [SerializeField, Min(0f)]
    [Tooltip("Scales damage received from the car. Lower values make this enemy harder to run over.")]
    private float vehicleDamageMultiplier = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("Scales knockback received from the car, before the general knockback resistance is applied.")]
    private float vehicleKnockbackMultiplier = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("Scales how much this enemy damages the car when hit. Use higher values for armored/heavy enemies.")]
    private float vehicleImpactCarDamageMultiplier = 1f;

    [Tooltip("Vehicle-specific contact factor. Multiply the scaled contactDamage by this; do not apply run difficulty again.")]
    [SerializeField, Min(0f)]
    public float vehicleContactDamageMultiplier = 1f;

    [SerializeField, Min(0.05f)]
    [Tooltip("Prevents multiple colliders or repeated contacts from applying several vehicle hits in quick succession.")]
    private float vehicleHitCooldown = 0.4f;

    private float nextVehicleHitTime;

    [Header("Baked Animation")]
    [SerializeField] private HordeBakedAnimator bakedAnimator;

    // Per-spawn difficulty snapshot. Only outgoing damage is scaled.
    public float HealthMultiplier { get; private set; } = 1f;
    public float SpeedMultiplier { get; private set; } = 1f;
    public float DamageMultiplier { get; private set; } = 1f;
    public float MoveSpeed => moveSpeed * SpeedMultiplier;
    public float ClimbSpeed => climbSpeed * SpeedMultiplier;

    // Fast, allocation-free read for the centralized footstep controller.
    // No raycasts or extra components per enemy.
    internal bool IsMovingForFootsteps
    {
        get
        {
            if (isDead || !isActiveAndEnabled || isClimbing || IsFlying ||
                knockbackTimer > 0f || rb == null)
                return false;

            Vector3 v = rb.velocity;
            return Mathf.Abs(v.y) < 1.8f && (v.x * v.x + v.z * v.z) > 0.64f;
        }
    }

    // Contact damage stays a public field for existing player/car callers.
    // Capture it once, then restore from the base on every pool reuse.
    private float baseContactDamage;
    public float BaseContactDamage => baseContactDamage;

    public float VehicleImpactCarDamageMultiplier =>
        IsEMP ? 0f : vehicleImpactCarDamageMultiplier * DamageMultiplier;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        defaultGravity = rb.useGravity;
        baseLocalScale = transform.localScale;
        baseCollisionMode = rb.collisionDetectionMode;
        rangedAttack = GetComponent<HordeRangedAttack>();
        flyingAttack = GetComponent<HordeFlyingAttack>();
        ChunkyImpact = GetComponent<HordeChunkyImpact>();
        CacheVariantRenderers();

        baseContactDamage = contactDamage;
        currentHealth = MaxHealth;

        if (bakedAnimator == null)
            bakedAnimator = GetComponentInChildren<HordeBakedAnimator>(true);
    }

    private void OnEnable()
    {
        if (rb == null)
            return;

        knockbackTimer = 0f;
        wallNormal = Vector3.zero;
        climbForward = Vector3.zero;

        isClimbing = false;
        nextVehicleHitTime = 0f;

        rb.useGravity = IsFlying ? false : defaultGravity;

        Velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Distribute initial updates across manager ticks.
        simulationTimer = Random.Range(0f, 0.2f);

        if (HordeManager.Instance != null)
        {
            HordeManager.Instance.RegisterEnemy(this);
        }
    }

    private void OnDisable()
    {
        TurretTargetInvalidated?.Invoke(this);

        if (HordeManager.Instance != null)
            HordeManager.Instance.UnregisterEnemy(this);

        StopHitFlash(true);
        ResetMovementState();
    }

    // Called by HordeManager only.
    internal void ManagedTick(
        float elapsed,
        float updateInterval,
        Vector3 targetPosition)
    {
        if (isDead)
            return;

        if (knockbackTimer > 0f)
        {
            knockbackTimer -= elapsed;
            simulationTimer = 0f;
            return;
        }

        simulationTimer += elapsed;

        if (simulationTimer < updateInterval)
            return;

        float dt = simulationTimer;
        simulationTimer = 0f;

        SimulateEnemy(dt, targetPosition);
    }

    // ------------------------------------------------
    // MAIN AI SIMULATION
    // ------------------------------------------------

    private void SimulateEnemy(
        float dt,
        Vector3 targetPosition)
    {
        Vector3 toTarget = targetPosition - rb.position;

        Vector3 horizontalDirection =
            Vector3.ProjectOnPlane(toTarget, Vector3.up);

        // Flying archetypes bypass ground/chase/climb and keep their own attack pattern.
        // The common HordeEnemy still owns health, pooling, and knockback.
        if (IsFlying && !IsEMP)
        {
            flyingAttack.ManagedTick(dt, targetPosition);
            return;
        }

        // The optional ranged component is deliberately ticked by this manager-owned
        // simulation, never by its own Update/FixedUpdate. EMP overrides shooting.
        if (!IsEMP && rangedAttack != null && !isClimbing &&
            rangedAttack.ManagedTick(dt, targetPosition, horizontalDirection))
            return;

        // Already climbing?
        if (isClimbing)
        {
            if (TryGetWallAhead(horizontalDirection, out RaycastHit hit))
            {
                wallNormal = hit.normal;

                SimulateClimbing(dt, toTarget);
                return;
            }

            // Wall ended. Return to normal movement.
            ExitClimbing();
        }

        // Look for a wall in front of us.
        if (horizontalDirection.sqrMagnitude >
            EffectiveStopDistance * EffectiveStopDistance)
        {
            if (TryStartClimbing(horizontalDirection))
            {
                SimulateClimbing(dt, toTarget);
                return;
            }
        }

        // No climbable wall: normal ground movement.
        SimulateGroundMovement(dt, horizontalDirection);
    }

    // Called by HordeManager.
    // Animation updates are independent of AI update intervals.
    internal void ManagedAnimationTick(float elapsed)
    {
        if (isDead || bakedAnimator == null)
            return;

        Vector3 horizontalVelocity = Velocity;
        horizontalVelocity.y = 0f;

        HordeBakedAnimator.AnimationState state =
            horizontalVelocity.sqrMagnitude > 0.04f
                ? HordeBakedAnimator.AnimationState.Move
                : HordeBakedAnimator.AnimationState.Idle;

        bakedAnimator.Play(state);
        bakedAnimator.Tick(elapsed);
    }

    // ------------------------------------------------
    // GROUND MOVEMENT
    // ------------------------------------------------

    private void SimulateGroundMovement(
        float dt,
        Vector3 directionToPlayer)
    {
        Vector3 desiredVelocity = Vector3.zero;

        if (directionToPlayer.sqrMagnitude >
            EffectiveStopDistance * EffectiveStopDistance)
        {
            desiredVelocity =
                directionToPlayer.normalized * MoveSpeed;
        }

        // Prevent all enemies from occupying one position.
        desiredVelocity += CalculateSeparation() * (IsEMP ? 0.25f : 1f);

        desiredVelocity = Vector3.ClampMagnitude(
            desiredVelocity,
            MoveSpeed + separationStrength
        );

        Vector3 currentVelocity = Velocity;

        Vector3 currentHorizontal = new Vector3(
            currentVelocity.x,
            0f,
            currentVelocity.z
        );

        Vector3 nextHorizontal = Vector3.MoveTowards(
            currentHorizontal,
            desiredVelocity,
            acceleration * VariantMotionMultiplier * dt
        );

        currentVelocity.x = nextHorizontal.x;
        currentVelocity.z = nextHorizontal.z;

        // Preserve vertical physics velocity.
        Velocity = currentVelocity;

        FaceDirection(nextHorizontal, dt);
    }

    // Called only while a ranged prefab is holding position. Avoids a separation
    // physics query for every stationary shooter.
    internal bool BrakeAndFaceRanged(float dt, Vector3 faceDirection)
    {
        Vector3 velocity = Velocity;
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, acceleration * dt);
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;
        Velocity = velocity;
        FaceDirection(faceDirection, dt);
        return horizontal.sqrMagnitude <= 0.04f;
    }

    // ------------------------------------------------
    // ENEMY SEPARATION
    // ------------------------------------------------

    private Vector3 CalculateSeparation()
    {
        int count = Physics.OverlapSphereNonAlloc(
            rb.position,
            separationRadius,
            separationBuffer,
            enemyLayers,
            QueryTriggerInteraction.Ignore
        );

        Vector3 separation = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Collider other = separationBuffer[i];

            if (other == null)
                continue;

            Rigidbody otherBody = other.attachedRigidbody;

            if (otherBody == null || otherBody == rb)
                continue;

            Vector3 away =
                rb.position - otherBody.position;

            away.y = 0f;

            float distance = away.magnitude;

            if (distance < 0.001f ||
                distance >= separationRadius)
                continue;

            float strength =
                1f - distance / separationRadius;

            separation += away.normalized * strength;
        }

        return separation * separationStrength;
    }

    // ------------------------------------------------
    // WALL DETECTION
    // ------------------------------------------------

    private float GetProbeRadius()
    {
        Vector3 extents = capsule.bounds.extents;

        return Mathf.Max(
            0.08f,
            Mathf.Min(extents.x, extents.z) * 0.65f
        );
    }

    private bool IsClimbableSurface(Vector3 normal)
    {
        return Mathf.Abs(normal.y) <= maxWallNormalY;
    }

    private bool TryStartClimbing(Vector3 direction)
    {
        direction.Normalize();

        Vector3 origin = capsule.bounds.center;

        if (!Physics.SphereCast(
            origin,
            GetProbeRadius(),
            direction,
            out RaycastHit hit,
            wallProbeDistance,
            climbableLayers,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (!IsClimbableSurface(hit.normal))
            return false;

        wallNormal = hit.normal;
        climbForward = direction;

        EnterClimbing();

        return true;
    }

    private bool TryGetWallAhead(
        Vector3 direction,
        out RaycastHit hit)
    {
        if (direction.sqrMagnitude > 0.01f)
            climbForward = direction.normalized;

        return Physics.SphereCast(
            capsule.bounds.center,
            GetProbeRadius(),
            climbForward,
            out hit,
            wallProbeDistance,
            climbableLayers,
            QueryTriggerInteraction.Ignore
        ) && IsClimbableSurface(hit.normal);
    }

    // ------------------------------------------------
    // CLIMBING
    // ------------------------------------------------

    private void EnterClimbing()
    {
        isClimbing = true;

        // Disable gravity while traversing the wall.
        rb.useGravity = false;
    }

    private void SimulateClimbing(
        float dt,
        Vector3 toTarget)
    {
        // Find a direction along the wall towards the player.
        Vector3 climbDirection =
            Vector3.ProjectOnPlane(toTarget, wallNormal);

        if (climbDirection.sqrMagnitude < 0.001f)
        {
            climbDirection =
                Vector3.ProjectOnPlane(Vector3.up, wallNormal);
        }

        climbDirection.Normalize();

        // Always maintain an upward climbing tendency.
        if (climbDirection.y < minimumClimbUpDot)
        {
            Vector3 lateralDirection =
                Vector3.ProjectOnPlane(
                    climbDirection,
                    Vector3.up
                );

            climbDirection = (
                lateralDirection +
                Vector3.up * minimumClimbUpDot
            ).normalized;
        }

        // Move upwards/along the wall and stay attached.
        Vector3 desiredVelocity =
            climbDirection * ClimbSpeed -
            wallNormal * wallStickSpeed;

        Velocity = Vector3.MoveTowards(
            Velocity,
            desiredVelocity,
            climbAcceleration * VariantMotionMultiplier * dt
        );

        FaceDirection(climbDirection, dt);
    }

    private void ExitClimbing()
    {
        if (!isClimbing)
            return;

        isClimbing = false;

        rb.useGravity = IsFlying ? false : defaultGravity;

        Vector3 velocity = Velocity;

        // Prevent residual climbing velocity.
        velocity.y = 0f;

        Velocity = velocity;
    }

    // ------------------------------------------------
    // ROTATION
    // ------------------------------------------------

    private void FaceDirection(
        Vector3 direction,
        float dt)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction.normalized);

        Quaternion nextRotation = Quaternion.RotateTowards(
            rb.rotation,
            targetRotation,
            turnSpeed * VariantMotionMultiplier * dt
        );

        rb.MoveRotation(nextRotation);
    }

    // ------------------------------------------------
    // POOL SUPPORT
    // ------------------------------------------------

    private void ResetMovementState()
    {
        isClimbing = false;

        wallNormal = Vector3.zero;
        climbForward = Vector3.zero;

        nextVehicleHitTime = 0f;
        knockbackTimer = 0f;
        simulationTimer = 0f;
        if (rangedAttack != null)
            rangedAttack.ResetForPool();
        if (flyingAttack != null)
            flyingAttack.ResetForPool();

        if (rb == null)
            return;

        rb.useGravity = IsFlying ? false : defaultGravity;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (bakedAnimator != null)
            bakedAnimator.ResetAnimation();
    }

    public void PrepareForPool()
    {
        TurretTargetInvalidated?.Invoke(this);
        StopHitFlash(true);
        ResetMovementState();
    }

    public void ResetForSpawn()
    {
        ResetForSpawn(1f, 1f, 1f);
    }

    // Called while pooled/inactive, after Awake and before OnEnable.
    public void ResetForSpawn(float healthMultiplier, float speedMultiplier, float damageMultiplier)
    {
        ResetForSpawn(healthMultiplier, speedMultiplier, damageMultiplier,
            HordeEnemyVariant.Normal, 1f, 1f);
    }

    // Variant multipliers have already been combined with run difficulty by the manager.
    internal void ResetForSpawn(float healthMultiplier, float speedMultiplier, float damageMultiplier,
        HordeEnemyVariant variant, float scaleMultiplier, float motionMultiplier)
    {
        TurretTargetInvalidated?.Invoke(this);
        StopHitFlash(true);

        Variant = variant;
        variantSpeedMultiplier = Mathf.Max(1f, motionMultiplier);
        transform.localScale = baseLocalScale * Mathf.Max(0.1f, scaleMultiplier);
        SetEmpVisuals(IsEMP);
        if (rb != null)
            rb.collisionDetectionMode = IsEMP ? CollisionDetectionMode.ContinuousDynamic : baseCollisionMode;

        HealthMultiplier = SanitizeMultiplier(healthMultiplier);
        SpeedMultiplier = SanitizeMultiplier(speedMultiplier);
        DamageMultiplier = SanitizeMultiplier(damageMultiplier);
        // A drone only hurts targets on a committed dive, never by passive contact.
        contactDamage = (IsRanged || IsFlying) && !IsEMP
            ? 0f : Mathf.Max(0f, baseContactDamage) * DamageMultiplier;
        currentHealth = MaxHealth;
        isDead = false;

        ResetMovementState();
        unchecked { SpawnGeneration++; }
        if (rangedAttack != null)
            rangedAttack.ResetForSpawn(this);
        if (flyingAttack != null)
            flyingAttack.ResetForSpawn(this);

        simulationTimer = Random.Range(0f, 0.2f);
    }


    private static float SanitizeMultiplier(float multiplier)
    {
        return float.IsNaN(multiplier) || float.IsInfinity(multiplier)
            ? 1f : Mathf.Max(0f, multiplier);
    }

    public void TakeDamage(float damage, int criticalHits)
    {
        ApplyDamage(damage, criticalHits);
    }

    public void TakeDamage(
    float damage,
    Vector3 impactPosition,
    float knockbackForce,
    int criticalHits)
    {
        if (!ApplyDamage(damage, criticalHits))
            return;

        ApplyKnockback(impactPosition, knockbackForce);
    }

    /// <summary>
    /// Applies a car impact. Returns true when this enemy accepted the impact,
    /// including enemies configured as too heavy to be run over.
    /// </summary>
    public bool ReceiveVehicleImpact(
        float damage,
        Vector3 impactPosition,
        float knockbackForce)
    {
        if (isDead || IsEMP || Time.time < nextVehicleHitTime)
            return false;

        nextVehicleHitTime = Time.time + Mathf.Max(0.05f, vehicleHitCooldown);

        // Heavy/anchored enemies still punish the vehicle, but cannot be run over.
        if (!canBeRunOver)
            return true;

        float finalDamage = Mathf.Max(0f, damage) * Mathf.Max(0f, vehicleDamageMultiplier);
        ApplyDamage(finalDamage, 0);

        // ApplyDamage may have killed and despawned the enemy.
        if (!isDead && knockbackForce > 0f)
        {
            float finalKnockback =
                knockbackForce * Mathf.Max(0f, vehicleKnockbackMultiplier);

            ApplyKnockback(impactPosition, finalKnockback);
        }

        return true;
    }


    private bool ApplyDamage(float damage, int criticalHits)
    {
        if (isDead || damage <= 0f)
            return false;

        // Preserve the full damage value for the damage number,
        // even if it exceeds the enemy's remaining health.
        float displayedDamage = damage;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        bool lethalHit = currentHealth <= 0f;

        Vector3 numberPosition = capsule != null
            ? capsule.bounds.center + Vector3.up * (capsule.bounds.extents.y + 0.08f)
            : transform.position + Vector3.up;

        DamageNumberPool.ShowDamage(
            numberPosition,
            displayedDamage,
            MaxHealth,
            lethalHit,
            criticalHits
        );

        if (lethalHit)
        {
            Die();
            return false;
        }

        PlayHitSound();
        PlayHitFlash();

        return true;
    }

    private void ApplyKnockback(Vector3 impactPosition, float force)
    {
        if (rb == null || force <= 0f)
            return;

        float resistanceMultiplier = 1f - knockbackResistancePercent;
        float finalForce = force * resistanceMultiplier;

        if (finalForce <= 0f)
            return;

        Vector3 knockbackDirection = rb.position - impactPosition;

        // Keep knockback horizontal.
        knockbackDirection.y = 0f;

        if (knockbackDirection.sqrMagnitude < 0.001f)
            knockbackDirection = -transform.forward;

        knockbackDirection.Normalize();

        rb.AddForce(
            knockbackDirection * finalForce,
            ForceMode.VelocityChange
        );

        knockbackTimer = knockbackDuration;
        if (rangedAttack != null)
            rangedAttack.CancelForKnockback();
        if (flyingAttack != null)
            flyingAttack.CancelForKnockback();
    }

    private void PlayHitSound()
    {
        if (hitSound == null)
            return;

        AudioClip soundToPlay = hitSound;

        if (altHitSound != null)
        {
            int r = Random.Range(0, 100);

            if (r < 50)
            {
                soundToPlay = altHitSound;
            }
        }

        AudioManager.Play3D(
            soundToPlay,
            transform.position,
            hitSoundVolume,
            1,
            1,
            50, 192
        );
    }


    private void PlayHitFlash()
    {
        if (hitFlashMaterial == null || hitFlashDuration <= 0f ||
            (IsEMP && !flashEMPEnemies) || !isActiveAndEnabled ||
            variantRenderers.Count == 0)
            return;

        // Consecutive hits extend the current flash without swapping again.
        hitFlashEndsAt = Time.time + hitFlashDuration;
        if (hitFlashRoutine != null)
            return;

        foreach (VariantRendererState state in variantRenderers)
        {
            if (state.renderer != null && state.flashMaterials != null)
                state.renderer.sharedMaterials = state.flashMaterials;
        }

        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        // No new WaitForSeconds allocation or coroutine restart on repeated hits.
        while (Time.time < hitFlashEndsAt)
            yield return null;

        RestoreOriginalMaterials();
        hitFlashRoutine = null;
    }

    private void RestoreOriginalMaterials()
    {
        foreach (VariantRendererState state in variantRenderers)
        {
            if (state.renderer == null)
                continue;

            state.renderer.sharedMaterials = empVisualApplied && state.empMaterials != null
                ? state.empMaterials : state.normalMaterials;
        }
    }

    private void StopHitFlash(bool restoreMaterials)
    {
        if (hitFlashRoutine == null)
            return;

        StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = null;
        hitFlashEndsAt = 0f;

        if (restoreMaterials)
            RestoreOriginalMaterials();
    }

    // All visual states share a single cache, including inactive baked frames.
    // Arrays are allocated once per pooled enemy, never on damage.
    private void CacheVariantRenderers()
    {
        if (!CanBeEMP && hitFlashMaterial == null)
            return;

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            // Do not flash a ranged telegraph material with the enemy's body.
            if (rangedAttack != null && rangedAttack.IsChargeVisual(renderer.transform))
                continue;
            if (flyingAttack != null && flyingAttack.IsWarningVisual(renderer.transform))
                continue;
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                continue;

            Material[] normal = renderer.sharedMaterials;
            if (normal.Length == 0)
                continue;

            Material[] blue = CanBeEMP ? new Material[normal.Length] : null;
            Material[] white = hitFlashMaterial != null ? new Material[normal.Length] : null;

            for (int i = 0; i < normal.Length; i++)
            {
                if (blue != null) blue[i] = empBlueMaterial;
                if (white != null) white[i] = hitFlashMaterial;
            }

            variantRenderers.Add(new VariantRendererState
            {
                renderer = renderer,
                normalMaterials = normal,
                empMaterials = blue,
                flashMaterials = white
            });
        }
    }

    private void SetEmpVisuals(bool useEmpMaterials)
    {
        if (empVisualApplied == useEmpMaterials)
            return;

        empVisualApplied = useEmpMaterials;
        if (hitFlashRoutine == null)
            RestoreOriginalMaterials();
    }

    // Called by CarController before any normal run-over or contact damage.
    // No XP, kill credit or crate for a successful suicide attack.
    internal bool DetonateEMP(CarController car, Vector3 impactPoint)
    {
        if (!IsEMP || isDead || !isActiveAndEnabled || car == null) return false;
        isDead = true; // Guard duplicate collision callbacks BEFORE invoking other systems.
        currentHealth = 0f;
        StopHitFlash(true);
        HordeManager manager = HordeManager.Instance;
        if (manager != null) manager.PlayEMPImpact(impactPoint);
        car.TakeEMPBatteryHit();
        if (manager != null) manager.DespawnEnemy(this);
        else Destroy(gameObject);
        return true;
    }


    private void Die()
    {
        if (isDead)
            return;

        isDead = true;
        Vector3 deathPosition = transform.position;
        KilledAtPosition?.Invoke(this, deathPosition);
        TurretTargetInvalidated?.Invoke(this);

        StopHitFlash(true);

        if (playerInventory != null)
            playerInventory.ReportKill(gameObject);

        HordeManager manager = HordeManager.Instance;

        if (IsEMP && manager != null)
        {
            // EMP-specific explosion particles and sound.
            manager.PlayEMPImpact(deathPosition);
        }
        else
        {
            // Regular pooled death particles.
            if (deathParticlePool != null)
            {
                deathParticlePool.Play(deathPosition);
            }

            // Regular death sound.
            if (deathSound != null)
            {
                AudioManager.Play3D(
                    deathSound,
                    deathPosition,
                    deathSoundVolume, 1, 1, 50, 100
                );
            }
        }

        DropXP();

        if (IsElite && manager != null)
            manager.TryDropEliteCrate(deathPosition);

        // Return enemy to the pool.
        if (manager != null)
        {
            manager.DespawnEnemy(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }


    private void DropXP()
    {
        if (xpBlobPool != null)
        {
            Vector3 position = IsFlying
                ? flyingAttack.GetGroundedXPPosition() : transform.position;
            xpBlobPool.Spawn(position, xpDropValue);
        }
    }

    public void ReceiveThornsDamage(float damage)
    {
        if (damage <= 0f || IsDead)
            return;

        TakeDamage(damage, 0);
    }
}
