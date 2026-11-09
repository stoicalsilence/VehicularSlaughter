using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1f)]
    private float maxHealth = 100f;

    [Header("Damage")]
    [Tooltip("Minimum time between accepted hits. Prevents several enemies from dealing damage every physics frame.")]
    [SerializeField, Min(0f)]
    private float damageCooldown = 0.75f;

    [Header("Thorns")]
    [SerializeField, Min(0f)]
    private float thornsDamage = 5f;

    [SerializeField]
    private PlayerVehicleInteractor vehicleInteractor;

    private float currentHealth;
    private float nextDamageTime;

    public AudioClip damageSound;
    [SerializeField] private Image damageFlashImage;
    [SerializeField, Min(0.01f)] private float flashFadeDuration = 0.4f;

    private Coroutine flashCoroutine;

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    public float HealthFraction =>
        maxHealth > 0f ? currentHealth / maxHealth : 0f;

    public event Action<float, float> HealthChanged;

    private void Awake()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = maxHealth;

        if (vehicleInteractor == null)
            vehicleInteractor = GetComponent<PlayerVehicleInteractor>();
    }

    /// <summary>
    /// The central entry point for damage directed at the player.
    /// When seated in a vehicle, damage is redirected to that vehicle.
    /// </summary>
    public bool TakeDamage(float damage, HordeEnemy attacker = null)
    {
        if (damage <= 0f)
            return false;

        // While driving, redirect damage to the vehicle.
        if (vehicleInteractor != null && vehicleInteractor.IsInVehicle)
        {
            if (Time.time < nextDamageTime)
                return false;

            bool vehicleDamaged =
                vehicleInteractor.TryDamageCurrentVehicle(damage, attacker);

            if (vehicleDamaged)
                nextDamageTime = Time.time + damageCooldown;

            return vehicleDamaged;
        }

        // On foot: damage the player's own health.
        if (currentHealth <= 0f || Time.time < nextDamageTime)
            return false;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        PlayDamageFeedback();

        nextDamageTime = Time.time + damageCooldown;

        HealthChanged?.Invoke(currentHealth, maxHealth);

        // Thorns: damage the enemy that attacked the player.
        if (attacker != null && !attacker.IsDead && thornsDamage > 0f)
        {
            attacker.ReceiveThornsDamage(thornsDamage);
        }

        return true;
    }

    // CharacterController contact with an enemy.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null)
            return;

        HordeEnemy enemy =
            hit.collider.GetComponentInParent<HordeEnemy>();

        if (enemy == null || enemy.IsDead)
            return;

        TakeDamage(enemy.contactDamage, enemy);
    }

    private IEnumerator FadeDamageFlash()
    {
        Color color = damageFlashImage.color;

        color.a = 0.33f;
        damageFlashImage.color = color;

        float elapsed = 0f;

        while (elapsed < flashFadeDuration)
        {
            elapsed += Time.deltaTime;

            color.a = Mathf.Lerp(0.33f, 0f, elapsed / flashFadeDuration);
            damageFlashImage.color = color;

            yield return null;
        }

        color.a = 0f;
        damageFlashImage.color = color;

        flashCoroutine = null;
    }

    private void PlayDamageFeedback()
    {
        AudioManager.Play2D(damageSound, 1);

        if (damageFlashImage != null)
        {
            if (flashCoroutine != null)
                StopCoroutine(flashCoroutine);

            flashCoroutine = StartCoroutine(FadeDamageFlash());
        }
    }
}