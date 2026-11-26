using TMPro;
using UnityEngine;

/// <summary>Event-driven presentation of the shared player/vehicle XP progression.</summary>
[DisallowMultipleComponent]
public sealed class PlayerLevelMeter : MonoBehaviour
{
    [SerializeField] private PlayerLevelSystem levelSystem;

    [Header("UI")]
    [SerializeField] private HUDCycleProgress xpProgress;
    [SerializeField] private TextMeshProUGUI bankedLevelsText;
    [Tooltip("Optional percentage of actual XP towards the next level (not the animated fraction).")]
    [SerializeField] private TextMeshProUGUI xpPercentText;

    [Header("Banked Level Appearance")]
    [SerializeField] private bool tintBankedLevels = true;
    [SerializeField] private Color availableColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color emptyColor = new Color32(166, 179, 187, 255);

    [Header("Level-Up Feedback")]
    [Tooltip("Optional dedicated visual transform. Defaults to the banked level text.")]
    [SerializeField] private RectTransform pulseTarget;
    [SerializeField, Min(0f)] private float pulseStrength = 0.15f;
    [SerializeField, Min(0f)] private float pulseDuration = 0.22f;
    [SerializeField] private bool useUnscaledTime;

    private PlayerLevelSystem subscribedSystem;
    private int lastTotal;
    private int lastBanked = -1;
    private int lastPercent = -1;
    private RectTransform activePulseTarget;
    private Vector3 restScale;
    private float pulseRemaining;

    private void Awake()
    {
        if (levelSystem == null)
            levelSystem = GetComponentInParent<PlayerLevelSystem>();
        if (xpProgress == null)
            xpProgress = GetComponentInChildren<HUDCycleProgress>(true);
    }

    private void OnEnable()
    {
        if (levelSystem == null)
        {
            Debug.LogError("PlayerLevelMeter: Assign the player's PlayerLevelSystem.", this);
            return;
        }
        if (xpProgress == null)
            Debug.LogError("PlayerLevelMeter: Assign HUDCycleProgress.", this);

        activePulseTarget = pulseTarget != null ? pulseTarget :
            bankedLevelsText != null ? bankedLevelsText.rectTransform : null;
        if (activePulseTarget != null) restScale = activePulseTarget.localScale;
        pulseRemaining = 0f;
        subscribedSystem = levelSystem;
        subscribedSystem.XPChanged += OnXPChanged;
        subscribedSystem.BankedLevelsChanged += OnBankedLevelsChanged;
        Synchronize();
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(subscribedSystem, null))
        {
            subscribedSystem.XPChanged -= OnXPChanged;
            subscribedSystem.BankedLevelsChanged -= OnBankedLevelsChanged;
        }
        subscribedSystem = null;
        StopPulse();
        activePulseTarget = null;
        // Also cancel pending visuals if this adapter is disabled separately.
        if (xpProgress != null) xpProgress.SnapToValue(xpProgress.TargetValue);
    }

    /// <summary>Call after an external save/reset that does not emit progression events.</summary>
    public void Synchronize()
    {
        if (levelSystem == null) return;
        lastTotal = levelSystem.TotalLevelsEarned;
        lastBanked = -1;
        lastPercent = -1;
        StopPulse();
        if (xpProgress != null) xpProgress.SnapToValue(levelSystem.XPProgressFraction);
        UpdateBanked(false);
        UpdatePercent();
    }

    private void OnXPChanged()
    {
        if (levelSystem == null) return;
        int total = levelSystem.TotalLevelsEarned;
        if (total < lastTotal)
        {
            Synchronize();
            return;
        }
        int completedCycles = total - lastTotal;
        lastTotal = total;
        if (xpProgress != null)
            xpProgress.SetProgress(levelSystem.XPProgressFraction, completedCycles);
        UpdatePercent();
    }

    private void OnBankedLevelsChanged(int ignoredEventValue)
    {
        // Read the actual current count, including any synchronous spending by
        // other event listeners. Never infer earned cycles from banked balance.
        UpdateBanked(true);
    }

    private void UpdateBanked(bool animate)
    {
        if (levelSystem == null) return;
        int count = levelSystem.BankedLevels;
        if (count == lastBanked) return;
        bool increased = lastBanked >= 0 && count > lastBanked;
        lastBanked = count;
        if (bankedLevelsText != null)
        {
            bankedLevelsText.text = count.ToString();
            if (tintBankedLevels)
                bankedLevelsText.color = count > 0 ? availableColor : emptyColor;
        }
        if (animate && increased) StartPulse();
    }

    private void UpdatePercent()
    {
        if (xpPercentText == null || levelSystem == null) return;
        // Floor so 99.9% cannot display 100% before a level is actually earned.
        int percent = Mathf.FloorToInt(levelSystem.XPProgressFraction * 100f);
        if (percent == lastPercent) return;
        lastPercent = percent;
        xpPercentText.SetText("{0:0}%", (float)percent);
    }

    private void StartPulse()
    {
        if (activePulseTarget == null || pulseDuration <= 0f || pulseStrength <= 0f) return;
        pulseRemaining = pulseDuration;
        activePulseTarget.localScale = restScale * (1f + pulseStrength);
    }

    private void Update()
    {
        if (pulseRemaining <= 0f) return;
        if (activePulseTarget == null || pulseDuration <= 0f)
        {
            StopPulse();
            return;
        }
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;
        pulseRemaining = Mathf.Max(0f, pulseRemaining - dt);
        float t = Mathf.Clamp01(pulseRemaining / pulseDuration);
        activePulseTarget.localScale = restScale * (1f + pulseStrength * t * t);
        if (pulseRemaining == 0f) StopPulse();
    }

    private void StopPulse()
    {
        pulseRemaining = 0f;
        if (activePulseTarget != null) activePulseTarget.localScale = restScale;
    }
}
