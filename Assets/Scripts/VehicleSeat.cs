using UnityEngine;

/// <summary>
/// Vehicle-side configuration for the driver's seat, exit location, and third-person camera.
/// Add this to the same GameObject as CarController, then assign the anchors and camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class VehicleSeat : MonoBehaviour
{
    [Header("Vehicle")]
    [SerializeField] private CarController carController;

    [Header("Entry / Exit Anchors")]
    [Tooltip("A child Transform where the player root should sit while driving. Place it at the driver's seat.")]
    [SerializeField] private Transform driverSeat;
    [Tooltip("A child Transform positioned outside the vehicle where the player's root can safely stand.")]
    [SerializeField] private Transform exitPoint;

    [Header("Vehicle Camera")]
    [Tooltip("VehicleOrbitCamera component attached to the vehicle's third-person camera.")]
    [SerializeField] private VehicleOrbitCamera orbitCamera;
    [Tooltip("Camera component for the third-person vehicle view. Auto-detected from Orbit Camera if left empty.")]
    [SerializeField] private Camera vehicleCamera;
    [Tooltip("AudioListener on the vehicle camera. Auto-detected if left empty.")]
    [SerializeField] private AudioListener vehicleAudioListener;

    public CarController CarController => carController;
    public Transform DriverSeat => driverSeat;
    public Transform ExitPoint => exitPoint;
    public bool IsOccupied { get; private set; }

    /// <summary>Approximate speed based on the Rigidbody, suitable for gating exit while moving.</summary>
    public float SpeedKmh
    {
        get
        {
            if (carController == null)
                return 0f;

            Rigidbody body = carController.GetComponent<Rigidbody>();
            if (body != null)
                return body.velocity.magnitude * 3.6f;

            return Mathf.Abs(carController.carSpeed);
        }
    }

    private void Awake()
    {
        if (carController == null)
            carController = GetComponent<CarController>();

        if (vehicleCamera == null && orbitCamera != null)
            vehicleCamera = orbitCamera.GetComponent<Camera>();

        if (vehicleAudioListener == null && vehicleCamera != null)
            vehicleAudioListener = vehicleCamera.GetComponent<AudioListener>();

        if (carController == null)
            Debug.LogError("VehicleSeat: Assign a CarController or put this component beside one.", this);

        if (driverSeat == null)
            Debug.LogError("VehicleSeat: Assign a Driver Seat Transform.", this);

        if (exitPoint == null)
            Debug.LogError("VehicleSeat: Assign an Exit Point Transform.", this);

        // Cars are not player-driven until a player actually takes the seat.
        if (carController != null)
            carController.SetPlayerDrivingEnabled(false);

        SetVehicleCameraActive(false);
    }

    public bool CanBeEnteredByPlayer =>
        !IsOccupied && carController != null && driverSeat != null && exitPoint != null;

    public bool TryOccupy()
    {
        if (!CanBeEnteredByPlayer)
            return false;

        IsOccupied = true;
        carController.SetPlayerDrivingEnabled(true);
        return true;
    }

    public void Release()
    {
        IsOccupied = false;
        if (carController != null)
            carController.SetPlayerDrivingEnabled(false);
    }

    public void SetVehicleCameraActive(bool active)
    {
        // The camera GameObject should remain active; switch its components to avoid deactivating
        // the whole vehicle-camera hierarchy.
        if (orbitCamera != null)
            orbitCamera.enabled = active;

        if (vehicleCamera != null)
            vehicleCamera.enabled = active;

        if (vehicleAudioListener != null)
            vehicleAudioListener.enabled = active;
    }
}
