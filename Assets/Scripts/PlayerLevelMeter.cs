
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerLevelMeter : MonoBehaviour
{
    [SerializeField] private PlayerLevelSystem levelSystem;

    [Header("UI")]
    [SerializeField] private Image xpMeterImage;
    [SerializeField] private TextMeshProUGUI bankedLevelsText;

    [Header("Animation")]
    [SerializeField, Min(0f)]
    private float fillLerpSpeed = 10f;

    private float displayedFill;

    private void Awake()
    {
        if (levelSystem == null)
            levelSystem = GetComponentInParent<PlayerLevelSystem>();
    }

    private void Start()
    {
        if (levelSystem == null)
        {
            Debug.LogError(
                "PlayerLevelMeter: No PlayerLevelSystem assigned.",
                this
            );

            enabled = false;
            return;
        }

        displayedFill = levelSystem.XPProgressFraction;

        UpdateVisuals(true);
    }

    private void Update()
    {
        if (levelSystem == null)
            return;

        displayedFill = Mathf.Lerp(
            displayedFill,
            levelSystem.XPProgressFraction,
            Time.deltaTime * fillLerpSpeed
        );

        UpdateVisuals(false);
    }

    private void UpdateVisuals(bool immediate)
    {
        if (xpMeterImage != null)
        {
            xpMeterImage.fillAmount = immediate
                ? levelSystem.XPProgressFraction
                : displayedFill;
        }

        if (bankedLevelsText != null)
        {
            // Displays only the number, e.g. 16.
            bankedLevelsText.text =
                levelSystem.BankedLevels.ToString();
        }
    }
}
