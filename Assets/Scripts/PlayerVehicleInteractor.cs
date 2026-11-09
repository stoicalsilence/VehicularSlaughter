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

    public bool IsInVehicle => currentVehicle != null;
    public VehicleSeat NearbyVehicle => nearbyVehicle;
    public VehicleSeat CurrentVehicle => currentVehicle;

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

        // Disable the vehicle camera first. Its OnDisable unlocks the cursor; the first-person
        // MouseLook can re-lock it on its next Update according to its own cursor state.
        vehicle.SetVehicleCameraActive(false);

        transform.SetParent(previousParent, true);
        if (exitPoint != null)
        {
            Vector3 euler = exitPoint.rotation.eulerAngles;
            transform.SetPositionAndRotation(
                exitPoint.position,
                Quaternion.Euler(0f, euler.y, 0f));
        }

        vehicle.Release();
        currentVehicle = null;
        nearbyVehicle = null;
        currentCarController = null;

        if (playerCamera != null)
            playerCamera.enabled = previousPlayerCameraEnabled;

        if (playerAudioListener != null)
            playerAudioListener.enabled = previousPlayerAudioListenerEnabled;

        if (characterController != null)
            characterController.enabled = previousCharacterControllerEnabled;

        if (playerController != null)
        {
            playerController.SendMessage("ResetMovementState", SendMessageOptions.DontRequireReceiver);
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
