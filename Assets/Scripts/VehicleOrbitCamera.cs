using UnityEngine;

/// <summary>
/// Third-person orbit camera for a vehicle.
/// Attach this script to the Camera and assign the vehicle root to Target.
/// Uses Unity's legacy Input Manager, like PrometeoCarController.
/// </summary>
[DisallowMultipleComponent]
public class VehicleOrbitCamera : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The vehicle transform the camera follows.")]
    public Transform target;

    [Tooltip("Look-at point relative to the target. Increase Y to look higher on the vehicle.")]
    public Vector3 focusOffset = new Vector3(0f, 1.4f, 0f);

    [Header("Orbit")]
    [Tooltip("When enabled, derive the initial orbit angles and distance from the Camera's scene position. When disabled, start behind the vehicle using the values below.")]
    public bool initializeFromCurrentPosition = false;
    [Tooltip("Distance from the focus point to the camera.")]
    [Min(0.1f)] public float distance = 6.5f;
    [Min(0.1f)] public float minDistance = 3f;
    [Min(0.1f)] public float maxDistance = 12f;
    [Tooltip("Mouse movement sensitivity. Increase this if orbiting feels too slow.")]
    [Min(0.01f)] public float mouseSensitivity = 3f;
    [Tooltip("Lowest camera elevation angle, in degrees.")]
    [Range(-30f, 20f)] public float minPitch = -10f;
    [Tooltip("Highest camera elevation angle, in degrees.")]
    [Range(20f, 85f)] public float maxPitch = 65f;
    public bool invertY = false;

    [Header("Zoom")]
    [Tooltip("Distance changed per mouse-wheel step.")]
    [Min(0.1f)] public float zoomSpeed = 2f;

    [Header("Smoothing")]
    [Tooltip("Lower values make position follow more tightly. Set to 0 for no position smoothing.")]
    [Min(0f)] public float positionSmoothTime = 0.06f;
    [Tooltip("How quickly the camera turns to look at the focus point.")]
    [Min(0f)] public float rotationSmoothSpeed = 12f;

    [Header("Camera Collision")]
    public bool avoidObstacles = true;
    [Min(0.01f)] public float collisionRadius = 0.25f;
    [Min(0f)] public float collisionPadding = 0.12f;
    public LayerMask collisionLayers = Physics.DefaultRaycastLayers;

    [Header("Mouse / Cursor")]
    [Tooltip("Lock and hide the cursor for standard in-game mouse camera controls.")]
    public bool lockCursor = true;
    [Tooltip("Press Escape to unlock the cursor. Press Escape again to lock it.")]
    public bool escapeTogglesCursor = true;
    [Tooltip("When cursor locking is disabled, require holding the right mouse button to orbit.")]
    public bool rotateOnlyWhileRightMouseHeld = true;

    private float yaw;
    private float pitch;
    private Vector3 positionSmoothVelocity;
    private bool initialized;

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("VehicleOrbitCamera: Assign the vehicle Transform to Target.", this);
            enabled = false;
            return;
        }

        InitializeOrbitFromCurrentPosition();

        if (lockCursor)
            SetCursorLocked(true);
    }

    private void Update()
    {
        if (target == null)
            return;

        if (escapeTogglesCursor && Input.GetKeyDown(KeyCode.Escape))
        {
            bool shouldLock = Cursor.lockState != CursorLockMode.Locked;
            SetCursorLocked(shouldLock);
        }

        bool canReadMouse = lockCursor
            ? Cursor.lockState == CursorLockMode.Locked
            : (!rotateOnlyWhileRightMouseHeld || Input.GetMouseButton(1));

        if (!canReadMouse)
            return;

        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;

        float mouseY = Input.GetAxis("Mouse Y");
        pitch += (invertY ? mouseY : -mouseY) * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance = Mathf.Clamp(distance - scroll * zoomSpeed, minDistance, maxDistance);
        }
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        if (!initialized)
            InitializeOrbitFromCurrentPosition();

        Vector3 focusPoint = target.TransformPoint(focusOffset);
        Vector3 orbitDirection = GetOrbitDirection();
        float actualDistance = Mathf.Clamp(distance, minDistance, maxDistance);
        bool obstructed = false;

        if (avoidObstacles)
        {
            RaycastHit[] hits = Physics.SphereCastAll(
                focusPoint,
                collisionRadius,
                orbitDirection,
                actualDistance,
                collisionLayers,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = actualDistance;
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].transform;

                // Ignore the vehicle itself, including colliders on its child objects.
                if (hitTransform == target || hitTransform.IsChildOf(target))
                    continue;

                if (hits[i].distance < nearestDistance)
                {
                    nearestDistance = hits[i].distance;
                    obstructed = true;
                }
            }

            if (obstructed)
                actualDistance = Mathf.Max(0.1f, nearestDistance - collisionPadding);
        }

        Vector3 desiredPosition = focusPoint + orbitDirection * actualDistance;

        // Snap inward when an obstacle blocks the camera to reduce wall clipping.
        // Otherwise smoothly follow the moving vehicle.
        if (obstructed || positionSmoothTime <= 0f)
        {
            transform.position = desiredPosition;
            positionSmoothVelocity = Vector3.zero;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref positionSmoothVelocity,
                positionSmoothTime);
        }

        Vector3 lookDirection = focusPoint - transform.position;
        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            if (rotationSmoothSpeed <= 0f)
                transform.rotation = desiredRotation;
            else
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    desiredRotation,
                    1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));
        }
    }

    private Vector3 GetOrbitDirection()
    {
        float yawRadians = yaw * Mathf.Deg2Rad;
        float pitchRadians = pitch * Mathf.Deg2Rad;
        float horizontalScale = Mathf.Cos(pitchRadians);

        return new Vector3(
            Mathf.Sin(yawRadians) * horizontalScale,
            Mathf.Sin(pitchRadians),
            Mathf.Cos(yawRadians) * horizontalScale).normalized;
    }

    private void InitializeOrbitFromCurrentPosition()
    {
        if (initializeFromCurrentPosition)
        {
            Vector3 focusPoint = target.TransformPoint(focusOffset);
            Vector3 offset = transform.position - focusPoint;

            if (offset.sqrMagnitude > 0.001f)
            {
                distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);
                Vector3 direction = offset.normalized;
                yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            else
            {
                yaw = target.eulerAngles.y + 180f;
                pitch = Mathf.Clamp(15f, minPitch, maxPitch);
            }
        }
        else
        {
            // Assume the vehicle's forward direction is its local +Z axis.
            // Starting at yaw + 180 degrees places the camera behind it.
            yaw = target.eulerAngles.y + 180f;
            pitch = Mathf.Clamp(15f, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        initialized = true;
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnDisable()
    {
        // Avoid leaving the cursor hidden if this component is disabled at runtime.
        if (lockCursor)
            SetCursorLocked(false);
    }
}
