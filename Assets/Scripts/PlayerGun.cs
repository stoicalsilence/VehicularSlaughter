using DG.Tweening;
using System.Collections.Generic;
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

    public PlayerStats playerStats;
    private PlayerInventory playerInventory;
    private PlayerHealth playerHealth;

    [Tooltip("The player's first-person camera. Auto-found in children when left empty.")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("Used to prevent firing/reloading while the player occupies a vehicle.")]
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;

    [Tooltip("Animator on the first-person arms root. The weapon should be a child of the animated rig.")]
    [SerializeField] private Animator weaponAnimator;

    [Tooltip("Optional muzzle position. If empty, shots originate from the camera.")]
    [SerializeField] private Transform muzzlePoint;

    [SerializeField] private GameObject muzzleFlash;

    [Header("Audio")]
    [SerializeField] private AudioClip critSound;
    [SerializeField] private AudioClip overcritSound;
    [SerializeField] private AudioClip overcritSound2;
    [SerializeField] private AudioClip overcritSound3;
    [SerializeField] private AudioClip overcritSound4;

    private int evilEyeHitStacks;
    public float damagePerPellet => playerStats.gunDamagePerPellet;

    public int pelletsPerShot => playerStats.gunPelletsPerShot;

    public float knockbackForce => playerStats.gunKnockbackForce;

    public float fireRate => playerStats.gunFireRate;

    public float accuracySpreadDegrees => playerStats.gunAccuracySpreadDegrees;

    public bool automaticFire = true;

    public float criticalChance => playerStats.gunCriticalChance;

    public float criticalDamageMultiplier => playerStats.gunCriticalDamageMultiplier;
    
    public int magazineSize => playerStats.gunMagazineSize;
    public float reloadDuration => playerStats.gunReloadDuration;

    public bool reloadAutomaticallyWhenEmpty = true; 

    public float maxShotDistance => playerStats.gunMaxShotDistance;


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
    [SerializeField] private AnimationClip idleAnim;

    [SerializeField] private AnimationClip shootAnim;

    [SerializeField] private AnimationClip reloadAnim;

    [Tooltip("Animator layer containing the Idle, Fire and Reload states. Usually 0 (Base Layer).")]
    [Min(0)]
    [SerializeField] private int animatorLayer = 0;

    [Min(0f)]
    [SerializeField] private float animationBlendTime = 0.04f;

    [Min(0.01f)]
    [SerializeField] private float reloadAnimationReferenceDuration = 2.2f;

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
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogError("PlayerGun: PlayerStats not found!", this);
            enabled = false;
            return;
        }

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

        playerInventory = GetComponent<PlayerInventory>();
        playerHealth = GetComponent<PlayerHealth>();

        CacheAnimatorData();
    }


    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;

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

        bool hasEvilEye = playerInventory != null &&
                          playerInventory.HasItemByName("Evil Eye");

        // Do not carry an old bonus into a newly equipped Evil Eye.
        if (!hasEvilEye)
            evilEyeHitStacks = 0;

        bool hitAnyDamageable = false;

        for (int i = 0; i < Mathf.Max(1, pelletsPerShot); i++)
        {
            if (FirePellet(shotOrigin))
                hitAnyDamageable = true;
        }

        // Reset once per trigger pull, not once per pellet.
        if (!hitAnyDamageable)
            evilEyeHitStacks = 0;

        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        nextShotTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);

        PlayMuzzleFlash();
        EjectCasing();

        if (fireSound != null)
        {
            AudioManager.Play2D(fireSound, fireSoundVolume);
        }

        if (currentAmmo <= 0 && reloadAutomaticallyWhenEmpty)
            BeginReload();
    }

    private bool FirePellet(Vector3 shotOrigin)
    {
        Vector3 cameraDirection = ApplyAccuracySpread(
            playerCamera.transform.forward);

        Ray cameraRay = new Ray(
            playerCamera.transform.position,
            cameraDirection);

        // Find the first non-damageable obstruction.
        // Damageable targets do not stop the camera aiming ray.
        float cameraAimDistance = maxShotDistance;

        RaycastHit[] cameraHits = Physics.RaycastAll(
            cameraRay,
            maxShotDistance,
            shotLayers,
            QueryTriggerInteraction.Ignore);

        System.Array.Sort(
            cameraHits,
            (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit cameraHit in cameraHits)
        {
            ITurretDamageable damageable =
                cameraHit.collider.GetComponentInParent<ITurretDamageable>();

            if (damageable != null)
                continue;

            cameraAimDistance = cameraHit.distance;
            break;
        }

        Vector3 aimPoint =
            cameraRay.origin + cameraRay.direction * cameraAimDistance;

        Vector3 muzzleToAimPoint = aimPoint - shotOrigin;
        float muzzleRayDistance = muzzleToAimPoint.magnitude;

        if (muzzleRayDistance <= 0.001f)
            return false;

        Vector3 muzzleDirection =
            muzzleToAimPoint / muzzleRayDistance;

        // Collect all hits along the complete trajectory.
        RaycastHit[] hits = Physics.RaycastAll(
            shotOrigin,
            muzzleDirection,
            muzzleRayDistance + 0.02f,
            shotLayers,
            QueryTriggerInteraction.Ignore);

        System.Array.Sort(
            hits,
            (a, b) => a.distance.CompareTo(b.distance));

        int piercesRemaining = Mathf.Max(
            0,
            Mathf.FloorToInt(playerStats.pierces));

        int enemiesHit = 0;

        // Prevent multiple colliders on the same enemy from counting separately.
        HashSet<Transform> hitTargets = new HashSet<Transform>();

        bool hitAnyDamageable = false;

        bool hasEvilEye = playerInventory != null &&
                          playerInventory.HasItemByName("Evil Eye");

        int evilEyeItemStacks = hasEvilEye
            ? playerInventory.GetStacksByName("Evil Eye")
            : 0;

        foreach (RaycastHit hit in hits)
        {
            Transform target = hit.collider.transform.root;

            if (!hitTargets.Add(target))
                continue;

            int criticalHits = RollCriticalHits();

            switch (criticalHits)
            {
                case 1:
                    AudioManager.Play3D(
                        critSound, hit.point, impactSoundVolume, 1, 20, 50);
                    break;

                case 2:
                    AudioManager.Play3D(
                        overcritSound, hit.point, impactSoundVolume, 1, 20, 50);
                    break;

                case 3:
                    AudioManager.Play3D(
                        overcritSound2, hit.point, impactSoundVolume, 1, 20, 50);
                    break;

                case 4:
                    AudioManager.Play3D(
                        overcritSound3, hit.point, impactSoundVolume, 0.8f, 20, 50);
                    break;

                case 5:
                    AudioManager.Play3D(
                        overcritSound4, hit.point, impactSoundVolume, 1, 20, 50);
                    break;
            }

            float finalDamage = CalculateCriticalDamage(
                damagePerPellet,
                criticalHits);

            // Apply the Evil Eye bonus using the current hit streak.
            if (hasEvilEye)
            {
                float evilEyeExtraDamage =
                    5f * evilEyeItemStacks * evilEyeHitStacks;

                finalDamage += evilEyeExtraDamage;
            }

            if (playerHealth != null &&
                playerInventory != null &&
                playerHealth.CurrentHealth <= playerHealth.MaxHealth / 2f)
            {
                int stackCount =
                    playerInventory.GetStacksByName("Angerjuice Injection");

                float damageMultiplier = 1f + (0.5f * stackCount);

                finalDamage *= damageMultiplier;
            }

            finalDamage += GetBonusSodaDamage();

            bool hitDamageable = ApplyDamage(
                hit,
                finalDamage,
                criticalHits);

            PlayImpactEffect(hit, hitDamageable);

            // Non-damageable objects block bullets but do not consume enemy pierces.
            if (!hitDamageable)
                break;

            hitAnyDamageable = true;
            enemiesHit++;

            // Increase Evil Eye's hit streak after applying this hit's damage.
            if (hasEvilEye)
                evilEyeHitStacks++;

            // Initial enemy hit + additional pierces.
            if (enemiesHit > piercesRemaining)
                break;
        }

        return hitAnyDamageable;
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
    private bool ApplyDamage(
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

            return true;
        }

        hit.collider.SendMessageUpwards(
            damageMessage,
            finalDamage,
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

        return playerStats.maxHealth * 0.2f * stacks;
    }
}
