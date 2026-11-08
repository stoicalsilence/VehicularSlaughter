using DG.Tweening;
using UnityEngine;

/// <summary>
/// First-person hitscan weapon for the on-foot player.
/// Weapon behaviour is defined by stats rather than weapon-specific classes:
/// pellets + spread can create a shotgun, high fire rate can create an SMG, etc.
///
/// The Animator should be on the first-person arms root, with the weapon beneath it,
/// so one set of clips animates the arms and weapon together.
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

    [Tooltip("Animator on the first-person arms root. The weapon should be a child of the animated rig.")]
    [SerializeField] private Animator weaponAnimator;

    [Tooltip("Optional muzzle position. If empty, shots originate from the camera.")]
    [SerializeField] private Transform muzzlePoint;

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
    [SerializeField] public int magazineSize = 30;

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


    [Header("Weapon Animation")]
    [Tooltip("Full Animator state path, usually 'Base Layer.Idle'. This should be the default looping state.")]
    [SerializeField] private AnimationClip idleAnim;

    [Tooltip("Full Animator state path, usually 'Base Layer.Fire'. This should be a non-looping one-shot state that returns to Idle.")]
    [SerializeField] private AnimationClip shootAnim;

    [Tooltip("Full Animator state path, usually 'Base Layer.Reload'. PlayerGun returns to Idle when the gameplay reload timer completes.")]
    [SerializeField] private AnimationClip reloadAnim;

    [Tooltip("Animator layer containing the Idle, Fire and Reload states. Usually 0 (Base Layer).")]
    [Min(0)]
    [SerializeField] private int animatorLayer = 0;

    [Tooltip("Blend time used when entering Fire or Reload and when returning to Idle.")]
    [Min(0f)]
    [SerializeField] private float animationBlendTime = 0.04f;

    [Tooltip("The reload clip's duration in seconds when played at 1x speed. Set this to the actual reload clip length.")]
    [Min(0.01f)]
    [SerializeField] private float reloadAnimationReferenceDuration = 2.2f;

    [Tooltip("Float parameter used as the Reload state's Speed Multiplier. Create this as a Float in the Animator Controller.")]
    [SerializeField] private string reloadSpeedParameter = "ReloadSpeed";


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

    [Tooltip("Played by the PlayReloadSound animation event, not automatically at reload start.")]
    [SerializeField] private AudioClip reloadSound;

    [Tooltip("Optional sounds called by animation events on the arms Animator.")]
    [SerializeField] private AudioClip magazineOutSound;
    [SerializeField] private AudioClip magazineInSound;
    [SerializeField] private AudioClip boltActionSound;
    [SerializeField] private AudioClip shellInsertSound;

    [Range(0f, 1f)]
    [SerializeField] private float reloadSoundVolume = 0.8f;


    // Runtime state.
    private int currentAmmo;
    private bool isReloading;
    private float nextShotTime;
    private float muzzleFlashOffTime;

    private Sequence reloadSequence;

    // Cached Animator data.
    private int idleStateHash;
    private int fireStateHash;
    private int reloadStateHash;
    private int reloadSpeedParameterHash;

    private bool hasIdleState;
    private bool hasFireState;
    private bool hasReloadState;
    private bool hasReloadSpeedParameter;

    private float originalAnimatorSpeed = 1f;
    private bool usingAnimatorSpeedFallback;


    public int CurrentAmmo => currentAmmo;
    public bool IsReloading => isReloading;
    public int MagazineSize => magazineSize;
    public bool CanFire => CanUseWeapon && !isReloading && currentAmmo > 0;


    // Read/write stat properties let a future upgrade system change the weapon at runtime.
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

        if (weaponAnimator == null)
            weaponAnimator = GetComponentInChildren<Animator>(true);

        currentAmmo = Mathf.Max(1, magazineSize);

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);

        if (playerCamera == null)
            Debug.LogError("PlayerGun: Assign the player's first-person camera.", this);

        CacheAnimatorData();
    }


    private void Update()
    {
        UpdateMuzzleFlash();

        // Cancel a reload if the player enters a vehicle midway through it.
        // Ammo is not replenished; an empty weapon reloads normally after exiting.
        if (!CanUseWeapon)
        {
            if (isReloading)
                CancelReload();

            return;
        }

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

        RestoreAnimatorSpeed();
        PlayIdleAnimation();

        if (muzzleFlash != null)
            muzzleFlash.SetActive(false);
    }


    private void OnDestroy()
    {
        reloadSequence?.Kill();
        RestoreAnimatorSpeed();
    }


    // --------------------------------------------------
    // ANIMATOR SETUP
    // --------------------------------------------------

    private void CacheAnimatorData()
    {
        if (weaponAnimator == null)
            return;

        originalAnimatorSpeed = weaponAnimator.speed;

        idleStateHash = Animator.StringToHash(idleAnim.name);
        fireStateHash = Animator.StringToHash(shootAnim.name);
        reloadStateHash = Animator.StringToHash(reloadAnim.name);
        reloadSpeedParameterHash = Animator.StringToHash(reloadSpeedParameter);

        if (weaponAnimator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("PlayerGun: The assigned weapon Animator has no Runtime Animator Controller.", this);
            return;
        }

        if (animatorLayer < 0 || animatorLayer >= weaponAnimator.layerCount)
        {
            Debug.LogWarning("PlayerGun: Animator Layer index is invalid. Check the Weapon Animation settings.", this);
            return;
        }

        hasIdleState = weaponAnimator.HasState(animatorLayer, idleStateHash);
        hasFireState = weaponAnimator.HasState(animatorLayer, fireStateHash);
        hasReloadState = weaponAnimator.HasState(animatorLayer, reloadStateHash);

        if (!hasIdleState)
            Debug.LogWarning($"PlayerGun: Animator state '{idleAnim.name}' was not found.", this);

        if (!hasFireState)
            Debug.LogWarning($"PlayerGun: Animator state '{shootAnim.name}' was not found. Shooting will still work, but without a fire animation.", this);

        if (!hasReloadState)
            Debug.LogWarning($"PlayerGun: Animator state '{reloadAnim.name}' was not found. Reloading will still work, but without a reload animation.", this);

        foreach (AnimatorControllerParameter parameter in weaponAnimator.parameters)
        {
            if (parameter.nameHash == reloadSpeedParameterHash &&
                parameter.type == AnimatorControllerParameterType.Float)
            {
                hasReloadSpeedParameter = true;
                break;
            }
        }

        if (!hasReloadSpeedParameter && hasReloadState)
        {
            Debug.LogWarning(
                $"PlayerGun: Float Animator parameter '{reloadSpeedParameter}' was not found. " +
                "Reload speed will be applied to the Animator directly. Add the parameter and use it as the Reload state's Speed Multiplier for finer control.",
                this);
        }
    }


    private void PlayFireAnimation()
    {
        if (!CanAnimate(hasFireState))
            return;

        // CrossFade back into the state at normalized time zero. This also retriggers
        // the fire animation for fast automatic weapons while a previous shot is playing.
        weaponAnimator.CrossFadeInFixedTime(
            fireStateHash,
            Mathf.Max(0f, animationBlendTime),
            animatorLayer,
            0f);
    }


    private void PlayReloadAnimation(float totalReloadDuration)
    {
        if (!CanAnimate(hasReloadState))
            return;

        float referenceDuration = Mathf.Max(0.01f, reloadAnimationReferenceDuration);
        float playbackSpeed = referenceDuration / Mathf.Max(0.01f, totalReloadDuration);

        if (hasReloadSpeedParameter)
        {
            weaponAnimator.SetFloat(reloadSpeedParameterHash, playbackSpeed);
        }
        else
        {
            // Fallback for a simple controller without a Speed Multiplier parameter.
            // The arms Animator is dedicated to the first-person weapon rig.
            weaponAnimator.speed = originalAnimatorSpeed * playbackSpeed;
            usingAnimatorSpeedFallback = true;
        }

        weaponAnimator.CrossFadeInFixedTime(
            reloadStateHash,
            Mathf.Max(0f, animationBlendTime),
            animatorLayer,
            0f);
    }


    private void PlayIdleAnimation()
    {
        RestoreAnimatorSpeed();

        if (!CanAnimate(hasIdleState))
            return;

        weaponAnimator.CrossFadeInFixedTime(
            idleStateHash,
            Mathf.Max(0f, animationBlendTime),
            animatorLayer,
            0f);
    }


    private bool CanAnimate(bool stateExists)
    {
        return weaponAnimator != null &&
               weaponAnimator.isActiveAndEnabled &&
               weaponAnimator.runtimeAnimatorController != null &&
               animatorLayer >= 0 &&
               animatorLayer < weaponAnimator.layerCount &&
               stateExists;
    }


    private void RestoreAnimatorSpeed()
    {
        if (!usingAnimatorSpeedFallback)
            return;

        if (weaponAnimator != null)
            weaponAnimator.speed = originalAnimatorSpeed;

        usingAnimatorSpeedFallback = false;
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

        PlayFireAnimation();

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
        PlayReloadAnimation(totalDuration);

        // Gameplay remains authoritative. The animation is played at a calculated
        // speed and animation events provide synchronized sound/visual feedback.
        reloadSequence = DOTween.Sequence();
        reloadSequence.AppendInterval(totalDuration);

        reloadSequence.OnComplete(() =>
        {
            currentAmmo = Mathf.Max(1, magazineSize);
            isReloading = false;
            reloadSequence = null;

            PlayIdleAnimation();
        });
    }


    private void CancelReload()
    {
        reloadSequence?.Kill();
        reloadSequence = null;
        isReloading = false;

        PlayIdleAnimation();
    }


    // --------------------------------------------------
    // ANIMATION EVENT CALLBACKS
    // Called through WeaponAnimationEvents on the same GameObject as the Animator.
    // --------------------------------------------------

    public void AnimationEvent_PlayReloadSound()
    {
        PlayReloadEventSound(reloadSound);
    }

    public void AnimationEvent_PlayMagazineOutSound()
    {
        PlayReloadEventSound(magazineOutSound);
    }

    public void AnimationEvent_PlayMagazineInSound()
    {
        PlayReloadEventSound(magazineInSound);
    }

    public void AnimationEvent_PlayBoltActionSound()
    {
        PlayReloadEventSound(boltActionSound);
    }

    public void AnimationEvent_PlayShellInsertSound()
    {
        PlayReloadEventSound(shellInsertSound);
    }

    private void PlayReloadEventSound(AudioClip clip)
    {
        // Ignore late animation events after reload cancellation or vehicle entry.
        if (clip == null || !isReloading || !CanUseWeapon)
            return;

        Vector3 soundPosition = weaponAnimator != null
            ? weaponAnimator.transform.position
            : transform.position;

        AudioManager.Play2D(
            clip,
            reloadSoundVolume);
    }
}
