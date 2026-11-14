
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public sealed class PlayerRadio : MonoBehaviour
{
    [Header("References")]
    public PlayerStats playerStats;
    public PlayerGun playerGun;
    public PlayerVehicleInteractor vehicleInteractor;
    public Camera playerCamera;
    public CarController carController;

    [Header("Arms")]
    public GameObject weaponArms;
    public GameObject radioArms;
    public Animator radioAnimator;

    public AnimationClip radioArmsUnequip;

    [Header("Audio")]
    public AudioClip denySound;

    [Header("Charge UI")]
    public GameObject radioUI;
    public Image chargeMeterImage;
    public float fillLerpSpeed = 10f;
    public TextMeshProUGUI promptText;

    public Color unavailableChargeColor = Color.gray;
    public Color repositionChargeColor = Color.cyan;
    public Color respawnChargeColor = Color.green;
    public Color airstrikeChargeColor = Color.red;

    public GameObject generalCooldownUI;
    public TextMeshProUGUI generalCooldownText;

    private float displayedCharge;

    [Header("Placement")]
    public GameObject carGhostPrefab;
    public GameObject airstrikeGhostPrefab;

    public LayerMask placementLayers = ~0;
    public float placementDistance = 100f;
    public float carGroundOffset = 0.5f;
    public float airstrikeGhostOffset = 0.05f;

    [Header("Animation")]
    public int animatorLayer = 0;
    public float animationBlendTime = 0.04f;

    private Quaternion weaponArmsOriginalRotation;
    private Tween armsRotationTween;

    public AudioClip radiobleep;
    public AudioClip radioNoise;
    public AudioClip repositionCarSound;
    public AudioClip resetCarSound;
    public AudioClip airSupportInbound;
    public AudioClip selectCallInSound;

    public GameObject cashInSelectParticleBlue;
    public GameObject cashInSelectParticleRed;
    private bool selectionParticlesSpawned;

    public event Action<Vector3> AirstrikeRequested;

    private enum RadioState
    {
        WeaponReady,
        Opening,
        Charging,
        Placing,
        Returning
    }

    private enum CallType
    {
        None,
        RepositionCar,
        RespawnCar,
        Airstrike
    }

    private RadioState state = RadioState.WeaponReady;
    private CallType selectedCall = CallType.None;

    private float charge;

    private float nextRepositionTime;
    private float nextRespawnTime;
    private float nextAirstrikeTime;

    private bool playerGunWasEnabled;
    private bool hasValidPlacement;

    private GameObject placementGhost;

    private Vector3 placementGroundPoint;
    private Quaternion placementRotation;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerGun == null)
            playerGun = GetComponentInParent<PlayerGun>();

        if (vehicleInteractor == null)
            vehicleInteractor = GetComponentInParent<PlayerVehicleInteractor>();

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (radioAnimator == null && radioArms != null)
            radioAnimator = radioArms.GetComponentInChildren<Animator>(true);
    }

    private void Start()
    {
        if (playerStats == null || weaponArms == null ||
            radioArms == null || playerCamera == null)
        {
            Debug.LogError("PlayerRadio: Missing required references.", this);
            enabled = false;
            return;
        }

        radioArms.SetActive(false);

        if (radioUI != null)
            radioUI.SetActive(false);

        UpdateChargeMeter();
        UpdateCooldownUI();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        if (state == RadioState.WeaponReady)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                OpenRadio();
        }
        else if (state == RadioState.Charging)
        {
            UpdateCharging();
        }
        else if (state == RadioState.Placing)
        {
            UpdatePlacement();
        }

        UpdateChargeMeter();
        UpdateCooldownUI();
    }

    private void OpenRadio()
    {
        if (vehicleInteractor != null &&
            vehicleInteractor.currentCarController != null)
            return;

        if (GetRadioCooldownRemaining() > 0f)
        {
            DenyCallIn();
            return;
        }

        charge = 0f;
        displayedCharge = 0f;
        selectedCall = CallType.None;

        if (radioUI != null)
            radioUI.SetActive(true);

        if (chargeMeterImage != null)
        {
            chargeMeterImage.fillAmount = 0f;
            chargeMeterImage.color = unavailableChargeColor;
        }

        UpdateCooldownUI();

        StartCoroutine(OpenRadioRoutine());
    }

    private IEnumerator OpenRadioRoutine()
    {
        state = RadioState.Opening;

        if (playerGun != null)
        {
            playerGunWasEnabled = playerGun.enabled;
            playerGun.enabled = false;
        }

        weaponArmsOriginalRotation = weaponArms.transform.localRotation;

        armsRotationTween?.Kill();

        armsRotationTween = weaponArms.transform.DOLocalRotate(weaponArmsOriginalRotation.eulerAngles + new Vector3(90f, 0f, 0f),0.5f).SetEase(Ease.InOutCubic);

        yield return new WaitForSeconds(0.5f);

        AudioManager.Play2D(radiobleep, 0.3f);
        AudioManager.Play2D(radioNoise, 0.6f);

        radioArms.SetActive(true);

        state = RadioState.Charging;

        UpdateChargePrompt();
    }

    private void UpdateCharging()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            BeginReturn();
            return;
        }

        if (Input.GetKey(KeyCode.Tab))
        {
            charge = Mathf.Min(
                1f,
                charge + playerStats.radioChargeRate * Time.deltaTime
            );

            UpdateChargeMeter();
            UpdateChargePrompt();
        }
        else
        {
            SelectCallIn();
        }
    }

    private void UpdateChargeMeter()
    {
        displayedCharge = Mathf.Lerp(
            displayedCharge,
            charge,
            Time.deltaTime * fillLerpSpeed
        );

        if (chargeMeterImage == null)
            return;

        chargeMeterImage.fillAmount = displayedCharge;

        switch (GetCallTypeForCharge())
        {
            case CallType.RepositionCar:
                chargeMeterImage.color = repositionChargeColor;
                break;

            case CallType.RespawnCar:
                chargeMeterImage.color = respawnChargeColor;
                break;

            case CallType.Airstrike:
                chargeMeterImage.color = airstrikeChargeColor;
                break;

            default:
                chargeMeterImage.color = unavailableChargeColor;
                break;
        }
    }

    private void UpdateChargePrompt()
    {
        if (promptText == null)
            return;

        if (charge < 1f / 3f)
        {
            promptText.text = "HOLD TAB TO CHARGE";
        }
        else if (charge < 2f / 3f)
        {
            promptText.text = "RELEASE TAB: REPOSITION CAR";
        }
        else if (charge < 0.9999f)
        {
            promptText.text = "RELEASE TAB: RESPAWN CAR";
        }
        else
        {
            promptText.text = "RELEASE TAB: AIRSTRIKE";
        }
    }

    private void SelectCallIn()
    {
        if (charge < 1f / 3f)
        {
            BeginReturn();
            return;
        }

        if (charge < 2f / 3f)
        {
            selectedCall = CallType.RepositionCar;
        }
        else if (charge < 0.9999f)
        {
            selectedCall = CallType.RespawnCar;
        }
        else
        {
            selectedCall = CallType.Airstrike;
        }

        AudioManager.Play2D(selectCallInSound);

        radioUI.SetActive(false);

        if (selectedCall != CallType.Airstrike &&
            carController == null)
        {
            Debug.LogError("PlayerRadio: No CarController assigned.", this);
            BeginReturn();
            return;
        }

        CreatePlacementGhost();
    }

    private float GetRadioCooldownRemaining()
    {
        float repositionRemaining =
            nextRepositionTime - Time.time;

        float respawnRemaining =
            nextRespawnTime - Time.time;

        float airstrikeRemaining =
            nextAirstrikeTime - Time.time;

        return Mathf.Max(
            0f,
            repositionRemaining,
            respawnRemaining,
            airstrikeRemaining
        );
    }

    private void DenyCallIn()
    {
        if (denySound != null)
            AudioManager.Play2D(denySound, 1f);

        BeginReturn();
    }

    private void CreatePlacementGhost()
    {
        selectionParticlesSpawned = false;

        GameObject prefab = selectedCall == CallType.Airstrike
            ? airstrikeGhostPrefab
            : carGhostPrefab;

        if (prefab == null)
        {
            Debug.LogError("PlayerRadio: Missing placement ghost prefab.", this);
            BeginReturn();
            return;
        }

        placementGhost = Instantiate(prefab);

        Collider[] ghostColliders =
            placementGhost.GetComponentsInChildren<Collider>(true);

        foreach (Collider ghostCollider in ghostColliders)
            ghostCollider.enabled = false;

        hasValidPlacement = false;

        state = RadioState.Placing;

        if (promptText != null)
        {
            promptText.text =
                "LEFT CLICK: CONFIRM\nRIGHT CLICK: CANCEL";
        }
    }

    private void UpdatePlacement()
    {
        UpdatePlacementGhost();

        if (Input.GetKeyDown(KeyCode.Escape) ||
            Input.GetMouseButtonDown(1))
        {
            BeginReturn();
            return;
        }

        if (Input.GetMouseButtonDown(0))
            ConfirmCallIn();
    }

    private void UpdatePlacementGhost()
    {
        RaycastHit hit;

        if (!TryGetPlacementHit(out hit))
        {
            hasValidPlacement = false;

            if (placementGhost != null)
                placementGhost.SetActive(false);

            return;
        }

        hasValidPlacement = true;

        placementGroundPoint = hit.point;

        if (selectedCall == CallType.Airstrike)
        {
            placementRotation = Quaternion.identity;

            placementGhost.transform.SetPositionAndRotation(
                hit.point + Vector3.up * airstrikeGhostOffset,
                placementRotation
            );
        }
        else
        {
            placementRotation = Quaternion.Euler(
                0f,
                carController.transform.eulerAngles.y,
                0f
            );

            placementGhost.transform.SetPositionAndRotation(
                hit.point + Vector3.up * carGroundOffset,
                placementRotation
            );
        }

        if (!placementGhost.activeSelf)
            placementGhost.SetActive(true);

        if (!selectionParticlesSpawned)
        {
            if (selectedCall == CallType.Airstrike)
            {
                GameObject particles = Instantiate(cashInSelectParticleRed, placementGhost.transform.position + Vector3.up * 2f, Quaternion.identity);
                Destroy(particles, 2f);
            }
            else
            {
                GameObject particles = Instantiate(cashInSelectParticleBlue, placementGhost.transform.position + Vector3.up * 2f, Quaternion.identity);
                Destroy(particles, 2f);
            }

            selectionParticlesSpawned = true;
        }
    }

    private bool TryGetPlacementHit(out RaycastHit bestHit)
    {
        Ray ray = playerCamera.ViewportPointToRay(
            new Vector3(0.5f, 0.5f, 0f)
        );

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            placementDistance,
            placementLayers,
            QueryTriggerInteraction.Ignore
        );

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;

            if (hitTransform == transform ||
                hitTransform.IsChildOf(transform))
                continue;

            if (carController != null &&
                (hitTransform == carController.transform ||
                 hitTransform.IsChildOf(carController.transform)))
                continue;

            if (placementGhost != null &&
                (hitTransform == placementGhost.transform ||
                 hitTransform.IsChildOf(placementGhost.transform)))
                continue;

            bestHit = hit;
            return true;
        }

        bestHit = default;
        return false;
    }

    private void ConfirmCallIn()
    {
        if (!hasValidPlacement)
            return;

        switch (selectedCall)
        {
            case CallType.RepositionCar:

                TeleportCar();

                AudioManager.Play2D(repositionCarSound, 0.3f);

                nextRepositionTime =
                Time.time + playerStats.radioRepositionCooldown;
                GameObject particlesBlueReposition = Instantiate(cashInSelectParticleBlue, placementGhost.transform.position + Vector3.up * 2f, Quaternion.identity);
                Destroy(particlesBlueReposition, 2f);

                break;

            case CallType.RespawnCar:

                TeleportCar();

                AudioManager.Play2D(resetCarSound, 0.3f);

                nextRespawnTime =
                Time.time + playerStats.radioRespawnCooldown;
                GameObject particlesBlueRespawn = Instantiate(cashInSelectParticleBlue, placementGhost.transform.position + Vector3.up, Quaternion.identity);
                Destroy(particlesBlueRespawn, 2f);

                break;

            case CallType.Airstrike:

                nextAirstrikeTime =
                    Time.time + playerStats.radioAirstrikeCooldown;

                AudioManager.Play2D(airSupportInbound, 0.3f);

                StartCoroutine(AirstrikeDelayRoutine(
                    placementGroundPoint
                ));

                GameObject particles = Instantiate(cashInSelectParticleRed, placementGhost.transform.position + Vector3.up, Quaternion.identity);
                Destroy(particles, 2f);
                break;
        }

        BeginReturn();
    }

    private void TeleportCar()
    {
        Vector3 destination =
            placementGroundPoint + Vector3.up * carGroundOffset;

        Transform carTransform = carController.transform;

        Rigidbody carBody = carController.GetComponent<Rigidbody>();

        if (carBody != null)
        {
            carBody.velocity = Vector3.zero;
            carBody.angularVelocity = Vector3.zero;

            carBody.position = destination;
            carBody.rotation = placementRotation;

            carBody.WakeUp();
        }
        else
        {
            carTransform.SetPositionAndRotation(
                destination,
                placementRotation
            );
        }

        Physics.SyncTransforms();
    }

    private IEnumerator AirstrikeDelayRoutine(Vector3 targetPosition)
    {
        yield return new WaitForSeconds(
            Mathf.Max(0f, playerStats.radioAirstrikeDelay)
        );

        AirstrikeRequested?.Invoke(targetPosition);
    }

    private void BeginReturn()
    {
        if (state == RadioState.Returning ||
            state == RadioState.WeaponReady)
            return;

        StartCoroutine(ReturnToWeaponRoutine());
    }

    private IEnumerator ReturnToWeaponRoutine()
    {
        state = RadioState.Returning;

        if (placementGhost != null)
        {
            Destroy(placementGhost);
            placementGhost = null;
        }

        hasValidPlacement = false;

        yield return PlayAnimationAndWait(
            radioAnimator,
            radioArmsUnequip
        );

        AudioManager.Play2D(radiobleep, 0.3f);

        radioArms.SetActive(false);

        armsRotationTween?.Kill();

        armsRotationTween = weaponArms.transform.DOLocalRotate(
            weaponArmsOriginalRotation.eulerAngles,
            0.5f
        ).SetEase(Ease.InOutCubic);

        yield return new WaitForSeconds(0.5f);

        charge = 0f;
        selectedCall = CallType.None;

        UpdateChargeMeter();

        if (playerGun != null)
            playerGun.enabled = playerGunWasEnabled;

        state = RadioState.WeaponReady;
    }

    private IEnumerator PlayAnimationAndWait(
        Animator animator,
        AnimationClip clip)
    {
        if (animator == null || clip == null)
            yield break;

        int stateHash = Animator.StringToHash(clip.name);

        if (!animator.HasState(animatorLayer, stateHash))
        {
            Debug.LogError(
                "PlayerRadio: Animator state not found: " + clip.name,
                this
            );

            yield break;
        }

        animator.CrossFadeInFixedTime(
            stateHash,
            animationBlendTime,
            animatorLayer,
            0f
        );

        float elapsed = 0f;
        float timeout = Mathf.Max(clip.length * 3f, 1f);

        while (elapsed < timeout)
        {
            if (animator == null || !animator.isActiveAndEnabled)
                yield break;

            AnimatorStateInfo info =
                animator.GetCurrentAnimatorStateInfo(animatorLayer);

            if (info.shortNameHash == stateHash &&
                info.normalizedTime >= 1f)
                yield break;

            elapsed += Time.deltaTime;

            yield return null;
        }

        Debug.LogWarning(
            "PlayerRadio: Animation timed out: " + clip.name,
            this
        );
    }

    private CallType GetCallTypeForCharge()
    {
        if (charge < 1f / 3f)
            return CallType.None;

        if (charge < 2f / 3f)
            return CallType.RepositionCar;

        if (charge < 0.9999f)
            return CallType.RespawnCar;

        return CallType.Airstrike;
    }

    private void UpdateCooldownUI()
    {
        float remaining = GetRadioCooldownRemaining();

        bool onCooldown = remaining > 0f;

        if (generalCooldownUI != null &&
            generalCooldownUI.activeSelf != onCooldown)
        {
            generalCooldownUI.SetActive(onCooldown);
        }

        if (generalCooldownText != null)
        {
            generalCooldownText.text = onCooldown
                ? Mathf.CeilToInt(remaining) + "s"
                : "(TAB)";
        }
    }
}
