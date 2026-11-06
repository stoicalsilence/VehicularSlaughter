
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public sealed class HordeEnemy : MonoBehaviour, ITurretDamageable
{
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

    private float currentHealth;
    private bool isDead;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => isDead;


    [Header("Hit Feedback")]
    [SerializeField]
    private float hitFlashDuration = 0.08f;

    // Shader properties for URP and Built-in Standard shaders.
    private static readonly int BaseColorId =
        Shader.PropertyToID("_BaseColor");

    private static readonly int ColorId =
        Shader.PropertyToID("_Color");

    private Coroutine hitFlashRoutine;

    private class RendererFlashState
    {
        public Renderer renderer;
        public MaterialPropertyBlock originalBlock;
        public MaterialPropertyBlock flashBlock;

        public bool hasBaseColor;
        public bool hasColor;
    }

    private readonly List<RendererFlashState> flashRenderers =
        new List<RendererFlashState>();


    [Header("Death")]
    public DeathParticlePool deathParticlePool;

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

    [SerializeField, Min(0.05f)]
    [Tooltip("Prevents multiple colliders or repeated contacts from applying several vehicle hits in quick succession.")]
    private float vehicleHitCooldown = 0.4f;

    private float nextVehicleHitTime;

    public float VehicleImpactCarDamageMultiplier => vehicleImpactCarDamageMultiplier;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        defaultGravity = rb.useGravity;

        currentHealth = maxHealth;
        CacheFlashRenderers();
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

        rb.useGravity = defaultGravity;

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
            stopDistance * stopDistance)
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

    // ------------------------------------------------
    // GROUND MOVEMENT
    // ------------------------------------------------

    private void SimulateGroundMovement(
        float dt,
        Vector3 directionToPlayer)
    {
        Vector3 desiredVelocity = Vector3.zero;

        if (directionToPlayer.sqrMagnitude >
            stopDistance * stopDistance)
        {
            desiredVelocity =
                directionToPlayer.normalized * moveSpeed;
        }

        // Prevent all enemies from occupying one position.
        desiredVelocity += CalculateSeparation();

        desiredVelocity = Vector3.ClampMagnitude(
            desiredVelocity,
            moveSpeed + separationStrength
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
            acceleration * dt
        );

        currentVelocity.x = nextHorizontal.x;
        currentVelocity.z = nextHorizontal.z;

        // Preserve vertical physics velocity.
        Velocity = currentVelocity;

        FaceDirection(nextHorizontal, dt);
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
            climbDirection * climbSpeed -
            wallNormal * wallStickSpeed;

        Velocity = Vector3.MoveTowards(
            Velocity,
            desiredVelocity,
            climbAcceleration * dt
        );

        FaceDirection(climbDirection, dt);
    }

    private void ExitClimbing()
    {
        if (!isClimbing)
            return;

        isClimbing = false;

        rb.useGravity = defaultGravity;

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
            turnSpeed * dt
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

        if (rb == null)
            return;

        rb.useGravity = defaultGravity;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    public void PrepareForPool()
    {
        StopHitFlash(true);
        ResetMovementState();
    }

    public void ResetForSpawn()
    {
        StopHitFlash(true);

        currentHealth = maxHealth;
        isDead = false;

        ResetMovementState();

        simulationTimer = Random.Range(0f, 0.2f);
    }


    public void TakeDamage(float damage)
    {
        ApplyDamage(damage);
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
        if (isDead || Time.time < nextVehicleHitTime)
            return false;

        nextVehicleHitTime = Time.time + Mathf.Max(0.05f, vehicleHitCooldown);

        // Heavy/anchored enemies still punish the vehicle, but cannot be run over.
        if (!canBeRunOver)
            return true;

        float finalDamage = Mathf.Max(0f, damage) * Mathf.Max(0f, vehicleDamageMultiplier);
        ApplyDamage(finalDamage);

        // ApplyDamage may have killed and despawned the enemy.
        if (!isDead && knockbackForce > 0f)
        {
            float finalKnockback =
                knockbackForce * Mathf.Max(0f, vehicleKnockbackMultiplier);

            ApplyKnockback(impactPosition, finalKnockback);
        }

        return true;
    }

    // Damage with explicitly requested knockback.
    public void TakeDamage(float damage, Vector3 impactPosition, float knockbackForce)
    {
        if (!ApplyDamage(damage))
            return;

        ApplyKnockback(impactPosition, knockbackForce);
    }

    private bool ApplyDamage(float damage)
    {
        if (isDead || damage <= 0f)
            return false;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (currentHealth <= 0f)
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
            50
        );
    }


    // Cache all renderers, including SkinnedMeshRenderers
    // and renderers on child GameObjects.
    private void CacheFlashRenderers()
    {
        flashRenderers.Clear();

        Renderer[] renderers =
            GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            bool hasBaseColor = false;
            bool hasColor = false;

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                if (material.HasProperty(BaseColorId))
                    hasBaseColor = true;

                if (material.HasProperty(ColorId))
                    hasColor = true;
            }

            if (!hasBaseColor && !hasColor)
                continue;

            flashRenderers.Add(new RendererFlashState
            {
                renderer = renderer,
                originalBlock = new MaterialPropertyBlock(),
                flashBlock = new MaterialPropertyBlock(),
                hasBaseColor = hasBaseColor,
                hasColor = hasColor
            });
        }
    }


    private void PlayHitFlash()
    {
        if (hitFlashDuration <= 0f)
            return;

        bool alreadyFlashing = hitFlashRoutine != null;

        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        foreach (RendererFlashState state in flashRenderers)
        {
            if (state.renderer == null)
                continue;

            // Only capture the original state on the first hit.
            if (!alreadyFlashing)
            {
                state.renderer.GetPropertyBlock(
                    state.originalBlock);
            }

            state.renderer.GetPropertyBlock(
                state.flashBlock);

            if (state.hasBaseColor)
                state.flashBlock.SetColor(BaseColorId, Color.white);

            if (state.hasColor)
                state.flashBlock.SetColor(ColorId, Color.white);

            state.renderer.SetPropertyBlock(
                state.flashBlock);
        }

        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }


    private IEnumerator HitFlashRoutine()
    {
        yield return new WaitForSeconds(hitFlashDuration);

        RestoreOriginalMaterials();

        hitFlashRoutine = null;
    }


    private void RestoreOriginalMaterials()
    {
        foreach (RendererFlashState state in flashRenderers)
        {
            if (state.renderer == null)
                continue;

            state.renderer.SetPropertyBlock(
                state.originalBlock);
        }
    }


    private void StopHitFlash(bool restoreMaterials)
    {
        if (hitFlashRoutine != null)
        {
            StopCoroutine(hitFlashRoutine);
            hitFlashRoutine = null;
        }

        if (restoreMaterials)
            RestoreOriginalMaterials();
    }


    private void Die()
    {
        if (isDead)
            return;

        isDead = true;

        StopHitFlash(true);

        // Spawn pooled death particles.
        if (deathParticlePool != null)
        {
            deathParticlePool.Play(transform.position);
        }

        // Play 3D death audio.
        if (deathSound != null)
        {
            AudioManager.Play3D(
                deathSound,
                transform.position,
                deathSoundVolume
            );
        }

        DropXP();

        // Return the enemy to the HordeManager pool.
        if (HordeManager.Instance != null)
        {
            HordeManager.Instance.DespawnEnemy(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    private void DropXP()
    {
        xpBlobPool.Spawn(transform.position, xpDropValue);
    }

}
