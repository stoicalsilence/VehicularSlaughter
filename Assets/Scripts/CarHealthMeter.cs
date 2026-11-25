using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CarHealthMeter : MonoBehaviour
{
    public CarController car;
    [HideInInspector] public CarStats carStats; // Legacy reference; car owns its stats.
    [SerializeField] private HUDProgressBar carHealthBar;
    public TextMeshProUGUI carHealthText;

    [Header("Legacy compatibility (optional)")]
    public Image carHPMeterImage;
    [SerializeField, Min(0f)] private float fillLerpSpeed = 10f;

    private CarController boundSource;
    private HUDProgressBar previousBar;
    private Image previousImage;
    private TextMeshProUGUI previousText;
    private bool initialized;
    private float lastMaxHealth;
    private int lastCurrent, lastMaximum;
    private float displayedFill;

    private void OnEnable()
    {
        initialized = false;
    }

    private void OnDisable()
    {
        Unbind();
        initialized = false;
    }

    private void Unbind()
    {
        if (!ReferenceEquals(boundSource, null))
        {
            boundSource.VehicleHealthChanged -= OnHealthChanged;
            boundSource.VehicleHealthReset -= OnHealthReset;
        }
        boundSource = null;
    }

    private void Update()
    {
        // First Update occurs after normal scene Start initialization. Rebind on
        // runtime source changes, and poll as a safety net for direct field writes.
        if (!ReferenceEquals(boundSource, car))
        {
            Unbind();
            boundSource = car;
            initialized = false;
            if (boundSource != null)
            {
                boundSource.VehicleHealthChanged += OnHealthChanged;
                boundSource.VehicleHealthReset += OnHealthReset;
            }
        }

        if (!(car != null && car.IsVehicleHealthInitialized))
        {
            initialized = false;
            return;
        }

        Sync(car.currentVehicleHealth, car.MaxVehicleHealth, false, Time.deltaTime);
    }

    private void OnHealthChanged(float current, float maximum)
    {
        // Events catch separate hits even when several occur within one frame.
        if (boundSource != car || !(car != null && car.IsVehicleHealthInitialized)) return;
        Sync(current, maximum, false, 0f);
    }

    private void OnHealthReset()
    {
        if (boundSource == car && car != null && car.IsVehicleHealthInitialized)
            Sync(car.currentVehicleHealth, car.MaxVehicleHealth, true, 0f);
    }

    private void Sync(float current, float maximum, bool forceSnap, float dt)
    {
        // Max-health upgrades are not damage, even if the normalized value drops.
        bool immediate = forceSnap || !initialized || previousBar != carHealthBar ||
            previousImage != carHPMeterImage || maximum != lastMaxHealth;
        float fraction = HUDProgressBar.Normalize(current, maximum);
        if (carHealthBar != null)
        {
            carHealthBar.SetValue(fraction, immediate);
        }
        else if (carHPMeterImage != null)
        {
            // Fallback for scenes not yet migrated. Only advance once per frame.
            displayedFill = immediate || fillLerpSpeed <= 0f ? fraction :
                Mathf.Lerp(displayedFill, fraction, 1f - Mathf.Exp(-fillLerpSpeed * dt));
            carHPMeterImage.fillAmount = displayedFill;
        }

        int currentInt = Mathf.CeilToInt(current);
        int maximumInt = Mathf.CeilToInt(maximum);
        if (carHealthText != null && (immediate || previousText != carHealthText ||
            currentInt != lastCurrent || maximumInt != lastMaximum))
            carHealthText.text = $"{currentInt} / {maximumInt}";

        lastCurrent = currentInt;
        lastMaximum = maximumInt;
        lastMaxHealth = maximum;
        previousText = carHealthText;
        previousBar = carHealthBar;
        previousImage = carHPMeterImage;
        initialized = true;
    }
}
