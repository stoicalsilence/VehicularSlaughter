
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Autonomous hitscan turret for a 3D horde shooter.
/// Requires DOTween.
/// Uses one assigned LineRenderer for bullet tracers.
/// </summary>
public class TurretGun : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform that rotates to aim and moves into the reload pose.")]
    public Transform GunVisual;

    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private LineRenderer bulletTracer;


    [Header("UPGRADE STATS")]
    [Space(5)]

    [Header("Offensive Stats")]
    [Min(0f)]
    [SerializeField] private float damagePerShot = 10f;

    [Tooltip("Knockback force applied to enemies hit by this turret.")]
    [Min(0f)]
    [SerializeField] private float knockbackForce = 5f;

    [Tooltip("Shots fired per second.")]
    [Min(0.01f)]
    [SerializeField] private float fireRate = 6f;

    [Tooltip("Accuracy spread in degrees.")]
    [Range(0f, 20f)]
    [SerializeField] private float accuracySpreadDegrees = 1.25f;


    [Header("Magazine Stats")]
    [Min(1)]
    [SerializeField] private int magazineSize = 30;

    [Min(0.01f)]
    [SerializeField] private float reloadTime = 2.2f;


    [Header("Range Stats")]
    [Min(0f)]
    [SerializeField] private float detectionRange = 20f;

    [Min(0.1f)]
    [SerializeField] private float maxShotDistance = 40f;


    [Header("Targeting")]
    [Min(0.01f)]
    [SerializeField] private float targetScanInterval = 0.15f;

    [SerializeField] private LayerMask enemyLayers = ~0;

    [SerializeField] private bool requireEnemyTag;
    [SerializeField] private string enemyTag = "Enemy";

    [SerializeField] private Vector3 targetAimOffset = new Vector3(0f, 1.2f, 0f);

    [SerializeField] private bool markedTargetMustBeInRange;


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

    [SerializeField] private Vector2 casingSideForce = new Vector2(1.2f, 2.4f);
    [SerializeField] private Vector2 casingUpForce = new Vector2(0.5f, 1.2f);

    [Min(0f)]
    [SerializeField] private float casingRandomTorque = 8f;


    [Header("Audio")]
    [SerializeField] private AudioClip fireSound;

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

    // Weapon state
    private int currentAmmo;
    private bool isReloading;

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


    public int CurrentAmmo => currentAmmo;
    public int MagazineSize => magazineSize;
    public bool IsReloading => isReloading;
    public Transform CurrentTarget => currentTarget;
    public bool HasMarkedTarget => markedTarget != null;


    private void Awake()
    {
        currentAmmo = magazineSize;

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


    private void Update()
    {
        UpdateMuzzleFlash();
        UpdateBulletTracer();
        UpdateTarget();

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

        if (distanceToTarget > maxShotDistance)
            return;

        if (Time.time >= nextShotTime &&
            Quaternion.Angle(GunVisual.rotation, desiredRotation)
                <= aimToleranceDegrees)
        {
            FireAtTarget(aimPoint, distanceToTarget);
        }
    }


    private void OnDisable()
    {
        aimTween?.Kill();
        reloadSequence?.Kill();

        aimTween = null;
        reloadSequence = null;

        isReloading = false;
        hasAimTweenRotation = false;

        if (bulletTracer != null)
            bulletTracer.enabled = false;

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);
    }


    private void OnDestroy()
    {
        aimTween?.Kill();
        reloadSequence?.Kill();
    }


    // --------------------------------------------------
    // TARGET MANAGEMENT
    // --------------------------------------------------

    public void MarkTarget(Transform target)
    {
        markedTarget = target;
        currentTarget = target;

        nextTargetScanTime = 0f;
        nextAimTweenTime = 0f;

        hasAimTweenRotation = false;
    }


    public void DisregardMarkedTarget()
    {
        markedTarget = null;
        currentTarget = null;

        nextTargetScanTime = 0f;
        nextAimTweenTime = 0f;

        hasAimTweenRotation = false;
    }


    public void Reload()
    {
        BeginReload();
    }


    private void UpdateTarget()
    {
        if (markedTarget != null)
        {
            if (!markedTarget.gameObject.activeInHierarchy)
            {
                markedTarget = null;
                currentTarget = null;
            }
            else
            {
                float distanceSqr =
                    (markedTarget.position - transform.position).sqrMagnitude;

                bool inRange =
                    distanceSqr <= detectionRange * detectionRange;

                currentTarget =
                    (!markedTargetMustBeInRange || inRange)
                    ? markedTarget
                    : null;

                return;
            }
        }

        if (currentTarget != null &&
            !currentTarget.gameObject.activeInHierarchy)
        {
            currentTarget = null;
        }

        if (Time.time < nextTargetScanTime)
            return;

        nextTargetScanTime =
            Time.time + Mathf.Max(0.01f, targetScanInterval);

        currentTarget = FindClosestTarget();
    }


    private Transform FindClosestTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRange,
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

            if (sqrDistance < closestSqrDistance)
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


    private void FireAtTarget(Vector3 aimPoint, float distanceToTarget)
    {
        Vector3 shotOrigin = muzzlePoint != null
            ? muzzlePoint.position
            : GunVisual.position;

        Vector3 baseDirection = aimPoint - shotOrigin;

        if (baseDirection.sqrMagnitude < 0.0001f)
            return;

        baseDirection.Normalize();

        Vector3 shotDirection = ApplyAccuracySpread(baseDirection);

        // Always fire the full ray distance.
        Vector3 tracerEnd = shotOrigin + shotDirection * maxShotDistance;

        // If something is hit, stop the tracer at the impact point.
        if (Physics.Raycast(
            shotOrigin,
            shotDirection,
            out RaycastHit hit,
            maxShotDistance,
            shotLayers,
            QueryTriggerInteraction.Ignore))
        {
            tracerEnd = hit.point;

            ApplyDamage(hit);

            if(hit.collider.gameObject.layer == 7)
            {
                GameObject sparks = Instantiate(metalSparkParticles, hit.point, Quaternion.identity);
                Destroy(sparks, 5);
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
            AudioManager.Play3D(
                fireSound,
                shotOrigin,
                fireSoundVolume, 1, 1, 50);
        }

        currentAmmo = Mathf.Max(0, currentAmmo - 1);

        nextShotTime =
            Time.time + (1f / Mathf.Max(0.01f, fireRate));
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


    private void ApplyDamage(RaycastHit hit)
    {
        ITurretDamageable damageable =
            hit.collider.GetComponentInParent<ITurretDamageable>();

        if (damageable != null)
        {
            damageable.TakeDamage(
                damagePerShot,
                hit.point,
                knockbackForce);

            return;
        }

        // Fallback for objects that only support ordinary damage.
        hit.collider.SendMessageUpwards(
            damageMessage,
            damagePerShot,
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
        if (isReloading || currentAmmo >= magazineSize)
            return;

        isReloading = true;

        aimTween?.Kill();
        aimTween = null;
        hasAimTweenRotation = false;

        float totalReloadTime =
            Mathf.Max(0.01f, reloadTime);

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
        Gizmos.color = Color.yellow;

        Gizmos.DrawWireSphere(
            transform.position,
            detectionRange);
    }
}
