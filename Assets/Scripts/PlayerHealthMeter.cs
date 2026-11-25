using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerHealthMeter : MonoBehaviour
{
    [SerializeField] private PlayerHealth player;
    [SerializeField] private HUDProgressBar healthBar;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Legacy compatibility (optional)")]
    [SerializeField] private Image healthMeterImage;
    [SerializeField] public TextMeshProUGUI lastDamageTakenText;
    [SerializeField] private bool hideLegacyDamageText = true;
    [SerializeField, Min(0f)] private float fillLerpSpeed = 10f;

    private PlayerHealth boundSource;
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
        if (hideLegacyDamageText && lastDamageTakenText != null)
        {
            lastDamageTakenText.text = string.Empty;
            lastDamageTakenText.enabled = false;
        }
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
            boundSource.HealthChanged -= OnHealthChanged;
        }
        boundSource = null;
    }

    private void Update()
    {
        // First Update occurs after normal scene Start initialization. Rebind on
        // runtime source changes, and poll as a safety net for direct field writes.
        if (!ReferenceEquals(boundSource, player))
        {
            Unbind();
            boundSource = player;
            initialized = false;
            if (boundSource != null)
            {
                boundSource.HealthChanged += OnHealthChanged;
            }
        }

        if (!(player != null && player.playerStats != null))
        {
            initialized = false;
            return;
        }

        Sync(player.CurrentHealth, player.MaxHealth, false, Time.deltaTime);
    }

    private void OnHealthChanged(float current, float maximum)
    {
        // Events catch separate hits even when several occur within one frame.
        if (boundSource != player || !(player != null && player.playerStats != null)) return;
        Sync(current, maximum, false, 0f);
    }

    private void Sync(float current, float maximum, bool forceSnap, float dt)
    {
        // Max-health upgrades are not damage, even if the normalized value drops.
        bool immediate = forceSnap || !initialized || previousBar != healthBar ||
            previousImage != healthMeterImage || maximum != lastMaxHealth;
        float fraction = HUDProgressBar.Normalize(current, maximum);
        if (healthBar != null)
        {
            healthBar.SetValue(fraction, immediate);
        }
        else if (healthMeterImage != null)
        {
            // Fallback for scenes not yet migrated. Only advance once per frame.
            displayedFill = immediate || fillLerpSpeed <= 0f ? fraction :
                Mathf.Lerp(displayedFill, fraction, 1f - Mathf.Exp(-fillLerpSpeed * dt));
            healthMeterImage.fillAmount = displayedFill;
        }

        int currentInt = Mathf.CeilToInt(current);
        int maximumInt = Mathf.CeilToInt(maximum);
        if (healthText != null && (immediate || previousText != healthText ||
            currentInt != lastCurrent || maximumInt != lastMaximum))
            healthText.text = $"{currentInt} / {maximumInt}";

        lastCurrent = currentInt;
        lastMaximum = maximumInt;
        lastMaxHealth = maximum;
        previousText = healthText;
        previousBar = healthBar;
        previousImage = healthMeterImage;
        initialized = true;
    }
}
