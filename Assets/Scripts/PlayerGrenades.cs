using System;
using TMPro;
using UnityEngine;

/// <summary>
/// On-foot G-key grenades with a sequential, regenerating charge bank.
/// Add to the player root alongside PlayerStats / PlayerVehicleInteractor.
/// No changes to PlayerGun, PlayerRadio, or HordeEnemy are required.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGrenades : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;
    [SerializeField] private PlayerGun playerGun;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Camera playerCamera;
    [Tooltip("Optional spawn transform on the first-person rig. Leave blank for a camera-relative release point.")]
    [SerializeField] private Transform throwOrigin;
    [Tooltip("Optional. Without a prefab, a simple sphere is created for testing.")]
    [SerializeField] private GameObject grenadePrefab;
    [Tooltip("Reuse the PlayerRadio airstrikeExplosion prefab. Auto-detected from PlayerRadio when blank.")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("Throw Feel")]
    [SerializeField] private KeyCode throwKey = KeyCode.G;
    [Tooltip("Upward bias applied to the camera look direction for a reliable lob.")]
    [SerializeField, Range(0f, 1.5f)] private float upwardLob = 0.38f;
    [Tooltip("How much of the player's current movement velocity is inherited.")]
    [SerializeField, Range(0f, 1f)] private float inheritPlayerVelocity = 0.35f;
    [SerializeField, Min(0f)] private float minimumTimeBetweenThrows = 0.35f;
    [SerializeField, Min(0f)] private float grenadeSpin = 8f;

    [Header("Explosion Visuals")]
    [Tooltip("Scales the explosion prefab, independently from PlayerStats.grenadeExplosionRadius.")]
    [SerializeField, Min(0.01f)] private float explosionVisualScale = 1f;
    [SerializeField, Min(0.1f)] private float explosionVisualLifetime = 3f;

    [Header("Feedback (Optional)")]
    [SerializeField] private AudioClip throwSound;
    [SerializeField] private AudioClip emptySound;
    [SerializeField] private AudioClip rechargeSound;
    [SerializeField] private AudioClip bounceSound;
    [Tooltip("Optional TMP text. Example: G  1/2  (+1 in 24s).")]
    [SerializeField] private TMP_Text grenadeHudText;

    public int CurrentGrenades { get; private set; }
    public int MaxGrenades => playerStats == null ? 0 : Mathf.Max(0, playerStats.grenadeCapacity);
    public float NextGrenadeInSeconds => CurrentGrenades >= MaxGrenades || MaxGrenades == 0
        ? 0f
        : Mathf.Max(0f, playerStats.grenadeRechargeSeconds - rechargeElapsed);
    public float RechargeProgress01 => CurrentGrenades >= MaxGrenades || MaxGrenades == 0
        ? 1f
        : Mathf.Clamp01(rechargeElapsed / Mathf.Max(0.01f, playerStats.grenadeRechargeSeconds));

    /// <summary>Fires whenever the stored count or maximum changes (current, maximum).</summary>
    public event Action<int, int> GrenadesChanged;

    private float rechargeElapsed;
    private float nextAllowedThrowTime;
    private int knownCapacity;
    private CharacterController characterController;
    private PlayerRadio playerRadio;
    private int displayedCount = -1;
    private int displayedMax = -1;
    private int displayedRemaining = -1;

    private static PhysicMaterial fallbackBounceMaterial;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();
        if (vehicleInteractor == null)
            vehicleInteractor = GetComponentInParent<PlayerVehicleInteractor>();
        if (playerGun == null)
            playerGun = GetComponentInParent<PlayerGun>();
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        characterController = GetComponentInParent<CharacterController>();
        playerRadio = GetComponentInParent<PlayerRadio>();

        if (playerStats == null || playerCamera == null)
        {
            Debug.LogError("PlayerGrenades needs PlayerStats and the first-person player Camera.", this);
            enabled = false;
            return;
        }

        knownCapacity = MaxGrenades;
        CurrentGrenades = knownCapacity; // Start fully stocked.
        rechargeElapsed = 0f;
        RefreshHud();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || playerStats == null)
            return;

        // Recharge runs even in a vehicle or while the radio is equipped.
        UpdateRecharge(Time.deltaTime);

        if (Input.GetKeyDown(throwKey))
            TryThrow();

        RefreshHud();
    }

    private void UpdateRecharge(float deltaTime)
    {
        int capacity = MaxGrenades;

        // Upgrades can alter capacity during the run, without creating free ammo.
        if (capacity != knownCapacity)
        {
            knownCapacity = capacity;
            CurrentGrenades = Mathf.Min(CurrentGrenades, capacity);
            GrenadesChanged?.Invoke(CurrentGrenades, capacity);
        }

        if (CurrentGrenades >= capacity)
        {
            rechargeElapsed = 0f;
            return;
        }

        rechargeElapsed += deltaTime;
        float interval = Mathf.Max(0.01f, playerStats.grenadeRechargeSeconds);
        int earned = Mathf.FloorToInt(rechargeElapsed / interval);
        if (earned <= 0)
            return;

        int restored = Mathf.Min(earned, capacity - CurrentGrenades);
        CurrentGrenades += restored;
        rechargeElapsed -= restored * interval;
        if (CurrentGrenades >= capacity)
            rechargeElapsed = 0f;

        GrenadesChanged?.Invoke(CurrentGrenades, capacity);
        if (rechargeSound != null)
            AudioManager.Play2D(rechargeSound, 0.45f);
    }

    public bool TryThrow()
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f || !CanThrow())
            return false;

        if (CurrentGrenades <= 0)
        {
            if (emptySound != null)
                AudioManager.Play2D(emptySound, 0.65f);
            return false;
        }

        if (Time.time < nextAllowedThrowTime)
            return false;

        GameObject blastFx = explosionPrefab != null
            ? explosionPrefab
            : (playerRadio != null ? playerRadio.airstrikeExplosion : null);

        if (blastFx == null)
        {
            // Don't spend ammo on a grenade that cannot use the existing explosion setup.
            Debug.LogWarning("PlayerGrenades: Assign Explosion Prefab (the same airstrikeExplosion used by PlayerRadio).", this);
            return false;
        }

        Transform view = playerCamera.transform;
        Vector3 direction = (view.forward + Vector3.up * upwardLob).normalized;
        Vector3 origin = FindSafeThrowPosition(view);

        GameObject grenade = grenadePrefab != null
            ? Instantiate(grenadePrefab, origin, Quaternion.LookRotation(direction))
            : CreateTestGrenade(origin, direction);

        if (grenade == null)
            return false;

        if (!grenade.activeSelf)
            grenade.SetActive(true);

        Rigidbody body = grenade.GetComponent<Rigidbody>();
        if (body == null)
            body = grenade.AddComponent<Rigidbody>();

        Collider[] grenadeColliders = grenade.GetComponentsInChildren<Collider>(true);
        if (grenadeColliders.Length == 0)
        {
            // A missing collider would make the grenade fall through the world.
            SphereCollider fallback = grenade.AddComponent<SphereCollider>();
            fallback.radius = 0.13f;
            grenadeColliders = new Collider[] { fallback };
        }

        // The grenade must never collide with its thrower's CharacterController/rig.
        Collider[] ownerColliders = playerStats.GetComponentsInChildren<Collider>(true);
        foreach (Collider projectileCollider in grenadeColliders)
        {
            if (projectileCollider == null)
                continue;

            foreach (Collider ownerCollider in ownerColliders)
            {
                if (ownerCollider != null)
                    Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
            }
        }

        GrenadeProjectile projectile = grenade.GetComponent<GrenadeProjectile>();
        if (projectile == null)
            projectile = grenade.AddComponent<GrenadeProjectile>();

        // Snapshot upgrades on release. Mid-flight stat changes affect future throws.
        projectile.Arm(
            blastFx,
            playerStats.grenadeDamage,
            playerStats.grenadeExplosionRadius,
            playerStats.grenadeKnockbackForce,
            explosionVisualScale,
            explosionVisualLifetime,
            playerStats.grenadeFuseSeconds,
            bounceSound);

        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Vector3 inheritedVelocity = characterController != null
            ? characterController.velocity * inheritPlayerVelocity
            : Vector3.zero;
        body.velocity = direction * playerStats.grenadeThrowSpeed + inheritedVelocity;
        body.angularVelocity = UnityEngine.Random.onUnitSphere * grenadeSpin;
        body.WakeUp();

        CurrentGrenades--;
        nextAllowedThrowTime = Time.time + minimumTimeBetweenThrows;
        GrenadesChanged?.Invoke(CurrentGrenades, MaxGrenades);
        RefreshHud();

        if (throwSound != null)
            AudioManager.Play2D(throwSound, 0.8f);

        return true;
    }

    private bool CanThrow()
    {
        if (vehicleInteractor != null && vehicleInteractor.IsInVehicle)
            return false;
        if (playerCamera == null || !playerCamera.isActiveAndEnabled)
            return false;
        if (playerHealth != null && playerHealth.CurrentHealth <= 0f)
            return false;
        // PlayerRadio disables PlayerGun when opened. Don't let G interrupt the radio.
        return true;
    }

    private Vector3 FindSafeThrowPosition(Transform view)
    {
        Vector3 cameraPosition = view.position;
        Vector3 desiredPosition = throwOrigin != null
            ? throwOrigin.position
            : cameraPosition + view.forward * 0.65f + view.right * 0.2f - view.up * 0.18f;

        Vector3 offset = desiredPosition - cameraPosition;
        float distance = offset.magnitude;
        if (distance <= 0.01f)
            return desiredPosition;

        // Do not pop grenades through a wall when standing close to it.
        Vector3 rayDirection = offset / distance;
        RaycastHit[] hits = Physics.SphereCastAll(cameraPosition, 0.13f,
            rayDirection, distance, ~0, QueryTriggerInteraction.Ignore);

        float safeDistance = distance;
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0f, hit.distance - 0.05f));
        }

        return cameraPosition + rayDirection * safeDistance;
    }

    private static GameObject CreateTestGrenade(Vector3 origin, Vector3 direction)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Grenade (temporary sphere - assign a prefab for production)";
        sphere.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        sphere.transform.localScale = Vector3.one * 0.26f;

        SphereCollider collider = sphere.GetComponent<SphereCollider>();
        if (fallbackBounceMaterial == null)
        {
            fallbackBounceMaterial = new PhysicMaterial("Grenade bounce")
            {
                bounciness = 0.3f,
                dynamicFriction = 0.5f,
                staticFriction = 0.5f,
                bounceCombine = PhysicMaterialCombine.Maximum
            };
        }
        collider.sharedMaterial = fallbackBounceMaterial;
        return sphere;
    }


    private void RefreshHud()
    {
        if (grenadeHudText == null || playerStats == null)
            return;

        int remaining = CurrentGrenades < MaxGrenades
            ? Mathf.CeilToInt(NextGrenadeInSeconds)
            : 0;

        if (displayedCount == CurrentGrenades &&
            displayedMax == MaxGrenades &&
            displayedRemaining == remaining)
            return;

        displayedCount = CurrentGrenades;
        displayedMax = MaxGrenades;
        displayedRemaining = remaining;

        // Green: fully stocked
        if (CurrentGrenades >= MaxGrenades && MaxGrenades > 0)
        {
            grenadeHudText.text =
                $"<color=#46E59A>G  {CurrentGrenades}/{MaxGrenades}</color>" +
                "  <color=#46E59A></color>";
        }
        // Red: no grenades available
        else if (CurrentGrenades <= 0)
        {
            grenadeHudText.text =
                $"<color=#FF626B>G  {CurrentGrenades}/{MaxGrenades}</color>" +
                $"  <color=#FF626B>(+1 in {remaining}s)</color>";
        }
        // Yellow: partially stocked
        else
        {
            grenadeHudText.text =
                $"<color=#FFC247>G  {CurrentGrenades}/{MaxGrenades}</color>" +
                $"  <color=#AAB4C3>(+1 in {remaining}s)</color>";
        }
    }


    private void OnValidate()
    {
        minimumTimeBetweenThrows = Mathf.Max(0f, minimumTimeBetweenThrows);
        grenadeSpin = Mathf.Max(0f, grenadeSpin);
        explosionVisualScale = Mathf.Max(0.01f, explosionVisualScale);
        explosionVisualLifetime = Mathf.Max(0.1f, explosionVisualLifetime);
    }
}
