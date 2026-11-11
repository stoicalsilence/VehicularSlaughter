using Q3Movement;
using UnityEngine;

/// <summary>
/// Finds a nearby VehicleSeat and switches between the Q3 first-person controller and vehicle
/// controls/camera. Attach this to the same root GameObject as Q3PlayerController and its
/// CharacterController.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerVehicleInteractor : MonoBehaviour
{
    [Header("Player References")]
    [SerializeField] private Q3PlayerController playerController;
    [SerializeField] private CharacterController characterController;
    [Tooltip("The player's first-person camera, not the vehicle camera.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Optional AudioListener on the first-person camera. Auto-detected if left empty.")]
    [SerializeField] private AudioListener playerAudioListener;

    [Header("Interaction")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField, Min(0.25f)] private float interactionRadius = 3f;
    [Tooltip("Only colliders on these layers are considered for vehicle detection.")]
    [SerializeField] private LayerMask vehicleLayers = ~0;
    [Tooltip("Prevent exiting while the vehicle is moving faster than this speed, in km/h.")]
    [SerializeField] private GameObject enterPrompt;
    [Tooltip("Optional UI object shown while inside a vehicle. The prompt can explain the exit key.")]
    [SerializeField] private GameObject exitPrompt;

    private readonly Collider[] overlapBuffer = new Collider[64];
    private VehicleSeat nearbyVehicle;
    private VehicleSeat currentVehicle;
    public CarController currentCarController;
    private Transform previousParent;

    private bool previousPlayerControllerEnabled;
    private bool previousCharacterControllerEnabled;
    private bool previousPlayerCameraEnabled;
    private bool previousPlayerAudioListenerEnabled;

    public GameObject crosshair;

    public AudioClip enterVehicleSound;
    public AudioClip exitVehicleSound;

    public bool IsInVehicle => currentVehicle != null;

    private void Awake()
    {
        if (playerController == null)
            playerController = GetComponent<Q3PlayerController>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);

        if (playerAudioListener == null && playerCamera != null)
            playerAudioListener = playerCamera.GetComponent<AudioListener>();

        if (playerController == null)
            Debug.LogError("PlayerVehicleInteractor: Assign a Q3PlayerController on the player root.", this);

        if (characterController == null)
            Debug.LogError("PlayerVehicleInteractor: A CharacterController is required on the player root.", this);

        if (playerCamera == null)
            Debug.LogError("PlayerVehicleInteractor: Assign the player's first-person Camera.", this);

        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, false);
    }

    private void Update()
    {
        if (currentVehicle != null)
        {
            nearbyVehicle = null;
            SetPromptActive(enterPrompt, false);
            SetPromptActive(exitPrompt, true);

            if (Input.GetKeyDown(interactKey))
            {
                    ExitVehicle();
            }

            return;
        }

        nearbyVehicle = FindNearbyVehicle();
        SetPromptActive(enterPrompt, nearbyVehicle != null);
        SetPromptActive(exitPrompt, false);

        if (nearbyVehicle != null && Input.GetKeyDown(interactKey))
            EnterVehicle(nearbyVehicle);
    }

    private VehicleSeat FindNearbyVehicle()
    {
        Vector3 origin = transform.position;
        int count = Physics.OverlapSphereNonAlloc(
            origin,
            interactionRadius,
            overlapBuffer,
            vehicleLayers,
            QueryTriggerInteraction.Collide);

        VehicleSeat best = null;
        float bestDistanceSqr = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider candidateCollider = overlapBuffer[i];
            if (candidateCollider == null)
                continue;

            VehicleSeat seat = candidateCollider.GetComponentInParent<VehicleSeat>();
            if (seat == null || !seat.CanBeEnteredByPlayer)
                continue;

            Vector3 closestPoint = candidateCollider.ClosestPoint(origin);
            float distanceSqr = (closestPoint - origin).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                best = seat;
            }
        }

        return best;
    }

    private void EnterVehicle(VehicleSeat seat)
    {
        if (seat == null || !seat.TryOccupy())
            return;

        currentCarController = FindCarController(seat);

        if (currentCarController == null)
        {
            Debug.LogError(
                "PlayerVehicleInteractor: The occupied vehicle has no CarController. " +
                "Player damage cannot be routed to its health.",
                seat
            );
        }

        AudioManager.Play2D(enterVehicleSound, 1);
        previousParent = transform.parent;
        previousPlayerControllerEnabled = playerController != null && playerController.enabled;
        previousCharacterControllerEnabled = characterController != null && characterController.enabled;
        previousPlayerCameraEnabled = playerCamera != null && playerCamera.enabled;
        previousPlayerAudioListenerEnabled = playerAudioListener != null && playerAudioListener.enabled;

        // Stop player movement/collision before teleporting the player root into the vehicle.
        if (playerController != null)
        {
            playerController.enabled = false;
            playerController.SendMessage("ResetMovementState", SendMessageOptions.DontRequireReceiver);
        }

        if (characterController != null)
            characterController.enabled = false;

        transform.SetParent(seat.DriverSeat, true);
        transform.SetPositionAndRotation(seat.DriverSeat.position, seat.DriverSeat.rotation);

        if (playerCamera != null)
            playerCamera.enabled = false;

        if (playerAudioListener != null)
            playerAudioListener.enabled = false;

        seat.SetVehicleCameraActive(true);
        currentVehicle = seat;
        nearbyVehicle = null;

        crosshair.SetActive(false);

        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, true);
    }

    private void ExitVehicle()
    {
        if (currentVehicle == null)
            return;

        VehicleSeat vehicle = currentVehicle;
        Transform exitPoint = vehicle.ExitPoint;
        AudioManager.Play2D(exitVehicleSound, 1);

        // Disable the vehicle camera first.
        vehicle.SetVehicleCameraActive(false);

        // Restore the player's original parent.
        transform.SetParent(previousParent, true);

        if (exitPoint != null)
        {
            // Use the ExitPoint's forward direction as the player's
            // desired facing direction after exiting.
            Vector3 exitForward = Vector3.ProjectOnPlane(
                exitPoint.forward,
                Vector3.up
            );

            // Fallback in case the ExitPoint has an invalid forward direction.
            if (exitForward.sqrMagnitude < 0.001f)
            {
                exitForward = Vector3.ProjectOnPlane(
                    vehicle.transform.forward,
                    Vector3.up
                );
            }

            Quaternion exitRotation = exitForward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(exitForward.normalized, Vector3.up)
                : transform.rotation;

            transform.SetPositionAndRotation(
                exitPoint.position,
                exitRotation
            );
        }

        vehicle.Release();

        currentVehicle = null;
        nearbyVehicle = null;
        currentCarController = null;

        // Restore the first-person camera.
        if (playerCamera != null)
            playerCamera.enabled = previousPlayerCameraEnabled;

        if (playerAudioListener != null)
            playerAudioListener.enabled = previousPlayerAudioListenerEnabled;

        if (characterController != null)
            characterController.enabled = previousCharacterControllerEnabled;

        if (playerController != null)
        {
            playerController.SendMessage(
                "ResetMovementState",
                SendMessageOptions.DontRequireReceiver
            );

            // IMPORTANT:
            // Update MouseLook's cached rotation BEFORE enabling the controller.
            playerController.SyncMouseLookToCurrentRotation(
                playerCamera != null ? playerCamera.transform : null
            );

            playerController.enabled = previousPlayerControllerEnabled;
        }

        crosshair.SetActive(true);

        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, false);
    }

    private void OnDisable()
    {
        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, false);

        // Avoid leaving the player parented to a vehicle if this component is disabled at runtime.
        if (currentVehicle != null)
            ExitVehicle();
    }

    private static void SetPromptActive(GameObject prompt, bool active)
    {
        if (prompt != null && prompt.activeSelf != active)
            prompt.SetActive(active);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }

    private static CarController FindCarController(VehicleSeat seat)
    {
        if (seat == null)
            return null;

        CarController car = seat.GetComponent<CarController>();

        if (car == null)
            car = seat.GetComponentInParent<CarController>();

        if (car == null)
            car = seat.GetComponentInChildren<CarController>(true);

        return car;
    }

    public bool TryDamageCurrentVehicle(
    float damage,
    HordeEnemy attacker = null)
    {
        if (currentCarController == null)
            return false;

        return currentCarController.TakeVehicleDamage(damage, attacker);
    }
}
