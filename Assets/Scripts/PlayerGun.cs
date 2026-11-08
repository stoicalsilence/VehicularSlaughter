using DG.Tweening;
using UnityEngine;

/// <summary>
/// First-person hitscan weapon for the on-foot player.
/// Weapon behaviour is defined by stats rather than weapon-specific classes:
/// pellets + spread can create a shotgun, high fire rate can create an SMG, etc.
/// Requires DOTween and the project's ITurretDamageable / AudioManager systems.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerGun : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player's first-person camera. Auto-found in children when left empty.")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("Used to prevent firing/reloading while the player occupies a vehicle.")]
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;

    [Tooltip("Optional muzzle position. If empty, shots originate from the camera.")]
    [SerializeField] private Transform muzzlePoint;

    [Tooltip("Optional weapon model transform animated during reloads.")]
    [SerializeField] private Transform weaponVisual;

    [SerializeField] private GameObject muzzleFlash;

    [Header("Offensive Stats")]
    [Min(0f)]
    [SerializeField] private float damagePerPellet = 10f;

    [Tooltip("Number of independent hitscan rays fired for each trigger pull. Set above 1 for a shotgun-style weapon.")]
    [Min(1)]
    [SerializeField] private int pelletsPerShot = 1;

    [Min(0f)]
    [SerializeField] private float knockbackForce = 5f;

    [Tooltip("Shots fired per second. For a multi-pellet shot, this is still one trigger shot.")]
    [Min(0.01f)]
    [SerializeField] private float fireRate = 6f;

    [Tooltip("Maximum angular spread, in degrees, applied independently to each pellet.")]
    [Range(0f, 30f)]
    [SerializeField] private float accuracySpreadDegrees = 1.25f;

    [Tooltip("When enabled, holding the fire button repeats shots. Disable for semi-automatic weapons.")]
    [SerializeField] private bool automaticFire = true;

    [Header("Magazine Stats")]
    [Min(1)]
    [SerializeField] private int magazineSize = 30;

    [Min(0.01f)]
    [SerializeField] private float reloadDuration = 2.2f;

    [SerializeField] private bool reloadAutomaticallyWhenEmpty = true;

    [Header("Range Stats")]
    [Min(0.1f)]
    [SerializeField] private float maxShotDistance = 60f;

    [Header("Input")]
    [Range(0, 2)]
    [SerializeField] private int fireMouseButton = 0;
    [SerializeField] private KeyCode reloadKey = KeyCode.R;

    [Header("Hit Detection")]
    [Tooltip("Layers that can be hit by shots. Exclude the player and weapon layers to prevent self-hits.")]
    [SerializeField] private LayerMask shotLayers = ~0;

    [Tooltip("A hit on an ITurretDamageable object uses metal impact particles, as do objects on these layers.")]
    [SerializeField] private LayerMask metalImpactLayers = 1 << 7;

    [SerializeField] private string damageMessage = "TakeDamage";

    [Header("Impact Effects")]
    [SerializeField] private GameObject metalImpactParticles;
    [SerializeField] private GameObject dirtImpactParticles;

    [Min(0.1f)]
    [SerializeField] private float impactParticleLifetime = 5f;

    [SerializeField] private AudioClip dirtImpactSound;

    [Range(0f, 1f)]
    [SerializeField] private float impactSoundVolume = 0.8f;

    [Header("Reload Animation")]
    [Tooltip("How long each transition into and out of the reload pose takes. The remaining reload time is spent holding the pose. The complete animation always matches Reload Duration.")]
    [Min(0.01f)]
    [SerializeField] private float reloadPoseTransitionTime = 0.2f;

    [SerializeField] private Vector3 reloadLocalPosition = new Vector3(0f, -0.12f, -0.08f);
    [SerializeField] private Vector3 reloadLocalEulerAngles = new Vector3(25f, 0f, 0f);
    [SerializeField] private Ease reloadPoseEase = Ease.InOutSine;

    [Header("Muzzle Flash")]
    [Min(0.01f)]
    [SerializeField] private float muzzleFlashDuration = 0.06f;

    [SerializeField] private Vector2 muzzleFlashRandomX = new Vector2(0f, 360f);

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

    [SerializeField] private AudioClip reloadSound;

    [Range(0f, 1f)]
    [SerializeField] private float reloadSoundVolume = 0.8f;

    // Runtime state.
    private int currentAmmo;
    private bool isReloading;
    private float nextShotTime;
    private float muzzleFlashOffTime;

    private Vector3 weaponDefaultLocalPosition;
    private Vector3 weaponDefaultLocalEulerAngles;
    private bool hasWeaponDefaultPose;

    private Sequence reloadSequence;

    public int CurrentAmmo => currentAmmo;
    public bool IsReloading => isReloading;
    public bool CanFire => CanUseWeapon && !isReloading && currentAmmo > 0;

    // Read/write stat properties let a future upgrade system change the weapon at runtime.
    // The same values remain editable in the Inspector.
    public float DamagePerPellet
    {
        get => damagePerPellet;
        set => damagePerPellet = Mathf.Max(0f, value);
    }

    public int PelletsPerShot
    {
        get => pelletsPerShot;
        set => pelletsPerShot = Mathf.Max(1, value);
    }

    public float KnockbackForce
    {
        get => knockbackForce;
        set => knockbackForce = Mathf.Max(0f, value);
    }

    public float FireRate
    {
        get => fireRate;
        set => fireRate = Mathf.Max(0.01f, value);
    }

    public float AccuracySpreadDegrees
    {
        get => accuracySpreadDegrees;
        set => accuracySpreadDegrees = Mathf.Clamp(value, 0f, 30f);
    }

    public int MagazineSize
    {
        get => magazineSize;
        set
        {
            magazineSize = Mathf.Max(1, value);
            currentAmmo = Mathf.Min(currentAmmo, magazineSize);
        }
    }

    public float ReloadDuration
    {
        get => reloadDuration;
        set => reloadDuration = Mathf.Max(0.01f, value);
    }

    public float MaxShotDistance
    {
        get => maxShotDistance;
        set => maxShotDistance = Mathf.Max(0.1f, value);
    }

    private bool CanUseWeapon
    {
        get
        {
            if (vehicleInteractor != null && vehicleInteractor.IsInVehicle)
                return false;

            // PlayerVehicleInteractor disables this camera while the player is in the car.
            return playerCamera != null && playerCamera.enabled;
        }
    }

    private void Awake()
    {
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (vehicleInteractor == null)
            vehicleInteractor = GetComponent<PlayerVehicleInteractor>();

        currentAmmo = Mathf.Max(1, magazineSize);

        if (weaponVisual != null)
        {
            weaponDefaultLocalPosition = weaponVisual.localPosition;
            weaponDefaultLocalEulerAngles = weaponVisual.localEulerAngles;
            hasWeaponDefaultPose = true;
        }

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);

        if (playerCamera == null)
            Debug.LogError("PlayerGun: Assign the player's first-person camera.", this);
    }

    private void Update()
    {
        UpdateMuzzleFlash();

        // This component can remain enabled while driving; the interactor/camera gate
        // ensures the on-foot weapon cannot be used in the vehicle.
        if (!CanUseWeapon)
            return;

        if (Input.GetKeyDown(reloadKey))
            BeginReload();

        if (isReloading)
            return;

        if (currentAmmo <= 0)
        {
            if (reloadAutomaticallyWhenEmpty)
                BeginReload();

            return;
        }

        bool fireInput = automaticFire
            ? Input.GetMouseButton(fireMouseButton)
            : Input.GetMouseButtonDown(fireMouseButton);

        if (fireInput && Time.time >= nextShotTime)
            FireShot();
    }

    private void OnDisable()
    {
        reloadSequence?.Kill();
        reloadSequence = null;
        isReloading = false;

        RestoreWeaponPose();

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);
    }

    private void OnDestroy()
    {
        reloadSequence?.Kill();
    }

    // --------------------------------------------------
    // SHOOTING
    // --------------------------------------------------

    private void FireShot()
    {
        if (playerCamera == null)
            return;

        Vector3 shotOrigin = muzzlePoint != null
            ? muzzlePoint.position
            : playerCamera.transform.position;

        // Each pellet gets its own spread direction and ray. The camera ray chooses
        // the crosshair aim point; the muzzle ray then checks the actual line of fire
        // so the weapon cannot shoot through nearby cover because of camera offset.
        for (int i = 0; i < Mathf.Max(1, pelletsPerShot); i++)
            FirePellet(shotOrigin);

        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        nextShotTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);

        PlayMuzzleFlash();
        EjectCasing();

        if (fireSound != null)
        {
            AudioManager.Play2D(
                fireSound,
                fireSoundVolume);
        }

        if (currentAmmo <= 0 && reloadAutomaticallyWhenEmpty)
            BeginReload();
    }

    private void FirePellet(Vector3 shotOrigin)
    {
        Vector3 cameraDirection = ApplyAccuracySpread(playerCamera.transform.forward);
        Ray cameraRay = new Ray(playerCamera.transform.position, cameraDirection);

        Vector3 aimPoint = cameraRay.origin + cameraRay.direction * maxShotDistance;

        if (Physics.Raycast(
            cameraRay,
            out RaycastHit cameraHit,
            maxShotDistance,
            shotLayers,
            QueryTriggerInteraction.Ignore))
        {
            aimPoint = cameraHit.point;
        }

        Vector3 muzzleToAimPoint = aimPoint - shotOrigin;
        float muzzleRayDistance = muzzleToAimPoint.magnitude;

        if (muzzleRayDistance <= 0.001f)
            return;

        Vector3 muzzleDirection = muzzleToAimPoint / muzzleRayDistance;

        if (Physics.Raycast(
            shotOrigin,
            muzzleDirection,
            out RaycastHit hit,
            muzzleRayDistance + 0.02f,
            shotLayers,
            QueryTriggerInteraction.Ignore))
        {
            bool hitDamageable = ApplyDamage(hit);
            PlayImpactEffect(hit, hitDamageable);
        }
    }

    private Vector3 ApplyAccuracySpread(Vector3 direction)
    {
        if (accuracySpreadDegrees <= 0f)
            return direction;

        float spread = Mathf.Tan(accuracySpreadDegrees * Mathf.Deg2Rad);
        Vector2 randomSpread = Random.insideUnitCircle * spread;

        Transform cameraTransform = playerCamera.transform;

        return (
            direction +
            cameraTransform.right * randomSpread.x +
            cameraTransform.up * randomSpread.y
        ).normalized;
    }

    /// <returns>True when the hit object implements ITurretDamageable.</returns>
    private bool ApplyDamage(RaycastHit hit)
    {
        ITurretDamageable damageable =
            hit.collider.GetComponentInParent<ITurretDamageable>();

        if (damageable != null)
        {
            damageable.TakeDamage(
                damagePerPellet,
                hit.point,
                knockbackForce);

            return true;
        }

        // Fallback for objects that only implement the project's ordinary damage message.
        hit.collider.SendMessageUpwards(
            damageMessage,
            damagePerPellet,
            SendMessageOptions.DontRequireReceiver);

        return false;
    }

    // --------------------------------------------------
    // IMPACT EFFECTS
    // --------------------------------------------------

    private void PlayImpactEffect(RaycastHit hit, bool hitDamageable)
    {
        int hitLayerMask = 1 << hit.collider.gameObject.layer;
        bool isMetal = hitDamageable || (metalImpactLayers.value & hitLayerMask) != 0;

        GameObject particlePrefab = isMetal
            ? metalImpactParticles
            : dirtImpactParticles;

        if (particlePrefab != null)
        {
            GameObject particles = Instantiate(
                particlePrefab,
                hit.point,
                Quaternion.LookRotation(hit.normal));

            Destroy(particles, impactParticleLifetime);
        }

        if (!isMetal && dirtImpactSound != null)
        {
            AudioManager.Play3D(
                dirtImpactSound,
                hit.point,
                impactSoundVolume, 1, 20, 50);
        }
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

        localEuler.y = Random.Range(
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
                casingEjectPoint.right * Random.Range(casingSideForce.x, casingSideForce.y) +
                casingEjectPoint.up * Random.Range(casingUpForce.x, casingUpForce.y);

            casingBody.AddForce(ejectionForce, ForceMode.Impulse);
            casingBody.AddTorque(
                Random.insideUnitSphere * casingRandomTorque,
                ForceMode.Impulse);
        }

        Destroy(casing, casingLifetime);
    }

    // --------------------------------------------------
    // RELOADING
    // --------------------------------------------------

    public void Reload()
    {
        BeginReload();
    }

    private void BeginReload()
    {
        if (isReloading || currentAmmo >= magazineSize || !CanUseWeapon)
            return;

        isReloading = true;
        reloadSequence?.Kill();

        float totalDuration = Mathf.Max(0.01f, reloadDuration);
        float transitionDuration = Mathf.Min(
            Mathf.Max(0.01f, reloadPoseTransitionTime),
            totalDuration * 0.5f);

        // The two transitions are part of Reload Duration. Any remaining time is
        // spent holding the reload pose, so the animation and ammo refill finish together.
        float poseHoldDuration = Mathf.Max(
            0f,
            totalDuration - transitionDuration * 2f);

        reloadSequence = DOTween.Sequence();

        if (weaponVisual != null)
        {
            reloadSequence.Append(
                weaponVisual
                    .DOLocalMove(reloadLocalPosition, transitionDuration)
                    .SetEase(reloadPoseEase));

            reloadSequence.Join(
                weaponVisual
                    .DOLocalRotate(reloadLocalEulerAngles, transitionDuration, RotateMode.Fast)
                    .SetEase(reloadPoseEase));

            reloadSequence.AppendInterval(poseHoldDuration);

            if (hasWeaponDefaultPose)
            {
                reloadSequence.Append(
                    weaponVisual
                        .DOLocalMove(weaponDefaultLocalPosition, transitionDuration)
                        .SetEase(reloadPoseEase));

                reloadSequence.Join(
                    weaponVisual
                        .DOLocalRotate(weaponDefaultLocalEulerAngles, transitionDuration, RotateMode.Fast)
                        .SetEase(reloadPoseEase));
            }
        }
        else
        {
            reloadSequence.AppendInterval(totalDuration);
        }

        reloadSequence.OnComplete(() =>
        {
            currentAmmo = Mathf.Max(1, magazineSize);
            isReloading = false;
            reloadSequence = null;
        });

        if (reloadSound != null)
        {
            AudioManager.Play3D(
                reloadSound,
                transform.position,
                reloadSoundVolume);
        }
    }

    private void RestoreWeaponPose()
    {
        if (!hasWeaponDefaultPose || weaponVisual == null)
            return;

        weaponVisual.localPosition = weaponDefaultLocalPosition;
        weaponVisual.localEulerAngles = weaponDefaultLocalEulerAngles;
    }
}
