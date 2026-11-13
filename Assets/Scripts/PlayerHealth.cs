using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerHealth : MonoBehaviour
{
    public PlayerStats playerStats;

    private float maxHealth => playerStats.maxHealth;

    private float damageCooldown => playerStats.damageCooldown;

    public float thornsDamage => playerStats.thornsDamage;

    [SerializeField]
    private PlayerVehicleInteractor vehicleInteractor;

    private float currentHealth;

    public PlayerHealthMeter playerHealthMeter;

    // Enemy attacks use independent cooldowns. Damage without an identified
    // enemy (for example, environmental damage) uses the fallback cooldown.
    private float nextDamageTime;
    private bool wasInVehicle;

    private sealed class EnemyDamageState
    {
        public float nextDamageTime;
        public float lastContactTime;
    }

    private readonly Dictionary<HordeEnemy, EnemyDamageState> enemyDamageStates =
        new Dictionary<HordeEnemy, EnemyDamageState>();

    // Reused list avoids allocating while removing expired enemy states.
    private readonly List<HordeEnemy> staleEnemyStates =
        new List<HordeEnemy>();

    public AudioClip damageSound;
    [SerializeField] private Image damageFlashImage;
    [SerializeField, Min(0.01f)] private float flashFadeDuration = 0.4f;

    private Coroutine flashCoroutine;

    [Header("Enemy Contact Detection")]
    [SerializeField]
    private LayerMask enemyDetectionMask = ~0;

    [SerializeField, Min(0f)]
    [Tooltip("Small extra detection radius to account for CharacterController skin width.")]
    private float enemyContactProbeDistance = 0.05f;

    [SerializeField, Min(0.05f)]
    [Tooltip("How long an enemy's cooldown is retained after contact temporarily disappears from both contact checks. Helps prevent duplicate hits while moving against enemies.")]
    private float enemyContactGracePeriod = 0.2f;

    private CharacterController characterController;
    private readonly Collider[] enemyOverlapBuffer = new Collider[64];

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    public float HealthFraction =>
        maxHealth > 0f ? currentHealth / maxHealth : 0f;

    public event Action<float, float> HealthChanged;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogError("PlayerGun: PlayerStats not found!", this);
            enabled = false;
            return;
        }

        playerStats.maxHealth = Mathf.Max(1f, maxHealth);
        currentHealth = maxHealth;

        characterController = GetComponent<CharacterController>();

        if (vehicleInteractor == null)
            vehicleInteractor = GetComponent<PlayerVehicleInteractor>();
    }

    private void LateUpdate()
    {
        DetectEnemiesTouchingPlayer();
    }

    private void Update()
    {
        if (playerStats.hpRegeneration > 0)
        {
            currentHealth += playerStats.hpRegeneration / 600;
            if (currentHealth > MaxHealth) currentHealth = MaxHealth;
        }
    }

    private void DetectEnemiesTouchingPlayer()
    {
        if (characterController == null || currentHealth <= 0f)
            return;

        // When inside a vehicle, the car handles enemy contact damage.
        // Clear player contact cooldowns only when entering/exiting the vehicle.
        bool isInVehicle = vehicleInteractor != null && vehicleInteractor.IsInVehicle;

        if (isInVehicle)
        {
            if (!wasInVehicle)
                enemyDamageStates.Clear();

            wasInVehicle = true;
            return;
        }

        if (wasInVehicle)
        {
            enemyDamageStates.Clear();
            wasInVehicle = false;
        }

        // Calculate the CharacterController's world-space capsule.
        Vector3 scale = transform.lossyScale;

        float radius = characterController.radius *
                       Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

        float height = characterController.height * Mathf.Abs(scale.y);

        radius += Mathf.Max(0f, enemyContactProbeDistance);
        height = Mathf.Max(height, radius * 2f);

        Vector3 center = transform.TransformPoint(characterController.center);
        float halfSegment = height * 0.5f - radius;

        Vector3 point1 = center + transform.up * halfSegment;
        Vector3 point2 = center - transform.up * halfSegment;

        // Find enemies overlapping or very close to the player capsule.
        int count = Physics.OverlapCapsuleNonAlloc(
            point1,
            point2,
            radius,
            enemyOverlapBuffer,
            enemyDetectionMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider other = enemyOverlapBuffer[i];

            if (other == null)
                continue;

            // Ignore the player's own colliders.
            if (other.transform == transform ||
                other.transform.IsChildOf(transform))
            {
                continue;
            }

            HordeEnemy enemy = other.GetComponentInParent<HordeEnemy>();

            if (enemy == null || enemy.IsDead)
                continue;

            // The per-enemy state deduplicates multiple colliders and also
            // coordinates this overlap scan with OnControllerColliderHit.
            HandleEnemyContact(enemy);
        }

        CleanupExpiredEnemyStates();
    }

    /// <summary>
    /// Records contact from either the overlap scan or CharacterController callback,
    /// then attempts damage through the same per-enemy cooldown.
    /// </summary>
    private void HandleEnemyContact(HordeEnemy enemy)
    {
        if (enemy == null || enemy.IsDead)
            return;

        float now = Time.time;

        float gracePeriod = Mathf.Max(0.05f, enemyContactGracePeriod);

        if (!enemyDamageStates.TryGetValue(enemy, out EnemyDamageState state))
        {
            state = new EnemyDamageState();
            enemyDamageStates.Add(enemy, state);
        }
        else if (now - state.lastContactTime > gracePeriod)
        {
            // This is a genuinely new contact after the grace period. Allow
            // an immediate hit instead of inheriting the previous contact's cooldown.
            state.nextDamageTime = 0f;
        }

        // Always refresh contact time, even when this enemy is on cooldown.
        // This prevents the cleanup pass from deleting a live attacker's timer.
        state.lastContactTime = now;

        TakeDamage(enemy.contactDamage, enemy);
    }

    private void CleanupExpiredEnemyStates()
    {
        float now = Time.time;
        float gracePeriod = Mathf.Max(0.05f, enemyContactGracePeriod);

        staleEnemyStates.Clear();

        foreach (KeyValuePair<HordeEnemy, EnemyDamageState> entry in enemyDamageStates)
        {
            HordeEnemy enemy = entry.Key;
            EnemyDamageState state = entry.Value;

            if (enemy == null ||
                enemy.IsDead ||
                now - state.lastContactTime > gracePeriod)
            {
                staleEnemyStates.Add(enemy);
            }
        }

        for (int i = 0; i < staleEnemyStates.Count; i++)
            enemyDamageStates.Remove(staleEnemyStates[i]);
    }

    /// <summary>
    /// The central entry point for damage directed at the player.
    /// When seated in a vehicle, damage is redirected to that vehicle.
    /// Each identified enemy has an independent damage cooldown.
    /// </summary>
    public bool TakeDamage(float damage, HordeEnemy attacker = null)
    {
        if (damage <= 0f)
            return false;

        if (IsDamageOnCooldown(attacker))
            return false;

        // While driving, redirect damage to the vehicle.
        if (vehicleInteractor != null && vehicleInteractor.IsInVehicle)
        {
            bool vehicleDamaged =
                vehicleInteractor.TryDamageCurrentVehicle(damage, attacker);

            if (vehicleDamaged)
                StartDamageCooldown(attacker);

            return vehicleDamaged;
        }

        // On foot: damage the player's own health.
        if (currentHealth <= 0f)
            return false;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        CancelInvoke(nameof(HideLastDamageText));
        int damageInt = (int)damage;
        playerHealthMeter.lastDamageTakenText.text = "-" + damageInt.ToString();
        Invoke(nameof(HideLastDamageText), 5);

        PlayDamageFeedback();

        StartDamageCooldown(attacker);

        HealthChanged?.Invoke(currentHealth, maxHealth);

        // Thorns: damage the enemy that attacked the player.
        if (attacker != null && !attacker.IsDead && thornsDamage > 0f)
        {
            attacker.ReceiveThornsDamage(thornsDamage);
        }

        return true;
    }

    private void HideLastDamageText()
    {
        playerHealthMeter.lastDamageTakenText.text = "";
    }

    private bool IsDamageOnCooldown(HordeEnemy attacker)
    {
        if (attacker == null)
            return Time.time < nextDamageTime;

        return enemyDamageStates.TryGetValue(attacker, out EnemyDamageState state) &&
               Time.time < state.nextDamageTime;
    }

    private void StartDamageCooldown(HordeEnemy attacker)
    {
        float nextTime = Time.time + Mathf.Max(0f, damageCooldown);

        if (attacker == null)
        {
            nextDamageTime = nextTime;
            return;
        }

        if (!enemyDamageStates.TryGetValue(attacker, out EnemyDamageState state))
        {
            state = new EnemyDamageState();
            enemyDamageStates.Add(attacker, state);
        }

        state.nextDamageTime = nextTime;
        // Keep the state alive from the accepted hit, even if the overlap scan
        // misses the collider for a frame immediately after the collision callback.
        state.lastContactTime = Time.time;
    }

    // CharacterController contact with an enemy.
    // This can run in addition to the overlap scan. Both routes refresh the
    // same contact state and use the same per-enemy cooldown.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == null)
            return;

        HordeEnemy enemy =
            hit.collider.GetComponentInParent<HordeEnemy>();

        if (enemy == null || enemy.IsDead)
            return;

        HandleEnemyContact(enemy);
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

    private void OnDisable()
    {
        playerStats.MaxHealthChanged -= OnMaxHealthChanged;
    }

    private void OnMaxHealthChanged(float oldMax, float newMax)
    {
        if (currentHealth > 0f)
        {
            float increase = newMax - oldMax;
            currentHealth = Mathf.Clamp(
                currentHealth + increase,
                0f,
                newMax
            );
        }

        HealthChanged?.Invoke(currentHealth, newMax);
    }
}
