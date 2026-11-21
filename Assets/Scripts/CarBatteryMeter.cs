
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CarBatteryMeter : MonoBehaviour
{
    [Header("References")]
    public CarController car;

    public Image batteryMeterImage;
    public TextMeshProUGUI batteryHealthText;

    [Header("Battery Colors")]
    public Color fullBatteryColor = new Color32(0, 175, 229, 255);
    public Color damagedBatteryColor = new Color32(235, 197, 45, 255);
    public Color emptyBatteryColor = new Color32(102, 108, 116, 255);

    [Header("Animation")]
    [SerializeField, Min(0f)]
    private float colorLerpSpeed = 10f;

    private Color targetColor;

    private void OnEnable()
    {
        if (car == null)
        {
            Debug.LogError(
                "CarBatteryMeter: No CarController assigned.", this);

            enabled = false;
            return;
        }

        car.BatteryHealthChanged += OnBatteryHealthChanged;

        RefreshMeter(car.BatteryHP, car.MaxBatteryHealth);

        if (batteryMeterImage != null)
            batteryMeterImage.color = targetColor;
    }

    private void OnDisable()
    {
        if (car != null)
            car.BatteryHealthChanged -= OnBatteryHealthChanged;
    }

    private void Update()
    {
        if (batteryMeterImage == null)
            return;

        batteryMeterImage.color = colorLerpSpeed > 0f
            ? Color.Lerp(
                batteryMeterImage.color,
                targetColor,
                Time.deltaTime * colorLerpSpeed)
            : targetColor;
    }

    private void OnBatteryHealthChanged(int current, int maximum)
    {
        RefreshMeter(current, maximum);
    }

    private void RefreshMeter(int current, int maximum)
    {
        if (current <= 0)
            targetColor = emptyBatteryColor;
        else if (current < maximum)
            targetColor = damagedBatteryColor;
        else
            targetColor = fullBatteryColor;

        if (batteryHealthText != null)
            batteryHealthText.text = $"{current}/{maximum}";
    }
}
