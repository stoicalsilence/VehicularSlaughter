using System;
using UnityEngine;

/// <summary>
/// Runtime upgrade values belonging to one player instance.
/// Put this on the player root. Components on child objects can find it
/// with GetComponentInParent<PlayerStats>().
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerStats : MonoBehaviour
{
    [Header("Survivability")]
    [SerializeField, Min(1f)] public float maxHealth = 100f;
    [SerializeField, Min(0f)] public float damageCooldown = 0.75f;
    [SerializeField, Min(0f)] public float thornsDamage = 5f;
    [SerializeField, Min(0f)] public float hpRegeneration = 0f;

    [Header("Movement")]
    [SerializeField, Min(0f)] public float groundMoveSpeed = 7f;
    [SerializeField, Min(0f)] public float groundAcceleration = 14f;
    [SerializeField, Min(0f)] public float groundDeceleration = 10f;
    [SerializeField, Min(0f)] public float airMoveSpeed = 7f;
    [SerializeField, Min(0f)] public float airAcceleration = 2f;
    [SerializeField, Min(0f)] public float airDeceleration = 2f;
    [SerializeField, Min(0f)] public float strafeMaxSpeed = 1f;
    [SerializeField, Min(0f)] public float strafeAcceleration = 15f;
    [SerializeField, Min(0f)] public float strafeDeceleration = 15f;
    [SerializeField, Min(0f)] public float friction = 6f;
    [SerializeField, Min(0f)] public float gravity = 20f;
    [SerializeField, Min(0f)] public float jumpForce = 8f;
    [SerializeField, Min(0f)] public float airControl = 0.3f;

    [Header("Weapon")]
    [SerializeField, Min(0f)] public float gunDamagePerPellet = 10f;
    [SerializeField, Min(1)] public int gunPelletsPerShot = 1;
    [SerializeField, Min(0f)] public float gunKnockbackForce = 5f;
    [SerializeField, Min(0f)] public float gunFireRate = 6f;
    [SerializeField, Range(0f, 30f)] public float gunAccuracySpreadDegrees = 1.25f;
    [SerializeField, Min(0f)] public float gunCriticalChance = 0f;
    [SerializeField, Min(1f)] public float gunCriticalDamageMultiplier = 2f;
    [SerializeField, Min(1)] public int gunMagazineSize = 30;
    [SerializeField, Min(0.01f)] public float gunReloadDuration = 2.2f;
    [SerializeField, Min(0.1f)] public float gunMaxShotDistance = 60f;
    [SerializeField, Min(0f)] public float pierces = 0f;

    [Header("Misc")]
    [SerializeField, Min(0f)] public float itemLuck = 0f;
    [SerializeField, Min(0f)] public float xpGain = 0f;

    /// <summary>Raised when max health changes. Arguments are old max and new max.</summary>
    public event Action<float, float> MaxHealthChanged;

    public float MaxHealth
    {
        get => maxHealth;
        set
        {
            float oldValue = maxHealth;
            maxHealth = Mathf.Max(1f, value);
            if (!Mathf.Approximately(oldValue, maxHealth))
                MaxHealthChanged?.Invoke(oldValue, maxHealth);
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
        maxHealth = Mathf.Max(1f, maxHealth);
        damageCooldown = Mathf.Max(0f, damageCooldown);
        thornsDamage = Mathf.Max(0f, thornsDamage);
        hpRegeneration = Mathf.Max(0f, hpRegeneration);

        groundMoveSpeed = Mathf.Max(0f, groundMoveSpeed);
        groundAcceleration = Mathf.Max(0f, groundAcceleration);
        groundDeceleration = Mathf.Max(0f, groundDeceleration);
        airMoveSpeed = Mathf.Max(0f, airMoveSpeed);
        airAcceleration = Mathf.Max(0f, airAcceleration);
        airDeceleration = Mathf.Max(0f, airDeceleration);
        strafeMaxSpeed = Mathf.Max(0f, strafeMaxSpeed);
        strafeAcceleration = Mathf.Max(0f, strafeAcceleration);
        friction = Mathf.Max(0f, friction);
        gravity = Mathf.Max(0f, gravity);
        jumpForce = Mathf.Max(0f, jumpForce);
        airControl = Mathf.Max(0f, airControl);

        gunDamagePerPellet = Mathf.Max(0f, gunDamagePerPellet);
        gunPelletsPerShot = Mathf.Max(1, gunPelletsPerShot);
        gunKnockbackForce = Mathf.Max(0f, gunKnockbackForce);
        gunFireRate = Mathf.Max(0.01f, gunFireRate);
        gunAccuracySpreadDegrees = Mathf.Clamp(gunAccuracySpreadDegrees, 0f, 30f);
        gunCriticalChance = Mathf.Max(0f, gunCriticalChance);
        gunCriticalDamageMultiplier = Mathf.Max(1f, gunCriticalDamageMultiplier);
        gunMagazineSize = Mathf.Max(1, gunMagazineSize);
        gunReloadDuration = Mathf.Max(0.01f, gunReloadDuration);
        gunMaxShotDistance = Mathf.Max(0.1f, gunMaxShotDistance);

        itemLuck = Mathf.Max(0f, itemLuck);
        xpGain = Mathf.Max(1f, xpGain);
    }
}
