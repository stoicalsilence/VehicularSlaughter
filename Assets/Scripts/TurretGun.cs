
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Autonomous hitscan turret for a 3D horde shooter.
/// Requires DOTween.
/// Uses one assigned LineRenderer for bullet tracers.
/// </summary>
// Run the indicator LateUpdate after ordinary camera LateUpdate callbacks.
[DefaultExecutionOrder(100)]
public class TurretGun : MonoBehaviour
{
    [Header("References")]
    public CarStats carStats;
    private PlayerStats playerStats;
    private PlayerInventory playerInventory;
    private CarController owningCar;
    public AudioSource audioSource;

    [Tooltip("Transform that rotates to aim and moves into the reload pose.")]
    public Transform GunVisual;

    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private LineRenderer bulletTracer;

    [SerializeField] private float damagePerShot => carStats.turretDamagePerShot;

    [SerializeField] private float knockbackForce => carStats.turretKnockbackForce;

    [SerializeField] private float fireRate => carStats.turretFireRate;

    [SerializeField] private float accuracySpreadDegrees => carStats.turretAccuracySpreadDegrees;

    [SerializeField] public float criticalChance => carStats.turretCriticalChance;

    [SerializeField] public float criticalDamageMultiplier => carStats.turretCriticalDamageMultiplier;

    [SerializeField] private int magazineSize => carStats.turretMagazineSize;

    [SerializeField] private float reloadTime => carStats.turretReloadTime;

    // Support Beams only buffs the turret while its car's parking brake is engaged.
    private int SupportBeamsStacks => owningCar != null && owningCar.IsParkingBrakeEngaged &&
        playerInventory != null ? Mathf.Max(0, playerInventory.GetStacksByName("Support Beams")) : 0;

    private float EffectiveFireRate => fireRate * (1f + 0.2f * SupportBeamsStacks);

    // Additive -20% per stack, with a 5% minimum to avoid zero/negative reloads.
    private float EffectiveReloadTime => Mathf.Max(0.01f, reloadTime) *
        Mathf.Max(0.05f, 1f - 0.2f * SupportBeamsStacks);

    [SerializeField] private float detectionRange => carStats.turretDetectionRange;

    [SerializeField] private float maxShotDistance => carStats.turretMaxShotDistance;


    [Header("Targeting")]
    [Min(0.01f)]
    [SerializeField] private float targetScanInterval = 0.15f;

    [SerializeField] private LayerMask enemyLayers = ~0;

    [SerializeField] private bool requireEnemyTag;
    [SerializeField] private string enemyTag = "Enemy";

    [SerializeField] private Vector3 targetAimOffset = new Vector3(0f, 1.2f, 0f);

    private const float MarkedTargetRangeMultiplier = 2f;

    [Header("Marked Target Indicator")]
    [Tooltip("Optional single scene object, shared by every marked enemy. No collider needed.")]
    [SerializeField] private GameObject markedTargetIndicator;
    [SerializeField] private Vector3 markedTargetIndicatorOffset = new Vector3(0f, 2.25f, 0f);
    [Tooltip("Optional local rotation correction for the arrow artwork. Usually zero; try Y = 180 if the visible face is reversed.")]
    [SerializeField] private Vector3 markedTargetIndicatorEulerOffset = Vector3.zero;
    [Tooltip("Optional player reference for the active view camera. Automatically found once at Start if unassigned.")]
    [SerializeField] private PlayerVehicleInteractor indicatorViewSource;


    [Header("Aiming (DOTween)")]
    [Min(0.01f)]
    [SerializeField] private float aimTweenDuration = 0.12f;

    [Min(0.02f)]
    [SerializeField] private float aimTweenRefreshInterval = 0.08f;

    [Tooltip("Only create a new aim tween when the desired rotation changes by this amount.")]
    [Range(0.05f, 10f)]
    [SerializeField] private float aimRetargetThreshold = 0.75f;

    [Range(0f, 30f)]
    [SerializeField] private float aimToleranceDegrees = 3f;

    [SerializeField] private Ease aimEase = Ease.OutSine;


    [Header("Weapon Settings")]
    [Tooltip("Layers that can stop a shot. Include enemies and world geometry.")]
    [SerializeField] private LayerMask shotLayers = ~0;

    [SerializeField] private string damageMessage = "TakeDamage";


    [Header("Reload Animation")]
    [Min(0.01f)]
    [SerializeField] private float reloadPoseTransitionTime = 0.2f;

    [SerializeField] private Vector3 reloadLocalEulerAngles = new Vector3(25f, 180f, 0f);

    [SerializeField] private Ease reloadPoseEase = Ease.InOutSine;


    [Header("Muzzle Flash")]
    [Min(0.01f)]
    [SerializeField] private float muzzleFlashDuration = 0.06f;

    [SerializeField] private Vector2 muzzleFlashRandomX = new Vector2(0f, 360f);


    [Header("Bullet Tracer")]
    [Min(0.01f)]
    [SerializeField] private float tracerLifetime = 0.07f;


    [Header("Bullet Casings")]
    [SerializeField] private GameObject casingPrefab;
    [SerializeField] private Transform casingEjectPoint;

    [Min(0.1f)]
    [SerializeField] private float casingLifetime = 4f;
    private int totalShotsFired = 0;
    [SerializeField] private Vector2 casingSideForce = new Vector2(1.2f, 2.4f);
    [SerializeField] private Vector2 casingUpForce = new Vector2(0.5f, 1.2f);

    [Min(0f)]
    [SerializeField] private float casingRandomTorque = 8f;


    [Header("Audio")]
    [SerializeField] public AudioClip fireSound;
    [SerializeField] private AudioClip critSound;
    [SerializeField] private AudioClip overcritSound;
    [SerializeField] private AudioClip overcritSound2;
    [SerializeField] private AudioClip overcritSound3;
    [SerializeField] private AudioClip overcritSound4;
    [SerializeField] private AudioClip markTargetSound;

    [Range(0f, 1f)]
    [SerializeField] private float fireSoundVolume = 1f;

    [SerializeField] private AudioClip dirtImpactSound;

    public GameObject metalSparkParticles;
    public GameObject dirtSparkParticles;

    [Range(0f, 1f)]
    [SerializeField] private float impactSoundVolume = 0.8f;

    [SerializeField] private AudioClip reloadSound;

    [Range(0f, 1f)]
    [SerializeField] private float reloadSoundVolume = 0.8f;


    // Target detection
    private readonly Collider[] detectionBuffer = new Collider[128];

    private readonly HashSet<Transform> checkedTargets =
        new HashSet<Transform>();

    private Transform currentTarget;
    private Transform markedTarget;
    private HordeEnemy markedEnemy;

    // Weapon state
    private int currentAmmo;
    private bool isReloading;
    private bool empDisabled;
    public bool IsEmpDisabled => empDisabled;

    private float nextShotTime;
    private float nextTargetScanTime;

    // Muzzle flash timer
    private float muzzleFlashOffTime;

    // Bullet tracer timer
    private float tracerOffTime;

    // Aiming
    private float nextAimTweenTime;

    private Quaternion lastAimTweenRotation;
    private bool hasAimTweenRotation;

    private Tween aimTween;
    private Sequence reloadSequence;
    private float reloadBaseDuration;


    public int CurrentAmmo => currentAmmo;
    public int MagazineSize => magazineSize;
    public bool IsReloading => isReloading;
    public Transform CurrentTarget => currentTarget;
    public bool HasMarkedTarget => markedTarget != null;
    public Transform MarkedTarget => markedTarget;


    private void Awake()
    {
        if (carStats == null)
            carStats = GetComponentInParent<CarStats>();

        if (carStats == null)
        {
            Debug.LogError(
                "TurretGun requires CarStats on itself or a parent GameObject.",
                this
            );

            enabled = false;
            return;
        }

        owningCar = GetComponentInParent<CarController>();
        currentAmmo = magazineSize;
        SetMarkedIndicatorActive(false);

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);

        // The LineRenderer is a scene component, not a prefab.
        if (bulletTracer != null)
        {
            bulletTracer.useWorldSpace = true;
            bulletTracer.positionCount = 2;
            bulletTracer.enabled = false;
        }
    }

    private void Start()
    {
        playerStats = FindObjectOfType<PlayerStats>();
        playerInventory = FindObjectOfType<PlayerInventory>();

        if (markedTargetIndicator != null && indicatorViewSource == null)
            indicatorViewSource = FindObjectOfType<PlayerVehicleInteractor>();
    }


    private void Update()
    {
        if (Time.timeScale <= 0f || empDisabled)
            return;

        UpdateMuzzleFlash();
        UpdateBulletTracer();
        UpdateTarget();

        // Adjust in-progress reloads immediately when parking brake / stacks change.
        if (isReloading && reloadSequence != null && reloadSequence.IsActive())
            reloadSequence.timeScale = reloadBaseDuration / EffectiveReloadTime;

        if (GunVisual == null || isReloading)
            return;

        if (currentTarget == null)
        {
            if (aimTween != null && aimTween.IsActive())
                aimTween.Kill();

            hasAimTweenRotation = false;
            return;
        }

        Vector3 aimPoint = GetAimPoint(currentTarget);

        if (!TryGetAimRotation(aimPoint, out Quaternion desiredRotation))
            return;

        UpdateAimTween(desiredRotation);

        if (currentAmmo <= 0)
        {
            BeginReload();
            return;
        }

        Vector3 shotOrigin = muzzlePoint != null
            ? muzzlePoint.position
            : GunVisual.position;

        float distanceToTarget =
            Vector3.Distance(shotOrigin, aimPoint);

        // Only a currently prioritized mark can bypass the normal weapon reach.
        // Reach its aim point without globally changing the CarStats shot range.
        float shotDistance = currentTarget == markedTarget
            ? Mathf.Max(maxShotDistance, distanceToTarget)
            : maxShotDistance;

        if (distanceToTarget > shotDistance)
            return;

        if (Time.time >= nextShotTime &&
            Quaternion.Angle(GunVisual.rotation, desiredRotation)
                <= aimToleranceDegrees)
        {
            FireAtTarget(aimPoint, shotDistance);
        }
    }


    // Separate power state: never enable a gun that was disabled for another reason.
    // Wreck handling can keep its original enabled-state bookkeeping.
    public void SetEmpDisabled(bool value)
    {
        if (empDisabled == value) return;
        empDisabled = value;
        if (value) StopWeaponActivity();
    }

    private void OnDisable()
    {
        StopWeaponActivity();
    }

    private void StopWeaponActivity()
    {
        aimTween?.Kill();
        reloadSequence?.Kill();

        aimTween = null;
        reloadSequence = null;

        isReloading = false;
        hasAimTweenRotation = false;
        DisregardMarkedTarget();
        nextTargetScanTime = 0f;
        nextAimTweenTime = 0f;

        if (bulletTracer != null)
            bulletTracer.enabled = false;

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);
    }


    private void OnDestroy()
    {
        DisregardMarkedTarget();
        aimTween?.Kill();
        reloadSequence?.Kill();
    }


    // --------------------------------------------------
    // TARGET MANAGEMENT
    // --------------------------------------------------

    public void MarkTarget(Transform target)
    {
        if (!isActiveAndEnabled || empDisabled)
            return;

        if (target == null)
        {
            DisregardMarkedTarget();
            return;
        }

        // Resolve child hitboxes to the enemy root. Keep the existing public
        // Transform API usable by other target types as well.
        HordeEnemy enemy = target.GetComponentInParent<HordeEnemy>();
        if (enemy != null)
        {
            if (enemy.IsDead || !enemy.isActiveAndEnabled)
                return;

            target = enemy.transform;
        }

        if (!target.gameObject.activeInHierarchy || target == transform ||
            target.IsChildOf(transform) || target == markedTarget)
            return;

        DisregardMarkedTarget();
        markedTarget = target;
        markedEnemy = enemy;

        // Subscribe ONLY to the selected enemy. Pooling can reuse the exact
        // same Transform before the next turret Update, so polling alone
        // is not sufficient to identify a new enemy life.
        if (markedEnemy != null)
            markedEnemy.TurretTargetInvalidated += OnMarkedEnemyInvalidated;

        UpdateMarkedIndicatorPose();
        SetMarkedIndicatorActive(true);

        AudioManager.Play2D(markTargetSound, 1);

        // Select immediately if in range; otherwise run ordinary targeting.
        UpdateTarget();
    }


    public void DisregardMarkedTarget()
    {
        if (markedEnemy != null)
            markedEnemy.TurretTargetInvalidated -= OnMarkedEnemyInvalidated;

        markedEnemy = null;
        markedTarget = null;
        SetMarkedIndicatorActive(false);
        SetCurrentTarget(null);
        nextTargetScanTime = 0f;
    }


    private void OnMarkedEnemyInvalidated(HordeEnemy enemy)
    {
        if (enemy == markedEnemy)
            DisregardMarkedTarget();
    }


    private void SetCurrentTarget(Transform target)
    {
        bool changed = currentTarget != target;
        currentTarget = target;

        if (!changed)
            return;

        nextAimTweenTime = 0f;
        hasAimTweenRotation = false;
    }


    private void LateUpdate()
    {
        // A single world-space indicator follows the mark, including while
        // the turret is using a temporary fallback target.
        if (markedTarget != null && markedTargetIndicator != null)
            UpdateMarkedIndicatorPose();
    }


    private void UpdateMarkedIndicatorPose()
    {
        if (markedTargetIndicator == null || markedTarget == null)
            return;

        Transform indicator = markedTargetIndicator.transform;
        Vector3 position = markedTarget.position + markedTargetIndicatorOffset;
        Camera viewCamera = indicatorViewSource != null
            ? indicatorViewSource.ActiveViewCamera
            : null;

        if (viewCamera != null && viewCamera.isActiveAndEnabled)
        {
            // Screen-aligned billboard: world rotation follows the actual view,
            // never the car, turret or enemy's inherited rotation.
            Quaternion rotation = viewCamera.transform.rotation *
                Quaternion.Euler(markedTargetIndicatorEulerOffset);
            indicator.SetPositionAndRotation(position, rotation);
        }
        else
        {
            indicator.position = position;
        }
    }


    private void SetMarkedIndicatorActive(bool active)
    {
        if (markedTargetIndicator != null && markedTargetIndicator.activeSelf != active)
            markedTargetIndicator.SetActive(active);
    }


    public void Reload()
    {
        BeginReload();
    }


    private void UpdateTarget()
    {
        Vector3 origin = transform.position;
        float normalRange = Mathf.Max(0f, detectionRange);
        float normalRangeSqr = normalRange * normalRange;

        // ReferenceEquals distinguishes a destroyed Unity object from an
        // actual null reference, so even non-HordeEnemy marks are cleaned up.
        if (!ReferenceEquals(markedTarget, null))
        {
            if (markedTarget == null || !markedTarget.gameObject.activeInHierarchy ||
                (markedEnemy != null && (markedEnemy.IsDead || !markedEnemy.isActiveAndEnabled)))
            {
                DisregardMarkedTarget();
            }
            else
            {
                float markedRange = normalRange * MarkedTargetRangeMultiplier;
                float distanceSqr = (markedTarget.position - origin).sqrMagnitude;

                // O(1) per frame; no overlap query at the expanded radius.
                if (distanceSqr <= markedRange * markedRange)
                {
                    SetCurrentTarget(markedTarget);
                    return;
                }

                // The mark is retained but no longer has priority. Scan now
                // rather than waiting for the normal scan timer.
                if (currentTarget == markedTarget)
                {
                    SetCurrentTarget(null);
                    nextTargetScanTime = 0f;
                }
            }
        }

        if (currentTarget != null &&
            (!currentTarget.gameObject.activeInHierarchy ||
             (currentTarget.position - origin).sqrMagnitude > normalRangeSqr))
        {
            SetCurrentTarget(null);
            nextTargetScanTime = 0f;
        }

        if (Time.time < nextTargetScanTime)
            return;

        nextTargetScanTime =
            Time.time + Mathf.Max(0.01f, targetScanInterval);

        SetCurrentTarget(FindClosestTarget());
    }


    private Transform FindClosestTarget()
    {
        float normalRange = Mathf.Max(0f, detectionRange);
        float normalRangeSqr = normalRange * normalRange;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            normalRange,
            detectionBuffer,
            enemyLayers,
            QueryTriggerInteraction.Collide);

        Transform closest = null;
        float closestSqrDistance = float.PositiveInfinity;

        checkedTargets.Clear();

        for (int i = 0; i < count; i++)
        {
            Collider candidateCollider = detectionBuffer[i];

            if (candidateCollider == null)
                continue;

            Transform candidate =
                ResolveTargetTransform(candidateCollider);

            if (candidate == null ||
                candidate == transform ||
                candidate.IsChildOf(transform) ||
                !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!checkedTargets.Add(candidate))
                continue;

            if (!HasRequiredTag(candidateCollider.transform))
                continue;

            float sqrDistance =
                (candidate.position - transform.position).sqrMagnitude;

            // A collider can overlap the sphere even if its root is outside.
            // Only the explicit mark is eligible for the expanded range.
            if (sqrDistance <= normalRangeSqr && sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
                closest = candidate;
            }
        }

        return closest;
    }


    private Transform ResolveTargetTransform(Collider candidateCollider)
    {
        ITurretDamageable damageable =
            candidateCollider.GetComponentInParent<ITurretDamageable>();

        // Dead or disabled horde enemies must not be picked back up by
        // the fallback scan, even if their pool return is deferred.
        if (damageable is HordeEnemy enemy && (enemy.IsDead || !enemy.isActiveAndEnabled))
            return null;

        if (damageable is Component damageableComponent)
            return damageableComponent.transform;

        if (candidateCollider.attachedRigidbody != null)
            return candidateCollider.attachedRigidbody.transform;

        return candidateCollider.transform;
    }


    private bool HasRequiredTag(Transform candidate)
    {
        if (!requireEnemyTag)
            return true;

        Transform current = candidate;

        while (current != null)
        {
            if (current.tag == enemyTag)
                return true;

            current = current.parent;
        }

        return false;
    }


    // --------------------------------------------------
    // AIMING
    // --------------------------------------------------

    private Vector3 GetAimPoint(Transform target)
    {
        return target.position + targetAimOffset;
    }


    private bool TryGetAimRotation(
        Vector3 aimPoint,
        out Quaternion desiredRotation)
    {
        Vector3 direction = aimPoint - GunVisual.position;

        if (direction.sqrMagnitude < 0.0001f)
        {
            desiredRotation = GunVisual.rotation;
            return false;
        }

        desiredRotation = Quaternion.LookRotation(
            direction.normalized,
            transform.up);

        return true;
    }


    private void UpdateAimTween(Quaternion desiredRotation)
    {
        float rotationDifference = hasAimTweenRotation
            ? Quaternion.Angle(lastAimTweenRotation, desiredRotation)
            : float.PositiveInfinity;

        // No need to create another tween for tiny rotation changes.
        if (rotationDifference < aimRetargetThreshold)
            return;

        // Prevent excessive tween creation.
        if (Time.time < nextAimTweenTime)
            return;

        aimTween?.Kill();

        aimTween = GunVisual
            .DORotateQuaternion(
                desiredRotation,
                Mathf.Max(0.01f, aimTweenDuration))
            .SetEase(aimEase);

        lastAimTweenRotation = desiredRotation;
        hasAimTweenRotation = true;

        nextAimTweenTime =
            Time.time + Mathf.Max(0.02f, aimTweenRefreshInterval);
    }


    // --------------------------------------------------
    // SHOOTING
    // --------------------------------------------------


    private void FireAtTarget(Vector3 aimPoint, float shotDistance)
    {
        Vector3 shotOrigin = muzzlePoint != null
            ? muzzlePoint.position
            : GunVisual.position;

        Vector3 baseDirection = aimPoint - shotOrigin;

        if (baseDirection.sqrMagnitude < 0.0001f)
            return;

        baseDirection.Normalize();

        totalShotsFired++;

        bool puteyAngryDoubleDamage =
            playerInventory != null &&
            playerInventory.HasItemByName("Putey (Angry)") &&
            totalShotsFired % 10 == 0;

        Vector3 shotDirection = ApplyAccuracySpread(baseDirection);

        // Use the effective range for both the ray and its visual tracer.
        Vector3 tracerEnd = shotOrigin + shotDirection * shotDistance;

        // If something is hit, stop the tracer at the impact point.
        if (Physics.Raycast(
            shotOrigin,
            shotDirection,
            out RaycastHit hit,
            shotDistance,
            shotLayers,
            QueryTriggerInteraction.Ignore))
        {
            tracerEnd = hit.point;

            int criticalHits = RollCriticalHits();

            float finalDamage = CalculateCriticalDamage(
                damagePerShot,
                criticalHits);

            if (playerInventory.HasItemByName("Advanced Computer System"))
            {
                int stacks = playerInventory.GetStacksByName("Advanced Computer System");
                finalDamage += 25 * stacks;
            }

            finalDamage += GetBonusSodaDamage();

            if (playerInventory.HasItemByName("OP Turret"))
            {
                finalDamage *= 2;
            }

            if (puteyAngryDoubleDamage)
            {
                finalDamage *= 2f;
            }

            ApplyDamage(hit, finalDamage, criticalHits);
            if (!isActiveAndEnabled || empDisabled)
            {
                currentAmmo = Mathf.Max(0, currentAmmo - 1);
                return;
            }

            if (hit.collider.gameObject.layer == 7)
            {
                GameObject sparks = Instantiate(metalSparkParticles, hit.point, Quaternion.identity);
                Destroy(sparks, 5);

                switch (criticalHits)
                {
                    case 1:
                        AudioManager.Play3D(
                        critSound,
                        hit.point,
                        impactSoundVolume, 1, 20, 50); break;
                    case 2:
                        AudioManager.Play3D(
                        overcritSound,
                        hit.point,
                        impactSoundVolume, 1, 20, 50); break;
                    case 3:
                        AudioManager.Play3D(
                        overcritSound2,
                        hit.point,
                        impactSoundVolume, 1, 20, 50); break;
                    case 4:
                        AudioManager.Play3D(
                        overcritSound3,
                        hit.point,
                        impactSoundVolume, 0.8f, 20, 50); break;
                    case 5:
                        AudioManager.Play3D(
                        overcritSound4,
                        hit.point,
                        impactSoundVolume, 1, 20, 50); break;
                }
            }
            else
            {
                GameObject sparks = Instantiate(dirtSparkParticles, hit.point, Quaternion.identity);
                Destroy(sparks, 5);

                AudioManager.Play3D(
                    dirtImpactSound,
                    hit.point,
                    impactSoundVolume, 1, 20, 50);
            }
        }

        ShowTracer(shotOrigin, tracerEnd);
        PlayMuzzleFlash();
        EjectCasing();

        if (fireSound != null)
        {
            audioSource.PlayOneShot(fireSound);
            //AudioManager.Play3D(
            //    fireSound,
            //    shotOrigin,
            //    fireSoundVolume, 1, 1, 50);
        }

        currentAmmo = Mathf.Max(0, currentAmmo - 1);

        if (currentAmmo <= 0)
        {
            BeginReload();
            return;
        }

        nextShotTime =
            Time.time + (1f / Mathf.Max(0.01f, EffectiveFireRate));
    }



    private Vector3 ApplyAccuracySpread(Vector3 direction)
    {
        if (accuracySpreadDegrees <= 0f)
            return direction;

        float spread =
            Mathf.Tan(accuracySpreadDegrees * Mathf.Deg2Rad);

        Vector2 randomSpread =
            Random.insideUnitCircle * spread;

        Vector3 right = GunVisual.right;
        Vector3 up = GunVisual.up;

        return (
            direction +
            right * randomSpread.x +
            up * randomSpread.y
        ).normalized;
    }


    private void ApplyDamage(
    RaycastHit hit,
    float finalDamage,
    int criticalHits)
    {
        ITurretDamageable damageable =
            hit.collider.GetComponentInParent<ITurretDamageable>();

        if (damageable != null)
        {
            damageable.TakeDamage(
                finalDamage,
                hit.point,
                knockbackForce,
                criticalHits);

            return;
        }

        // Fallback for non-ITurretDamageable objects.
        hit.collider.SendMessageUpwards(
            damageMessage,
            finalDamage,
            SendMessageOptions.DontRequireReceiver);
    }


    // --------------------------------------------------
    // BULLET TRACER
    // --------------------------------------------------

    private void ShowTracer(Vector3 start, Vector3 end)
    {
        if (bulletTracer == null)
            return;

        bulletTracer.SetPosition(0, start);
        bulletTracer.SetPosition(1, end);

        bulletTracer.enabled = true;

        tracerOffTime =
            Time.time + Mathf.Max(0.01f, tracerLifetime);
    }


    private void UpdateBulletTracer()
    {
        if (bulletTracer == null || !bulletTracer.enabled)
            return;

        if (Time.time >= tracerOffTime)
            bulletTracer.enabled = false;
    }


    // --------------------------------------------------
    // MUZZLE FLASH
    // --------------------------------------------------

    private void PlayMuzzleFlash()
    {
        if (muzzleFlash == null)
            return;

        Vector3 localEuler =
            muzzleFlash.transform.localEulerAngles;

        localEuler.x = Random.Range(
            muzzleFlashRandomX.x,
            muzzleFlashRandomX.y);

        muzzleFlash.transform.localEulerAngles = localEuler;

        muzzleFlash.SetActive(true);

        muzzleFlashOffTime =
            Time.time + muzzleFlashDuration;
    }


    private void UpdateMuzzleFlash()
    {
        if (muzzleFlash != null &&
            muzzleFlash.activeSelf &&
            Time.time >= muzzleFlashOffTime)
        {
            muzzleFlash.SetActive(false);
        }
    }


    // --------------------------------------------------
    // CASING EJECTION
    // --------------------------------------------------

    private void EjectCasing()
    {
        if (casingPrefab == null || casingEjectPoint == null)
            return;

        GameObject casing = Instantiate(
            casingPrefab,
            casingEjectPoint.position,
            casingEjectPoint.rotation);

        Rigidbody casingBody = casing.GetComponent<Rigidbody>();

        if (casingBody != null)
        {
            Vector3 ejectionForce =
                casingEjectPoint.right *
                Random.Range(casingSideForce.x, casingSideForce.y) +

                casingEjectPoint.up *
                Random.Range(casingUpForce.x, casingUpForce.y);

            casingBody.AddForce(
                ejectionForce,
                ForceMode.Impulse);

            casingBody.AddTorque(
                Random.insideUnitSphere * casingRandomTorque,
                ForceMode.Impulse);
        }

        Destroy(casing, casingLifetime);
    }


    // --------------------------------------------------
    // RELOADING
    // --------------------------------------------------

    private void BeginReload()
    {
        if (!isActiveAndEnabled || empDisabled || isReloading || currentAmmo >= magazineSize)
            return;

        isReloading = true;

        aimTween?.Kill();
        aimTween = null;
        hasAimTweenRotation = false;

        float totalReloadTime =
            Mathf.Max(0.01f, reloadTime);
        reloadBaseDuration = totalReloadTime;

        float poseTime = Mathf.Min(
            Mathf.Max(0.01f, reloadPoseTransitionTime),
            totalReloadTime);

        float holdTime =
            Mathf.Max(0f, totalReloadTime - poseTime);

        reloadSequence?.Kill();

        reloadSequence = DOTween.Sequence();

        if (GunVisual != null)
        {
            reloadSequence.Append(
                GunVisual
                    .DOLocalRotate(
                        reloadLocalEulerAngles,
                        poseTime,
                        RotateMode.Fast)
                    .SetEase(reloadPoseEase));
        }
        else
        {
            reloadSequence.AppendInterval(poseTime);
        }

        reloadSequence.AppendInterval(holdTime);
        reloadSequence.timeScale = reloadBaseDuration / EffectiveReloadTime;

        reloadSequence.OnComplete(() =>
        {
            currentAmmo = magazineSize;

            isReloading = false;
            reloadSequence = null;

            nextAimTweenTime = 0f;
            hasAimTweenRotation = false;
        });

        if (reloadSound != null)
        {
            AudioManager.Play3D(
                reloadSound,
                transform.position,
                reloadSoundVolume);
        }
    }


    // --------------------------------------------------
    // DEBUG
    // --------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        if (carStats == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position,
            detectionRange * MarkedTargetRangeMultiplier);
    }

    private int RollCriticalHits()
    {
        float remainingChance = Mathf.Max(0f, criticalChance);

        // Every complete 100% guarantees another critical hit.
        int criticalHits = Mathf.FloorToInt(remainingChance / 100f);

        // Roll the remaining percentage.
        float remainingPercentage = remainingChance % 100f;

        if (Random.value < remainingPercentage / 100f)
            criticalHits++;

        return criticalHits;
    }

    private float CalculateCriticalDamage(
    float baseDamage,
    int criticalHits)
    {
        if (criticalHits <= 0)
            return baseDamage;

        return baseDamage * Mathf.Pow(
            Mathf.Max(1f, criticalDamageMultiplier),
            criticalHits);
    }

    private float GetBonusSodaDamage()
    {
        if (playerStats == null || playerInventory == null)
            return 0f;

        if (!playerInventory.HasItemByName("Bonus Soda"))
            return 0f;

        int stacks = playerInventory.GetStacksByName("Bonus Soda");

        return playerStats.MaxHealth * 0.2f * stacks;
    }
}
