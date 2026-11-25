using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Harrier drone archetype. Add alongside HordeEnemy on a dedicated pooled prefab.
/// HordeEnemy/HordeManager still own health, variants, XP, damage, and scheduling.
/// This component has NO Update, FixedUpdate, coroutine, or per-AI-tick allocations.
///
/// Pattern: approach -> orbit -> visible windup -> committed diving pass -> climb out.
/// Only the diving pass can damage the player or the occupied vehicle.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HordeEnemy))]
public sealed class HordeFlyingAttack : MonoBehaviour
{
    private enum FlightPhase { Orbit, Telegraph, Dive, Recover }

    [Header("Spawn / Flight Envelope")]
    [SerializeField, Min(1f)] private float spawnHeightAboveGround = 6f;
    [SerializeField, Min(1f)] private float cruiseAltitudeAboveTarget = 5.5f;
    [SerializeField, Min(2f)] private float orbitRadius = 10f;
    [SerializeField, Min(0.1f)] private float orbitSpeed = 10f;
    [SerializeField, Min(0.1f)] private float approachSpeed = 18f;
    [SerializeField, Min(0.1f)] private float diveSpeed = 31f;
    [SerializeField, Min(0.1f)] private float flightAcceleration = 35f;
    [SerializeField, Min(1f)] private float turnDegreesPerSecond = 540f;
    [SerializeField, Range(0.25f, 4f)] private float maximumDifficultySpeedMultiplier = 2.5f;

    [Header("Telegraphed Dive")]
    [SerializeField, Min(0.1f)] private float windupSeconds = 0.7f;
    [SerializeField, Min(0.2f)] private float maximumDiveSeconds = 1.3f;
    [SerializeField, Min(0.1f)] private float recoverSeconds = 1.5f;
    [SerializeField, Min(0.2f)] private float minimumAttackCooldown = 4.5f;
    [SerializeField, Min(0.2f)] private float maximumAttackCooldown = 6.5f;
    [SerializeField, Range(0f, 1f)] private float targetVelocityLeadSeconds = 0.28f;
    [SerializeField, Min(0f)] private float maximumTargetLeadDistance = 7f;
    [SerializeField, Min(0f)] private float flyPastDistance = 4f;

    [Header("Dive Damage")]
    [Tooltip("Scaled by HordeEnemy.DamageMultiplier. Normal touching is harmless.")]
    [SerializeField, Min(0f)] private float diveDamage = 13f;
    [SerializeField, Min(0.15f)] private float diveHitRadius = 1.15f;
    [Tooltip("Include the Player CharacterController and car colliders. Enemy colliders are filtered out.")]
    [SerializeField] private LayerMask diveHitLayers = (1 << 6) | (1 << 8);

    [Header("Environment")]
    [Tooltip("Optional solid scenery layers for flight avoidance. EXCLUDE player, car, and enemy layers.")]
    [SerializeField] private LayerMask obstacleLayers = 0;
    [Tooltip("Set to terrain/ground layers so aerial kills drop their XP on the ground.")]
    [SerializeField] private LayerMask xpGroundLayers = 1;

    [Header("Optional Readability / Audio")]
    [Tooltip("Optional child model root. Only tilts forward during a dive; stays level while orbiting. Never assign the Rigidbody root.")]
    [SerializeField] private Transform visualRoot;
    [Tooltip("Normally inactive orange/red warning visual, enabled during the windup.")]
    [SerializeField] private GameObject windupIndicator;
    [SerializeField] private AudioClip windupSound;
    [SerializeField] private AudioClip diveSound;
    [SerializeField] private GameObject crashParticles;
    [SerializeField, Range(0f, 1f)] private float audioVolume = 0.55f;

    [Header("Smooth Facing")]
    [Tooltip("Higher values track the target more responsively. Applies to yaw only; orbiting always looks at the player/car, not along the sideways velocity.")]
    [SerializeField, Min(0.1f)] private float facingResponsiveness = 10f;
    [Tooltip("Pitch the visual model forward during a dive only. Zero disables pitching.")]
    [SerializeField, Range(0f, 35f)] private float maximumDivePitchDegrees = 18f;

    [Header("Dive Trail")]
    [Tooltip("Assign a child TrailRenderer. Keep its GameObject active; this script toggles TrailRenderer.emitting only during a dive.")]
    [SerializeField] private TrailRenderer diveTrail;
    [SerializeField] private TrailRenderer diveTrail2;

    [Header("Default Layer Impact Sound")]
    [Tooltip("Played whenever this Rigidbody begins colliding with a collider on Unity's Default layer (layer 0).")]
    [SerializeField] private AudioClip defaultLayerImpactSound;
    [SerializeField, Range(0f, 1f)] private float impactSoundVolume = 0.8f;
    [Tooltip("Prevents rapid repeated collisions from stacking the sound. Set to 0 for every collision.")]
    [SerializeField, Min(0f)] private float impactSoundCooldown = 0.2f;

    [Tooltip("Optional rotor pivots; advanced in the central manager tick, not per-drone Update.")]
    [SerializeField] private Transform[] rotorPivots;
    [SerializeField, Min(0f)] private float rotorDegreesPerSecond = 1300f;

    public float SpawnHeightAboveGround => spawnHeightAboveGround;
    internal bool IsWarningVisual(Transform candidate) =>
        windupIndicator != null && candidate.IsChildOf(windupIndicator.transform);

    private readonly Collider[] overlapResults = new Collider[32];
    private readonly RaycastHit[] sweepResults = new RaycastHit[32];
    private HordeEnemy owner;
    private Rigidbody body;
    private PlayerHealth playerHealth;
    private PlayerVehicleInteractor vehicleInteractor;
    private CharacterController playerCollider;
    private CarController cachedCar;
    private Rigidbody cachedCarBody;
    private FlightPhase phase;
    private float phaseEndsAt;
    private float nextAttackAt;
    private float orbitSign;
    private bool attackingCar;
    private Vector3 lockedTarget;
    private Vector3 diveExitPoint;
    private Vector3 diveFacingDirection;
    private Vector3 previousDivePosition;
    private float nextImpactSoundAt;
    private Vector3 targetPosition;
    private Vector3 targetVelocity;
    private CarController targetCar;

    public AudioSource audioSource;
    public AudioClip hoverSound;
    public AudioClip altHoverSound;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        owner = GetComponent<HordeEnemy>();
        if (windupIndicator != null) windupIndicator.SetActive(false);
        SetDiveTrail(false, true);
    }

    private void Start()
    {
        int r = UnityEngine.Random.Range(0, 100);
        if(r < 50)
        {
            audioSource.clip = hoverSound;
            audioSource.Play();
        }
        else
        {
            audioSource.clip = altHoverSound;
            audioSource.Play();
        }
    }

    // Called by HordeEnemy.ResetForSpawn while the instance is INACTIVE.
    internal void ResetForSpawn(HordeEnemy enemy)
    {
        owner = enemy;
        if (body == null) body = GetComponent<Rigidbody>();
        CachePlayerReferences();
        orbitSign = Random.value < 0.5f ? -1f : 1f;
        phase = FlightPhase.Orbit;
        phaseEndsAt = 0f;
        nextAttackAt = Time.time + Random.Range(2f, 3.8f);
        attackingCar = false;
        lockedTarget = Vector3.zero;
        diveExitPoint = Vector3.zero;
        previousDivePosition = transform.position;
        diveFacingDirection = transform.forward;
        nextImpactSoundAt = float.NegativeInfinity;
        if (windupIndicator != null) windupIndicator.SetActive(false);
        if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        SetDiveTrail(false, true);
    }

    internal void ResetForPool()
    {
        phase = FlightPhase.Orbit;
        phaseEndsAt = 0f;
        nextAttackAt = 0f;
        targetCar = null;
        diveFacingDirection = Vector3.zero;
        if (windupIndicator != null && windupIndicator.activeSelf)
            windupIndicator.SetActive(false);
        if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        SetDiveTrail(false, true);
    }

    private void OnDisable()
    {
        // Pooling or disabling the component must never leave an active trail.
        SetDiveTrail(false, true);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Unity's built-in Default layer is index 0 (NOT the Ground layer unless assigned there).
        if (defaultLayerImpactSound == null || collision.collider == null ||
            collision.collider.gameObject.layer != 0 || Time.time < nextImpactSoundAt)
            return;

        nextImpactSoundAt = Time.time + Mathf.Max(0f, impactSoundCooldown);
        Vector3 position = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;
        AudioManager.Play3D(defaultLayerImpactSound, position,
            impactSoundVolume, 1f, 2f, 50f, 160);

        //GameObject sparks = Instantiate(crashParticles, collision.GetContact(0).point + new Vector3(0, 0.8f, 0), Quaternion.identity);
        //Destroy(sparks, 4);
    }

    internal void CancelForKnockback()
    {
        // Knockback and dives must never resume from stale pooled/telegraph state.
        EndDive();
    }

    private void CachePlayerReferences()
    {
        HordeManager manager = HordeManager.Instance;
        Transform player = manager != null ? manager.Player : null;
        playerHealth = player != null ? player.GetComponentInParent<PlayerHealth>() : null;
        if (playerHealth == null && player != null)
            playerHealth = player.GetComponentInChildren<PlayerHealth>(true);
        vehicleInteractor = playerHealth != null
            ? playerHealth.GetComponent<PlayerVehicleInteractor>() : null;
        playerCollider = playerHealth != null
            ? playerHealth.GetComponent<CharacterController>() : null;
        cachedCar = null;
        cachedCarBody = null;
    }

    // Called through HordeEnemy.ManagedTick, never independently.
    internal void ManagedTick(float dt, Vector3 fallbackPlayerPosition)
    {
        if (!isActiveAndEnabled || owner == null || owner.IsDead || body == null)
            return;

        // Handles entering/exiting the car during an attack.
        ResolveCurrentTarget(fallbackPlayerPosition);
        bool isCarNow = targetCar != null;
        if ((phase == FlightPhase.Telegraph || phase == FlightPhase.Dive) &&
            isCarNow != attackingCar)
            EndDive();

        float speedFactor = Mathf.Clamp(owner.SpeedMultiplier, 0.25f,
            maximumDifficultySpeedMultiplier);
        Vector3 desiredVelocity;

        switch (phase)
        {
            case FlightPhase.Telegraph:
                desiredVelocity = OrbitVelocity(speedFactor, 0.48f);
                if (Time.time >= phaseEndsAt)
                {
                    BeginDive();
                    desiredVelocity = DiveVelocity(speedFactor);
                }
                break;

            case FlightPhase.Dive:
                // Physics-driven sweep covers distance since the previous manager tick.
                // End the dive before applying damage, since thorns can instantly kill us.
                if (TryHitTarget(previousDivePosition, body.position))
                {
                    desiredVelocity = OrbitVelocity(speedFactor, 1f);
                }
                else if (Time.time >= phaseEndsAt ||
                    (body.position - diveExitPoint).sqrMagnitude < 2.25f)
                {
                    EndDive();
                    desiredVelocity = OrbitVelocity(speedFactor, 1f);
                }
                else
                {
                    desiredVelocity = DiveVelocity(speedFactor);
                    previousDivePosition = body.position;
                }
                break;

            case FlightPhase.Recover:
                desiredVelocity = OrbitVelocity(speedFactor, 1f);
                if (Time.time >= phaseEndsAt)
                    phase = FlightPhase.Orbit;
                break;

            default:
                desiredVelocity = OrbitVelocity(speedFactor, 1f);
                float flatDistanceSqr = HorizontalDistanceSquared(body.position, targetPosition);
                float attackRadius = orbitRadius + 5f;
                if (Time.time >= nextAttackAt &&
                    flatDistanceSqr < attackRadius * attackRadius &&
                    Mathf.Abs(body.position.y - (targetPosition.y + cruiseAltitudeAboveTarget)) < 4f)
                    BeginTelegraph();
                break;
        }

        // The damage callback can kill/despawn the drone through thorns.
        if (owner.IsDead || !owner.isActiveAndEnabled) return;

        if (rotorPivots != null)
            for (int i = 0; i < rotorPivots.Length; i++)
                if (rotorPivots[i] != null)
                    rotorPivots[i].Rotate(0f, rotorDegreesPerSecond * dt, 0f, Space.Self);

        if (phase != FlightPhase.Dive && obstacleLayers.value != 0 &&
            desiredVelocity.sqrMagnitude > 0.01f &&
            Physics.SphereCast(body.position, 0.45f, desiredVelocity.normalized,
                out RaycastHit obstacle, 2.4f, obstacleLayers,
                QueryTriggerInteraction.Ignore))
        {
            desiredVelocity = Vector3.ProjectOnPlane(desiredVelocity, obstacle.normal)
                + Vector3.up * 5f * speedFactor;
        }

        float accel = flightAcceleration * speedFactor;
        body.velocity = Vector3.MoveTowards(body.velocity, desiredVelocity,
            accel * Mathf.Max(0f, dt));
        FaceTarget(dt, speedFactor);
    }

    private void ResolveCurrentTarget(Vector3 fallback)
    {
        targetCar = vehicleInteractor != null && vehicleInteractor.IsInVehicle
            ? vehicleInteractor.currentCarController : null;
        if (targetCar != null && !targetCar.IsVehicleWrecked)
        {
            if (cachedCar != targetCar)
            {
                cachedCar = targetCar;
                cachedCarBody = targetCar.GetComponent<Rigidbody>();
            }
            targetPosition = (cachedCarBody != null
                ? cachedCarBody.worldCenterOfMass : targetCar.transform.position)
                + Vector3.up * 0.3f;
            targetVelocity = cachedCarBody != null ? cachedCarBody.velocity : Vector3.zero;
        }
        else
        {
            targetCar = null;
            targetPosition = playerCollider != null && playerCollider.enabled
                ? playerCollider.bounds.center : fallback + Vector3.up;
            targetVelocity = playerCollider != null && playerCollider.enabled
                ? playerCollider.velocity : Vector3.zero;
        }
    }

    private Vector3 OrbitVelocity(float speedFactor, float orbitSpeedFactor)
    {
        Vector3 away = body.position - targetPosition;
        away.y = 0f;
        float distance = away.magnitude;
        if (distance < 0.05f)
        {
            away = transform.right;
            away.y = 0f;
            distance = Mathf.Max(0.05f, away.magnitude);
        }

        Vector3 radial = away / distance;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial) * orbitSign;

        // Velocity damping prevents overshooting the orbit radius and bobbing vertically.
        // Both corrections remain smooth even when the manager uses a longer tick interval.
        float radialSpeed = Vector3.Dot(body.velocity, radial);
        float radialCorrection = Mathf.Clamp(
            (distance - orbitRadius) * 1.8f + radialSpeed * 1.1f, -8f, 8f);
        float altitudeError = targetPosition.y + cruiseAltitudeAboveTarget - body.position.y;
        float verticalCorrection = Mathf.Clamp(
            altitudeError * 1.9f - body.velocity.y * 1.15f, -9f, 9f);

        Vector3 orbitVelocity = tangent * (orbitSpeed * orbitSpeedFactor)
            - radial * radialCorrection + Vector3.up * verticalCorrection;

        // Blend into the circle instead of switching abruptly from approach to
        // sideways orbiting at a single distance threshold.
        Vector3 orbitPosition = targetPosition + radial * orbitRadius
            + Vector3.up * cruiseAltitudeAboveTarget;
        Vector3 approachVelocity = Vector3.ClampMagnitude(
            (orbitPosition - body.position) * 2.2f, approachSpeed * speedFactor);
        float blend = Mathf.InverseLerp(orbitRadius + 2f, orbitRadius + 9f, distance);
        blend = blend * blend * (3f - 2f * blend); // Smoothstep
        return Vector3.ClampMagnitude(
            Vector3.Lerp(orbitVelocity, approachVelocity, blend),
            approachSpeed * speedFactor);
    }

    private Vector3 DiveVelocity(float speedFactor)
    {
        Vector3 toExit = diveExitPoint - body.position;
        if (toExit.sqrMagnitude <= 0.01f)
            return transform.forward * diveSpeed * speedFactor;
        return toExit.normalized * (diveSpeed * speedFactor);
    }

    private void BeginTelegraph()
    {
        phase = FlightPhase.Telegraph;
        phaseEndsAt = Time.time + windupSeconds;
        attackingCar = targetCar != null;
        Vector3 lead = Vector3.ClampMagnitude(targetVelocity * targetVelocityLeadSeconds,
            maximumTargetLeadDistance);
        lockedTarget = targetPosition + lead;
        if (windupIndicator != null) windupIndicator.SetActive(true);
        PlaySound(windupSound);
    }

    private void BeginDive()
    {
        phase = FlightPhase.Dive;
        phaseEndsAt = Time.time + maximumDiveSeconds;
        if (windupIndicator != null) windupIndicator.SetActive(false);
        Vector3 direction = lockedTarget - body.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        direction.Normalize();
        // Exit slightly beyond the locked aim point, not under the terrain.
        diveExitPoint = lockedTarget + direction * flyPastDistance + Vector3.up * 0.8f;
        // Lock the visual heading for the whole pass: no 180-degree spin at the target.
        diveFacingDirection = direction;
        previousDivePosition = body.position;
        SetDiveTrail(true, true);
        PlaySound(diveSound);
    }

    private void EndDive()
    {
        phase = FlightPhase.Recover;
        phaseEndsAt = Time.time + recoverSeconds;
        nextAttackAt = Time.time + Random.Range(minimumAttackCooldown,
            Mathf.Max(minimumAttackCooldown, maximumAttackCooldown));
        if (windupIndicator != null && windupIndicator.activeSelf)
            windupIndicator.SetActive(false);
        SetDiveTrail(false, false); // Existing trail fades naturally after the pass.
    }

    private bool TryHitTarget(Vector3 from, Vector3 to)
    {
        if (targetCar == null && (playerHealth == null || playerHealth.CurrentHealth <= 0f))
            return false;

        int count = Physics.OverlapSphereNonAlloc(to, diveHitRadius, overlapResults,
            diveHitLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (IsCurrentTargetCollider(overlapResults[i]))
                return ApplyDiveHit();

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance > 0.01f)
        {
            count = Physics.SphereCastNonAlloc(from, diveHitRadius, delta / distance,
                sweepResults, distance, diveHitLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (IsCurrentTargetCollider(sweepResults[i].collider))
                    return ApplyDiveHit();
        }
        return false;
    }

    private bool IsCurrentTargetCollider(Collider candidate)
    {
        if (candidate == null || !candidate.enabled) return false;
        if (targetCar != null)
            return candidate.GetComponentInParent<CarController>() == targetCar;
        // Require the actual CharacterController, not gun/viewmodel/child colliders.
        return playerCollider != null && candidate == playerCollider;
    }

    private bool ApplyDiveHit()
    {
        float damage = diveDamage * owner.DamageMultiplier;
        // Retreat whether damage is accepted or resisted by a target cooldown.
        // A successful hit must never be applied twice by overlapping physics ticks.
        EndDive();
        if (damage > 0f)
        {
            if (targetCar != null)
                targetCar.TakeVehicleDamage(damage, owner);
            else if (playerHealth != null)
                playerHealth.TakeDamage(damage, owner);
        }
        return true;
    }

    private void FaceTarget(float dt, float speedFactor)
    {
        // Orbit / windup / recover: always look INWARD at the player or occupied car.
        // The drone strafes around the circle rather than pointing sideways into its
        // tangent velocity, which caused the old fighting/jittering rotations.
        // Dive: hold the heading chosen at take-off so crossing the target doesn't
        // suddenly make the drone spin around.
        Vector3 direction = phase == FlightPhase.Dive
            ? diveFacingDirection
            : targetPosition - body.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.25f)
        {
            Quaternion desiredFacing = Quaternion.LookRotation(direction.normalized);
            float follow = 1f - Mathf.Exp(-Mathf.Max(0.1f, facingResponsiveness) * dt);
            Quaternion smoothed = Quaternion.Slerp(body.rotation, desiredFacing, follow);
            Quaternion rotation = Quaternion.RotateTowards(body.rotation, smoothed,
                turnDegreesPerSecond * speedFactor * dt);

            // The drone owns its yaw. Prevent collision-induced angular velocity
            // from fighting the target-facing controller between managed ticks.
            body.angularVelocity = Vector3.zero;
            body.MoveRotation(rotation);
        }

        if (visualRoot != null)
        {
            // Keep the model perfectly level while orbiting. Only a committed
            // dive may gently pitch the nose down; no sideways orbit banking.
            float pitch = 0f;
            if (phase == FlightPhase.Dive)
            {
                Vector3 offset = diveExitPoint - body.position;
                float horizontalDistance = Mathf.Sqrt(offset.x * offset.x + offset.z * offset.z);
                pitch = Mathf.Clamp(
                    Mathf.Atan2(-offset.y, Mathf.Max(0.5f, horizontalDistance))
                    * Mathf.Rad2Deg, 0f, maximumDivePitchDegrees);
            }
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation,
                Quaternion.Euler(pitch, 0f, 0f), Mathf.Clamp01(dt * 7f));
        }
    }

    private void SetDiveTrail(bool emitting, bool clear)
    {
        if (diveTrail == null) return;
        diveTrail.emitting = emitting;
        if (clear) diveTrail.Clear();

        if (diveTrail2 == null) return;
        diveTrail2.emitting = emitting;
        if (clear) diveTrail2.Clear();
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null)
            AudioManager.Play3D(clip, transform.position, audioVolume, 1f, 2f, 55f, 160);
    }

    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float x = a.x - b.x;
        float z = a.z - b.z;
        return x * x + z * z;
    }

    // Called by HordeEnemy when this drone dies. XP still uses the shared XPBlobPool.
    internal Vector3 GetGroundedXPPosition()
    {
        int mask = xpGroundLayers.value & ~(1 << 7);
        if (mask != 0 && Physics.Raycast(transform.position + Vector3.up * 2f,
            Vector3.down, out RaycastHit ground, 150f, mask,
            QueryTriggerInteraction.Ignore))
            return ground.point + Vector3.up * 0.25f;
        return transform.position;
    }
}
