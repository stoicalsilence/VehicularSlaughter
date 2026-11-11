using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CarHealthMeter : MonoBehaviour
{
    public CarController car;
    public CarStats carStats;

    public Image carHPMeterImage;
    public TextMeshProUGUI carHealthText;

    [SerializeField, Min(0f)]
    private float fillLerpSpeed = 10f;

    private float displayedFill;

    private void Start()
    {
        if (car == null)
        {
            Debug.LogError("CarHealthMeter: No CarController assigned.", this);
            enabled = false;
            return;
        }

        displayedFill = GetTargetFill();
        UpdateVisuals(true);
    }

    private void Update()
    {
        if (car == null)
            return;

        displayedFill = Mathf.Lerp(
            displayedFill,
            GetTargetFill(),
            Time.deltaTime * fillLerpSpeed
        );

        UpdateVisuals(false);
    }

    private float GetTargetFill()
    {
        float maxHealth = Mathf.Max(1f, carStats.maxVehicleHealth);

        return Mathf.Clamp01(
            car.currentVehicleHealth / maxHealth
        );
    }

    private void UpdateVisuals(bool immediate)
    {
        if (carHPMeterImage != null)
        {
            carHPMeterImage.fillAmount = immediate
                ? GetTargetFill()
                : displayedFill;
        }

        if (carHealthText != null)
        {
            carHealthText.text =
                $"{Mathf.CeilToInt(car.currentVehicleHealth)} / " +
                $"{Mathf.CeilToInt(carStats.maxVehicleHealth)}";
        }
    }
}