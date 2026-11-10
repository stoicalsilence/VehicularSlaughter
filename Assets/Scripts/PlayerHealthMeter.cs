using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerHealthMeter : MonoBehaviour
{
    [SerializeField] private PlayerHealth player;

    [SerializeField] private Image healthMeterImage;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] public TextMeshProUGUI lastDamageTakenText;

    [SerializeField, Min(0f)]
    private float fillLerpSpeed = 10f;

    private float displayedFill;

    private void Start()
    {
        if (player == null)
        {
            Debug.LogError("PlayerHealthMeter: No PlayerHealth assigned.", this);
            enabled = false;
            return;
        }

        displayedFill = player.HealthFraction;

        lastDamageTakenText.text = "";

        UpdateVisuals(true);
    }

    private void Update()
    {
        if (player == null)
            return;

        displayedFill = Mathf.Lerp(
            displayedFill,
            player.HealthFraction,
            Time.deltaTime * fillLerpSpeed
        );

        UpdateVisuals(false);
    }

    private void UpdateVisuals(bool immediate)
    {
        if (healthMeterImage != null)
        {
            healthMeterImage.fillAmount = immediate
                ? player.HealthFraction
                : displayedFill;
        }

        if (healthText != null)
        {
            healthText.text =
                $"{Mathf.CeilToInt(player.CurrentHealth)} / " +
                $"{Mathf.CeilToInt(player.MaxHealth)}";
        }
    }
}