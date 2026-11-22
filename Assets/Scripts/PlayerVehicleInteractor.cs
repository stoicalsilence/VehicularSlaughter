using Q3Movement;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Turret Target Marking")]
    [Tooltip("Initial car turret for marking on foot, before entering a vehicle.")]
    [SerializeField] private TurretGun markingTurret;
    [SerializeField] private KeyCode markTargetKey = KeyCode.Q;
    [SerializeField, Min(1f)] private float markTargetDistance = 200f;
    [Tooltip("Include enemy hitboxes and solid world geometry. Exclude non-blocking effects.")]
    [SerializeField] private LayerMask markTargetLayers = ~0;

    [Header("Easy Target Marking (On Foot + In Car)")]
    [Tooltip("Press Q to mark a visible enemy near the screen centre, both on foot and in a car. Disable for exact crosshair marking.")]
    [FormerlySerializedAs("easyMarkingInCar")]
    [SerializeField] private bool easyTargetMarking = true;
    [Tooltip("When enabled, prefer visible EMP enemies, then Elites, then normal enemies inside the aim-assist area. A direct crosshair hit on a special enemy still wins.")]
    [SerializeField] private bool prioritizeSpecialTargets = true;
    [Tooltip("Performance option: set to enemy collider layers only. The line-of-sight raycasts still use Mark Target Layers so walls block marks.")]
    [SerializeField] private LayerMask markCandidateLayers = ~0;
    [Tooltip("Horizontal aim-assist radius as a fraction of screen width (0.22 = 22%).")]
    [FormerlySerializedAs("carMarkHorizontalRadius")]
    [SerializeField, Range(0.02f, 0.5f)] private float markHorizontalRadius = 0.22f;
    [Tooltip("Vertical aim-assist radius as a fraction of screen height.")]
    [FormerlySerializedAs("carMarkVerticalRadius")]
    [SerializeField, Range(0.02f, 0.5f)] private float markVerticalRadius = 0.25f;

    private readonly Collider[] easyMarkCandidates = new Collider[128];
    private TurretGun lastDrivenTurret;
    private bool markRequested;
    private readonly RaycastHit[] markHitBuffer = new RaycastHit[32];

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

    // The active gameplay view, independent of camera tags and driving input.
    public Camera ActiveViewCamera => currentVehicle != null
        ? currentVehicle.VehicleCamera
        : playerCamera;

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
        // Capture once regardless of whether Q3 movement is enabled.
        markRequested = Time.timeScale > 0f && Input.GetKeyDown(markTargetKey);

        if (currentVehicle != null)
        {
            // Defensive fallback if the car's immediate ejection did not reach this occupant.
            if (currentCarController != null && currentCarController.IsVehicleWrecked)
            {
                currentCarController.EjectOccupant(this);
                return;
            }

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

    private void LateUpdate()
    {
        if (!markRequested)
            return;

        markRequested = false;
        if (Time.timeScale <= 0f)
            return;

        TryMarkTarget();
    }

    private void TryMarkTarget()
    {
        Camera aimCamera = ActiveViewCamera;
        TurretGun turret;
        Transform occupiedCarRoot = null;

        if (currentVehicle != null)
        {
            if (currentCarController == null || currentCarController.IsVehicleWrecked)
                return;

            turret = currentCarController.VehicleTurret;
            occupiedCarRoot = currentCarController.transform;
        }
        else
        {
            // Preserve the on-foot input gate of Q3PlayerController.
            if (playerController == null || !playerController.isActiveAndEnabled)
                return;

            turret = lastDrivenTurret != null ? lastDrivenTurret : markingTurret;
        }

        if (aimCamera == null || !aimCamera.isActiveAndEnabled ||
            turret == null || !turret.isActiveAndEnabled)
            return;

        // Read the centre ray first, preserving deliberate crosshair selection.
        // A directly aimed-at EMP/Elite is always selected immediately.
        HordeEnemy exactEnemy = FindExactMarkTarget(aimCamera, occupiedCarRoot);
        if (exactEnemy != null &&
            (!easyTargetMarking || !prioritizeSpecialTargets ||
             exactEnemy.IsEMP || exactEnemy.IsElite))
        {
            turret.MarkTarget(exactEnemy.transform);
            return;
        }

        // When the crosshair hits a normal enemy, only a visible special enemy
        // may override it. This also skips unnecessary normal-enemy LOS tests.
        HordeEnemy assistedEnemy = easyTargetMarking
            ? FindEasyMarkTarget(aimCamera, occupiedCarRoot,
                prioritizeSpecialTargets && exactEnemy != null)
            : null;

        HordeEnemy chosenEnemy = assistedEnemy != null ? assistedEnemy : exactEnemy;
        if (chosenEnemy != null)
            turret.MarkTarget(chosenEnemy.transform);
    }

    // The first non-self collider on the centre ray must be a living enemy.
    // World geometry remains solid and blocks enemies behind it.
    private HordeEnemy FindExactMarkTarget(Camera aimCamera, Transform occupiedCarRoot)
    {
        Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float distance = Mathf.Max(1f, markTargetDistance);

        // RaycastNonAlloc results are unordered. Find the nearest non-self hit.
        // A full buffer is not guaranteed to contain the nearest hit, so fall
        // back to RaycastAll only on saturation (and only on a Q press).
        int count = Physics.RaycastNonAlloc(ray, markHitBuffer, distance,
            markTargetLayers, QueryTriggerInteraction.Ignore);

        RaycastHit[] hits = markHitBuffer;
        if (count == markHitBuffer.Length)
        {
            hits = Physics.RaycastAll(ray, distance, markTargetLayers,
                QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }

        Collider closest = null;
        float closestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = hits[i].collider;
            if (candidate == null || hits[i].distance >= closestDistance)
                continue;

            Transform hitTransform = candidate.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            // The third-person camera may look through the occupied car.
            // Ignore its own body, but keep all other solid geometry blocking.
            if (occupiedCarRoot != null &&
                (hitTransform == occupiedCarRoot || hitTransform.IsChildOf(occupiedCarRoot)))
                continue;

            closest = candidate;
            closestDistance = hits[i].distance;
        }

        if (closest == null)
            return null;

        HordeEnemy enemy = closest.GetComponentInParent<HordeEnemy>();
        return enemy != null && enemy.isActiveAndEnabled && !enemy.IsDead ? enemy : null;
    }

    // Called only on a Q press in either mode, never every frame.
    // Returns a target; its priority is EMP > Elite > Normal, with screen
    // centering and distance only breaking ties within the same priority.
    private HordeEnemy FindEasyMarkTarget(Camera camera, Transform carRoot, bool specialOnly)
    {
        Vector3 origin = camera.transform.position;
        float maxDistance = Mathf.Max(1f, markTargetDistance);
        int count = Physics.OverlapSphereNonAlloc(origin, maxDistance,
            easyMarkCandidates, markCandidateLayers, QueryTriggerInteraction.Ignore);

        // Do not silently lose potential targets if the fixed buffer fills up.
        Collider[] candidates = easyMarkCandidates;
        if (count == easyMarkCandidates.Length)
        {
            candidates = Physics.OverlapSphere(origin, maxDistance,
                markCandidateLayers, QueryTriggerInteraction.Ignore);
            count = candidates.Length;
        }

        HordeEnemy bestEnemy = null;
        int bestPriority = -1;
        float bestScore = float.PositiveInfinity;
        float horizontalRadius = Mathf.Max(0.02f, markHorizontalRadius);
        float verticalRadius = Mathf.Max(0.02f, markVerticalRadius);

        for (int i = 0; i < count; i++)
        {
            Collider col = candidates[i];
            if (col == null || !col.enabled)
                continue;

            HordeEnemy enemy = col.GetComponentInParent<HordeEnemy>();
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead)
                continue;

            int priority = prioritizeSpecialTargets ? GetMarkPriority(enemy) : 0;
            if ((specialOnly && priority == 0) || priority < bestPriority)
                continue;

            // Use the collider's centre, so the player need not hit a tiny hitbox.
            Vector3 target = col.bounds.center;
            Vector3 screen = camera.WorldToViewportPoint(target);
            if (screen.z <= 0f)
                continue;

            float x = (screen.x - 0.5f) / horizontalRadius;
            float y = (screen.y - 0.5f) / verticalRadius;
            float screenScore = x * x + y * y;
            if (screenScore > 1f)
                continue;

            Vector3 direction = target - origin;
            float range = direction.magnitude;
            if (range < 0.001f || range > maxDistance)
                continue;

            // Only raycast if this candidate can beat the current best.
            float score = screenScore + 0.001f * (range / maxDistance);
            if (priority == bestPriority && score >= bestScore)
                continue;

            // Ignore the player and (when driving) the occupied car, but not obstacles.
            Ray visibilityRay = new Ray(origin, direction / range);
            int hitCount = Physics.RaycastNonAlloc(visibilityRay, markHitBuffer,
                range + 0.05f, markTargetLayers, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = markHitBuffer;
            if (hitCount == markHitBuffer.Length)
            {
                hits = Physics.RaycastAll(visibilityRay, range + 0.05f,
                    markTargetLayers, QueryTriggerInteraction.Ignore);
                hitCount = hits.Length;
            }

            Collider first = null;
            float firstDistance = float.PositiveInfinity;
            for (int h = 0; h < hitCount; h++)
            {
                Collider hitCollider = hits[h].collider;
                if (hitCollider == null || hits[h].distance >= firstDistance)
                    continue;

                Transform hit = hitCollider.transform;
                if (hit == transform || hit.IsChildOf(transform) ||
                    (carRoot != null && (hit == carRoot || hit.IsChildOf(carRoot))))
                    continue;

                first = hitCollider;
                firstDistance = hits[h].distance;
            }

            if (first == null || first.GetComponentInParent<HordeEnemy>() != enemy)
                continue;

            bestPriority = priority;
            bestScore = score;
            bestEnemy = enemy;
        }

        return bestEnemy;
    }

    private static int GetMarkPriority(HordeEnemy enemy)
    {
        if (enemy.IsEMP) return 2;
        if (enemy.IsElite) return 1;
        return 0;
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

            CarController car = FindCarController(seat);
            if (car != null && car.IsVehicleWrecked)
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
        if (seat == null)
            return;

        CarController car = FindCarController(seat);
        if (car != null && car.IsVehicleWrecked)
            return;

        if (!seat.TryOccupy())
            return;

        currentCarController = car;
        if (car != null && car.VehicleTurret != null)
            lastDrivenTurret = car.VehicleTurret;

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

        // Reset camera roll before disabling the player controller.
        if (playerController != null && playerCamera != null)
        {
            playerController.ResetMouseLookStrafeTilt(playerCamera.transform);
        }

        // Stop player movement/collision before teleporting the player root into the vehicle.
        if (playerController != null)
        {
            playerController.enabled = false;
            playerController.SendMessage(
                "ResetMovementState",
                SendMessageOptions.DontRequireReceiver
            );
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

        if (crosshair != null) crosshair.SetActive(false);

        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, true);
    }

    public void ForceEjectFromVehicle(CarController car, Vector3 explosionCentre,
        float horizontalSpeed, float verticalSpeed)
    {
        if (car == null || currentVehicle == null || currentCarController != car)
            return;

        Transform exitPoint = currentVehicle.ExitPoint;
        Vector3 exitPosition = exitPoint != null ? exitPoint.position : transform.position;
        Vector3 outward = Vector3.ProjectOnPlane(exitPosition - explosionCentre, Vector3.up);
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector3.ProjectOnPlane(car.transform.right, Vector3.up);
        if (outward.sqrMagnitude < 0.001f)
            outward = Vector3.right;
        outward.Normalize();

        Rigidbody carBody = car.GetComponent<Rigidbody>();
        Vector3 inheritedVelocity = carBody != null ? carBody.GetPointVelocity(exitPosition) : Vector3.zero;
        // Use horizontal momentum only; give an upward launch even if the car is falling.
        Vector3 launchVelocity = Vector3.ProjectOnPlane(inheritedVelocity, Vector3.up)
            + outward * Mathf.Max(0f, horizontalSpeed)
            + Vector3.up * Mathf.Max(0f, verticalSpeed);

        // ExitVehicle restores parenting, seat occupancy, cameras and movement first.
        ExitVehicle(true);
        if (playerController != null && playerController.enabled)
            playerController.Launch(launchVelocity);
    }

    private void ExitVehicle(bool forced = false)
    {
        if (currentVehicle == null)
            return;

        VehicleSeat vehicle = currentVehicle;
        Transform exitPoint = vehicle.ExitPoint;

        currentVehicle = null;
        nearbyVehicle = null;
        currentCarController = null;

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

        if (forced)
        {
            if (exitPoint == null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
                transform.rotation = forward.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
            }
            PlaceEjectedPlayerOutsideCar(vehicle);
            Physics.SyncTransforms();
        }

        vehicle.Release();

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

        if (crosshair != null) crosshair.SetActive(true);

        SetPromptActive(enterPrompt, false);
        SetPromptActive(exitPrompt, false);
    }

    // Called while the CharacterController is still disabled. Prefer the configured
    // ExitPoint; if it is obstructed, try positions above the car's solid colliders.
    private void PlaceEjectedPlayerOutsideCar(VehicleSeat vehicle)
    {
        Physics.SyncTransforms();
        if (vehicle.ExitPoint != null && IsEjectionPositionClear(transform.position))
            return;

        Transform carRoot = currentCarController != null ? currentCarController.transform : vehicle.transform;
        float roof = carRoot.position.y;
        foreach (Collider col in carRoot.GetComponentsInChildren<Collider>())
        {
            if (col.enabled && !col.isTrigger && !col.transform.IsChildOf(transform))
                roof = Mathf.Max(roof, col.bounds.max.y);
        }

        float feetOffset = 0f;
        if (characterController != null)
        {
            float scaleY = Mathf.Abs(transform.lossyScale.y);
            feetOffset = (characterController.height * 0.5f - characterController.center.y) * scaleY;
        }

        Vector3 candidate = new Vector3(carRoot.position.x, roof + feetOffset + 0.25f, carRoot.position.z);
        Vector3 side = Vector3.ProjectOnPlane(carRoot.right, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.001f) side = Vector3.right;
        for (int i = 0; i < 4; i++)
        {
            Vector3 raised = candidate + Vector3.up * i;
            if (IsEjectionPositionClear(raised)) { transform.position = raised; return; }
            if (IsEjectionPositionClear(raised + side * 2f)) { transform.position = raised + side * 2f; return; }
            if (IsEjectionPositionClear(raised - side * 2f)) { transform.position = raised - side * 2f; return; }
        }

        // No free nearby capsule was found (for example, a cramped tunnel).
        // Fall back to the roof rather than leaving the player attached to the wreck.
        transform.position = candidate;
        Debug.LogWarning("Vehicle ejection: no unobstructed exit position found.", this);
    }

    private bool IsEjectionPositionClear(Vector3 position)
    {
        if (characterController == null)
            return true;

        Vector3 scale = transform.lossyScale;
        float radius = characterController.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfHeight = Mathf.Max(radius, characterController.height * Mathf.Abs(scale.y) * 0.5f);
        float segment = Mathf.Max(0f, halfHeight - radius);
        Vector3 centre = position + transform.TransformVector(characterController.center);
        float queryRadius = Mathf.Max(0.01f, radius - characterController.skinWidth);
        foreach (Collider col in Physics.OverlapCapsule(centre - Vector3.up * segment,
            centre + Vector3.up * segment, queryRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col.transform == transform || col.transform.IsChildOf(transform))
                continue;
            if (Physics.GetIgnoreLayerCollision(gameObject.layer, col.gameObject.layer))
                continue;
            if (Physics.GetIgnoreCollision(characterController, col))
                continue;
            return false;
        }
        return true;
    }

    private void OnDisable()
    {
        markRequested = false;
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
