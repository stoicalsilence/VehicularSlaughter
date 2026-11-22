using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Optional prefab behavior. Put this on Ranged prefabs, NOT on Regular/Chunky.
/// No Update/FixedUpdate/coroutines: HordeEnemy's existing managed tick drives it.
/// Elite/EMP are still decided separately on HordeEnemy at spawn time.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HordeEnemy))]
public sealed class HordeRangedAttack : MonoBehaviour
{
    [Header("Positioning")]
    [SerializeField, Min(1f)] private float minHoldDistance = 10f;
    [SerializeField, Min(1f)] private float maxHoldDistance = 16f;
    [SerializeField, Min(0f)] private float resumeChaseBuffer = 2.5f;

    [Header("Telegraphed Shot")]
    [Tooltip("Aim locks when the warning begins, allowing the player to dodge.")]
    [SerializeField, Min(0.05f)] private float windupSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float minShotCooldown = 1.8f;
    [SerializeField, Min(0.1f)] private float maxShotCooldown = 2.9f;
    [SerializeField, Min(0f)] private float shotSpreadDegrees = 1.25f;
    [SerializeField, Min(0f)] private float baseProjectileDamage = 8f;
    [SerializeField, Min(0f)] private float targetHeight = 1f;
    [Tooltip("Child located at the muzzle (optional). Otherwise an offset is used.")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Optional glowing child enabled only during windup; normally inactive.")]
    [SerializeField] private GameObject chargeIndicator;
    [SerializeField] private AudioClip shotSound;
    [SerializeField, Range(0f, 1f)] private float shotVolume = 0.6f;

    private HordeEnemy owner;
    private float holdDistance;
    private bool holding;
    private bool charging;
    private float nextActionTime;
    private Vector3 lockedAimPoint;

    internal float HoldDistance => holdDistance;

    internal bool IsChargeVisual(Transform rendererTransform)
    {
        return chargeIndicator != null &&
               rendererTransform.IsChildOf(chargeIndicator.transform);
    }

    internal void ResetForPool()
    {
        holding = false;
        charging = false;
        nextActionTime = 0f;
        lockedAimPoint = Vector3.zero;
        if (chargeIndicator != null && chargeIndicator.activeSelf)
            chargeIndicator.SetActive(false);
    }

    internal void ResetForSpawn(HordeEnemy enemy)
    {
        owner = enemy;
        ResetForPool();
        float low = Mathf.Max(1f, minHoldDistance);
        float high = Mathf.Max(low, maxHoldDistance);
        holdDistance = Random.Range(low, high);
        nextActionTime = Time.time + Random.Range(0.2f, 1f);
    }

    internal void CancelForKnockback()
    {
        charging = false;
        if (chargeIndicator != null && chargeIndicator.activeSelf)
            chargeIndicator.SetActive(false);
        nextActionTime = Time.time + Random.Range(0.5f, 1f);
    }

    /// <returns>True when holding and movement was handled. False means chase normally.</returns>
    internal bool ManagedTick(float dt, Vector3 targetPosition, Vector3 horizontalToPlayer)
    {
        if (owner == null || !isActiveAndEnabled) return false;

        float distanceSqr = horizontalToPlayer.sqrMagnitude;
        float resume = holdDistance + Mathf.Max(0f, resumeChaseBuffer);
        if (holding && distanceSqr > resume * resume)
        {
            holding = false;
            CancelCharge();
        }
        else if (!holding && distanceSqr <= holdDistance * holdDistance)
        {
            holding = true;
            nextActionTime = Mathf.Max(nextActionTime, Time.time + 0.2f);
        }
        if (!holding) return false;

        bool stopped = owner.BrakeAndFaceRanged(dt, horizontalToPlayer);
        if (charging)
        {
            if (Time.time >= nextActionTime)
            {
                Fire();
                CancelCharge();
                nextActionTime = Time.time + Random.Range(
                    Mathf.Max(0.1f, minShotCooldown),
                    Mathf.Max(Mathf.Max(0.1f, minShotCooldown), maxShotCooldown));
            }
            return true;
        }

        if (!stopped || Time.time < nextActionTime) return true;
        charging = true;
        HordeManager manager = HordeManager.Instance;
        lockedAimPoint = manager != null
            ? manager.GetRangedAimPoint(targetPosition, targetHeight)
            : targetPosition + Vector3.up * targetHeight;
        nextActionTime = Time.time + Mathf.Max(0.05f, windupSeconds);
        if (chargeIndicator != null) chargeIndicator.SetActive(true);
        return true;
    }

    private void Fire()
    {
        HordeManager manager = HordeManager.Instance;
        if (manager == null || owner == null || owner.IsDead) return;

        Vector3 origin;
        if (muzzle != null)
        {
            origin = muzzle.position;
        }
        else
        {
            Vector3 forward = lockedAimPoint - owner.transform.position;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : owner.transform.forward;
            origin = owner.transform.position + Vector3.up * 1.15f + forward * 0.7f;
        }

        Vector3 direction = lockedAimPoint - origin;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();
        if (shotSpreadDegrees > 0f)
            direction = Quaternion.AngleAxis(
                Random.Range(-shotSpreadDegrees, shotSpreadDegrees), Vector3.up) * direction;

        float damage = Mathf.Max(0f, baseProjectileDamage) * owner.DamageMultiplier;
        if (manager.FireRangedProjectile(origin, direction, damage, owner) && shotSound != null)
            AudioManager.Play3D(shotSound, origin, shotVolume, 1, 1, 80);
    }

    private void CancelCharge()
    {
        charging = false;
        if (chargeIndicator != null && chargeIndicator.activeSelf)
            chargeIndicator.SetActive(false);
    }
}
