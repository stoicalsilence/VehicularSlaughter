using TMPro;
using UnityEngine;

/// <summary>Tracks the occupied or last driven vehicle. Never changes vehicle physics.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/Speedometer Meter")]
[DefaultExecutionOrder(200)]
public sealed class SpeedometerMeter : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;
    [Tooltip("Initial vehicle to track before the first entry, or standalone source if no interactor is assigned.")]
    [SerializeField] private CarController standaloneCar;
    [Header("References")]
    [Tooltip("Child panel containing all visuals. Required for Hide On Foot. Never assign this component's own object or an ancestor.")]
    [SerializeField] private GameObject speedometerRoot;
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private HUDArc gauge;
    [SerializeField] private TextMeshProUGUI statusText;
    [Header("Display")]
    [Tooltip("Hide the child panel when the assigned player is outside a vehicle. Requires a Vehicle Interactor and a valid child Speedometer Root.")]
    [SerializeField] private bool hideOnFoot;
    [SerializeField, Min(0f)] private float stoppedThresholdKmh = 0.5f;
    [Tooltip("Zero uses the current vehicle's forward maximum. Reverse uses the same scale.")]
    [SerializeField, Min(0f)] private float fixedGaugeMaximumKmh;
    [SerializeField, Min(0f)] private float reverseThresholdKmh = 2f;
    [Header("Needle feedback")]
    [Tooltip("Exponential response rate per second. Zero means immediate. The number is always unsmoothed.")]
    [SerializeField, Min(0f)] private float gaugeResponse = 18f;
    [SerializeField] private bool useUnscaledTime;
    [SerializeField] private bool warnNearMaximum;
    [SerializeField, Range(0f, 1f)] private float warningFraction = 0.9f;
    [SerializeField] private Color normalColor = new Color32(244, 246, 247, 255);
    [SerializeField] private Color accentColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color warningColor = new Color32(255, 180, 56, 255);
    [SerializeField] private Color offlineColor = new Color32(154, 168, 177, 255);

    private CarController lastCar;
    private CarController lastDrivenCar;
    private bool initialized;
    private int lastSpeed = -1;
    private float displayedGauge;
    private float lastGauge = -1f;
    private Color lastGaugeColor;
    private Color lastNumberColor;
    private Color lastStatusColor;
    private string lastStatus;
    private bool warnedInvalidRoot;
    private bool warnedMissingInteractor;

    private void OnEnable()
    {
        initialized = false;
        lastSpeed = -1;
        lastGauge = -1f;
        lastStatus = null;
        warnedInvalidRoot = false;
        warnedMissingInteractor = false;
        // LateUpdate runs after all Awake/Start calls, including CarController's Rigidbody setup.
    }

    private void LateUpdate()
    {
        bool hasInteractor = vehicleInteractor != null;
        bool occupied = hasInteractor && vehicleInteractor.IsInVehicle;
        CarController car;
        if (occupied)
        {
            // Never show another car's speed while occupying a seat without a controller.
            car = vehicleInteractor.currentCarController;
            if (car != null)
                lastDrivenCar = car;
        }
        else
        {
            // The source survives exit/ejection and keeps reading real chassis velocity.
            car = hasInteractor && lastDrivenCar != null ? lastDrivenCar : standaloneCar;
        }

        bool hasCar = car != null;
        // Occupancy, not the existence of a tracked car, controls visibility.
        // Standalone mode has no occupancy information and remains visible.
        bool visible = !hideOnFoot || !hasInteractor || occupied;
        bool rootSafe = speedometerRoot != null && speedometerRoot.transform != transform &&
            speedometerRoot.transform.IsChildOf(transform);

        if (hideOnFoot && !hasInteractor && !warnedMissingInteractor)
        {
            Debug.LogWarning("SpeedometerMeter: Hide On Foot requires a Vehicle Interactor. Standalone mode stays visible.", this);
            warnedMissingInteractor = true;
        }
        if (hideOnFoot && !rootSafe && !warnedInvalidRoot)
        {
            Debug.LogWarning("SpeedometerMeter: Hide On Foot requires Speedometer Root to be a child GameObject containing the visuals. Keep this component on the active parent.", this);
            warnedInvalidRoot = true;
        }

        if (!visible && rootSafe)
        {
            if (speedometerRoot.activeSelf)
                speedometerRoot.SetActive(false);
            initialized = false;
            return;
        }

        bool snap = !initialized || lastCar != car;
        lastCar = car;
        if (rootSafe && !speedometerRoot.activeSelf)
        {
            speedometerRoot.SetActive(true);
            snap = true;
        }

        float speed = hasCar ? car.ActualSpeedKmh : 0f;
        if (float.IsNaN(speed) || float.IsInfinity(speed)) speed = 0f;
        if (speed < Mathf.Max(0f, stoppedThresholdKmh)) speed = 0f;
        float max = fixedGaugeMaximumKmh > 0f ? fixedGaugeMaximumKmh :
            (hasCar ? car.SpeedometerMaximumKmh : 1f);
        float ratio = Mathf.Clamp01(speed / Mathf.Max(0.01f, max));
        bool wrecked = hasCar && car.IsVehicleWrecked;
        bool emp = hasCar && car.IsEmpDisabled;
        bool parked = hasCar && car.IsParkingBrakeEngaged;
        bool reverse = hasCar && speed > 0f && car.SignedForwardSpeedKmh < -Mathf.Max(0f, reverseThresholdKmh);
        bool high = hasCar && warnNearMaximum && ratio >= Mathf.Clamp01(warningFraction) && speed > 0f;

        // Keep the actual speed readable even when power is disabled and the car is coasting.
        string status = wrecked ? "OFFLINE" : emp ? "EMP OFFLINE" : parked ? "PARK" : reverse ? "R" : "";
        Color numberTint = wrecked || emp ? offlineColor : high ? warningColor : normalColor;
        Color gaugeTint = wrecked || emp ? offlineColor : high ? warningColor : accentColor;
        Color statusTint = wrecked || emp || parked ? warningColor : accentColor;
        int rounded = Mathf.RoundToInt(speed);
        if (speedText != null)
        {
            if (snap || rounded != lastSpeed) speedText.SetText("{0:0}", rounded);
            if (snap || lastNumberColor != numberTint) speedText.color = numberTint;
        }
        lastSpeed = rounded;
        lastNumberColor = numberTint;
        if (statusText != null)
        {
            if (snap || lastStatus != status) statusText.text = status;
            if (snap || lastStatusColor != statusTint) statusText.color = statusTint;
        }
        lastStatus = status;
        lastStatusColor = statusTint;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (snap || gaugeResponse <= 0f || speed == 0f)
            displayedGauge = ratio;
        else
        {
            displayedGauge = Mathf.Lerp(displayedGauge, ratio,
                1f - Mathf.Exp(-gaugeResponse * Mathf.Max(0f, dt)));
            if (Mathf.Abs(displayedGauge - ratio) < 0.0005f) displayedGauge = ratio;
        }
        if (gauge != null && (snap || lastGaugeColor != gaugeTint ||
            Mathf.Abs(displayedGauge - lastGauge) >= 0.0005f ||
            (displayedGauge == ratio && lastGauge != ratio)))
        {
            gauge.SetValue(displayedGauge, gaugeTint);
            lastGauge = displayedGauge;
            lastGaugeColor = gaugeTint;
        }
        initialized = true;
    }

    private void OnDisable()
    {
        initialized = false;
        if (gauge != null) gauge.ClearRuntimeTint();
    }
}
