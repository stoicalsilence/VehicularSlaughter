using System;
using UnityEngine;

/// <summary>
/// Runtime upgrade values belonging to one vehicle instance.
/// Put this on the car root. Child systems such as TurretGun can find it
/// with GetComponentInParent<CarStats>().
/// </summary>
[DisallowMultipleComponent]
public sealed class CarStats : MonoBehaviour
{
    [Header("Driving")]
    [SerializeField, Min(0)] public int maxSpeed = 130;
    [SerializeField, Min(0)] public int maxReverseSpeed = 65;
    [SerializeField, Min(0)] public int accelerationMultiplier = 10;
    [SerializeField, Min(0)] public int maxSteeringAngle = 45;
    [SerializeField, Min(0.01f)] public float steeringSpeed = 1f;
    [SerializeField, Min(0)] public int brakeForce = 390;
    [SerializeField, Min(0)] public int decelerationMultiplier = 1;
    [SerializeField, Min(1)] public int handbrakeDriftMultiplier = 10;

    [Header("Durability and Enemy Impacts")]
    [SerializeField, Min(1f)] public float maxVehicleHealth = 100f;
    [SerializeField, Min(0f)] public float minimumRunOverSpeedKmh = 18f;
    [SerializeField, Min(1f)] public float maximumImpactSpeedKmh = 70f;
    [SerializeField, Min(0f)] public float minimumEnemyDamage = 10f;
    [SerializeField, Min(0f)] public float maximumEnemyDamage = 36f;
    [SerializeField, Min(0f)] public float minimumEnemyKnockback = 2f;
    [SerializeField, Min(0f)] public float maximumEnemyKnockback = 9f;
    [SerializeField, Min(0f)] public float minimumVehicleDamagePerHit = 1f;
    [SerializeField, Min(0f)] public float maximumVehicleDamagePerHit = 10f;
    [SerializeField, Min(0.05f)] public float enemyContactDamageCooldown = 1f;
    [SerializeField, Min(0f)] public float vehicleThornsDamage = 5f;
    [SerializeField, Min(0f)] public float carExplosionDamage = 100f;
    [SerializeField, Min(0f)] public float carExplosionDamageRange = 15f;

    [Header("Mounted Turret")]
    [SerializeField, Min(0f)] public float turretDamagePerShot = 10f;
    [SerializeField, Min(0f)] public float turretKnockbackForce = 5f;
    [SerializeField, Min(0.01f)] public float turretFireRate = 6f;
    [SerializeField, Range(0f, 20f)] public float turretAccuracySpreadDegrees = 1.25f;
    [SerializeField, Min(0f)] public float turretCriticalChance = 0f;
    [SerializeField, Min(1f)] public float turretCriticalDamageMultiplier = 2f;
    [SerializeField, Min(1)] public int turretMagazineSize = 30;
    [SerializeField, Min(0.01f)] public float turretReloadTime = 2.2f;
    [SerializeField, Min(0f)] public float turretDetectionRange = 20f;
    [SerializeField, Min(0.1f)] public float turretMaxShotDistance = 40f;

    /// <summary>Raised when max vehicle health changes. Arguments are old max and new max.</summary>
    public event Action<float, float> MaxVehicleHealthChanged;

    public float MaxVehicleHealth
    {
        get => maxVehicleHealth;
        set
        {
            float oldValue = maxVehicleHealth;
            maxVehicleHealth = Mathf.Max(1f, value);
            if (!Mathf.Approximately(oldValue, maxVehicleHealth))
                MaxVehicleHealthChanged?.Invoke(oldValue, maxVehicleHealth);
        }
    }

    private void Awake()
    {
        ClampSerializedValues();
    }

    private void OnValidate()
    {
        ClampSerializedValues();
    }

    private void ClampSerializedValues()
    {
        maxSpeed = Mathf.Max(0, maxSpeed);
        maxReverseSpeed = Mathf.Max(0, maxReverseSpeed);
        accelerationMultiplier = Mathf.Max(0, accelerationMultiplier);
        maxSteeringAngle = Mathf.Max(0, maxSteeringAngle);
        steeringSpeed = Mathf.Max(0.01f, steeringSpeed);
        brakeForce = Mathf.Max(0, brakeForce);
        decelerationMultiplier = Mathf.Max(0, decelerationMultiplier);
        handbrakeDriftMultiplier = Mathf.Max(1, handbrakeDriftMultiplier);

        maxVehicleHealth = Mathf.Max(1f, maxVehicleHealth);
        minimumRunOverSpeedKmh = Mathf.Max(0f, minimumRunOverSpeedKmh);
        maximumImpactSpeedKmh = Mathf.Max(1f, maximumImpactSpeedKmh);
        minimumEnemyDamage = Mathf.Max(0f, minimumEnemyDamage);
        maximumEnemyDamage = Mathf.Max(0f, maximumEnemyDamage);
        minimumEnemyKnockback = Mathf.Max(0f, minimumEnemyKnockback);
        maximumEnemyKnockback = Mathf.Max(0f, maximumEnemyKnockback);
        minimumVehicleDamagePerHit = Mathf.Max(0f, minimumVehicleDamagePerHit);
        maximumVehicleDamagePerHit = Mathf.Max(0f, maximumVehicleDamagePerHit);
        enemyContactDamageCooldown = Mathf.Max(0.05f, enemyContactDamageCooldown);
        vehicleThornsDamage = Mathf.Max(0f, vehicleThornsDamage);

        turretDamagePerShot = Mathf.Max(0f, turretDamagePerShot);
        turretKnockbackForce = Mathf.Max(0f, turretKnockbackForce);
        turretFireRate = Mathf.Max(0.01f, turretFireRate);
        turretAccuracySpreadDegrees = Mathf.Clamp(turretAccuracySpreadDegrees, 0f, 20f);
        turretCriticalChance = Mathf.Max(0f, turretCriticalChance);
        turretCriticalDamageMultiplier = Mathf.Max(1f, turretCriticalDamageMultiplier);
        turretMagazineSize = Mathf.Max(1, turretMagazineSize);
        turretReloadTime = Mathf.Max(0.01f, turretReloadTime);
        turretDetectionRange = Mathf.Max(0f, turretDetectionRange);
        turretMaxShotDistance = Mathf.Max(0.1f, turretMaxShotDistance);
    }
}
