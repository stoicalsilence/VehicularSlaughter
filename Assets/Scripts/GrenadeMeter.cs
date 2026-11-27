using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only. PlayerGrenades owns every charge and recharge timer.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Grenade Meter")]
public sealed class GrenadeMeter : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private PlayerGrenades grenades;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI countText;
    [Tooltip("Optional. If omitted, Count Text displays current / maximum.")]
    [SerializeField] private TextMeshProUGUI capacityText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI keyText;
    [SerializeField] private HUDArc rechargeRing;
    [SerializeField] private HUDPanel panel;
    [SerializeField] private Graphic grenadeIcon;
    [SerializeField] private Graphic keyFrame;
    [Tooltip("Optional. Defaults to Count Text's RectTransform.")]
    [SerializeField] private RectTransform pulseTarget;

    [Header("Appearance")]
    [SerializeField] private Color normalTextColor = new Color32(244, 246, 247, 255);
    [SerializeField] private Color capacityColor = new Color32(166, 179, 187, 255);
    [SerializeField] private Color readyColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color rechargeColor = new Color32(255, 180, 56, 255);
    [SerializeField] private Color emptyColor = new Color32(255, 98, 107, 255);
    [SerializeField] private Color unavailableColor = new Color32(154, 168, 177, 255);
    [SerializeField] private Color unavailableAccent = new Color32(104, 121, 133, 255);
    [SerializeField] private bool tintPanelAccent = true;
    [SerializeField] private bool showFullStatus = true;
    [Tooltip("Only the radial display is quantized. 120 steps is sufficient for a small ring.")]
    [SerializeField, Range(24, 360)] private int arcSteps = 120;

    [Header("Recharge Feedback")]
    [SerializeField, Range(0f, 0.3f)] private float rechargePulseStrength = 0.12f;
    [SerializeField, Range(0.01f, 1f)] private float rechargePulseDuration = 0.22f;

    private enum Context { Missing, Disabled, Vehicle, Unavailable, OnFoot }
    private PlayerGrenades lastSource;
    private Context lastContext;
    private int lastCount, lastMax, lastSeconds, lastStep, lastSteps;
    private KeyCode lastKey;
    private bool hasSnapshot, lastShowFull;
    private RectTransform activePulseTarget;
    private Vector3 baseScale;
    private float pulseRemaining;
    private bool pulseApplied;

    private void OnEnable()
    {
        hasSnapshot = false;
        activePulseTarget = pulseTarget != null ? pulseTarget :
            countText != null ? countText.rectTransform : null;
        if (activePulseTarget != null) baseScale = activePulseTarget.localScale;
        pulseRemaining = 0f;
        pulseApplied = false;
    }

    private void Start()
    {
        if (grenades == null)
            Debug.LogWarning("GrenadeMeter: Assign PlayerGrenades in the Inspector.", this);
        if (countText == null)
            Debug.LogWarning("GrenadeMeter: Assign Count Text in the Inspector.", this);
    }

    private void OnDisable()
    {
        RestoreScale();
        pulseRemaining = 0f;
        hasSnapshot = false;
    }

    private void LateUpdate()
    {
        // Sample AFTER gameplay Update; no duplicate timer, scene search or event churn.
        PlayerGrenades source = grenades;
        bool valid = source != null && source.isActiveAndEnabled;
        int max = valid ? Mathf.Max(0, source.MaxGrenades) : 0;
        int count = valid ? Mathf.Clamp(source.CurrentGrenades, 0, max) : 0;
        bool charging = valid && max > 0 && count < max;
        Context context = !valid ? Context.Missing : max == 0 ? Context.Disabled :
            source.IsInVehicle ? Context.Vehicle : source.IsThrowContextAvailable ? Context.OnFoot : Context.Unavailable;
        float fraction = charging ? Sanitize(source.RechargeProgress01) : valid && max > 0 ? 1f : 0f;
        float timeLeft = charging ? source.NextGrenadeInSeconds : 0f;
        int seconds = charging && IsFinite(timeLeft) ? Mathf.CeilToInt(Mathf.Max(0f, timeLeft)) : 0;
        int steps = Mathf.Clamp(arcSteps, 24, 360);
        int step = Mathf.Clamp(Mathf.FloorToInt(fraction * steps), 0, steps);
        KeyCode key = valid ? source.ThrowKey : KeyCode.None;

        bool first = !hasSnapshot || lastSource != source;
        bool stockChanged = first || count != lastCount || max != lastMax;
        bool stateChanged = first || context != lastContext || stockChanged;
        bool statusChanged = stateChanged || seconds != lastSeconds || lastShowFull != showFullStatus;

        if (first)
        {
            RestoreScale();
            pulseRemaining = 0f;
        }
        else if (lastContext != Context.Missing && count > lastCount && context == Context.OnFoot && rechargePulseStrength > 0f)
        {
            pulseRemaining = Mathf.Max(0.01f, rechargePulseDuration);
        }
        if (context != Context.OnFoot || count < lastCount)
        {
            pulseRemaining = 0f;
            RestoreScale();
        }

        if (stockChanged || (stateChanged && (context == Context.Missing || lastContext == Context.Missing)))
        {
            if (countText != null)
                countText.text = !valid ? (capacityText != null ? "--" : "-- / --") :
                    capacityText != null ? count.ToString() : $"{count} / {max}";
            if (capacityText != null)
                capacityText.text = valid ? $"/ {max}" : "/ --";
        }

        if (first || key != lastKey)
        {
            if (keyText != null) keyText.text = key == KeyCode.None ? "--" : key.ToString().ToUpperInvariant();
        }

        bool onFoot = context == Context.OnFoot;
        Color ringColor = context == Context.Missing || context == Context.Disabled ? unavailableAccent :
            charging ? rechargeColor : onFoot ? readyColor : unavailableAccent;
        Color accent = !onFoot ? unavailableAccent : count == 0 ? emptyColor : ringColor;
        if (stateChanged)
        {
            if (countText != null) countText.color = !onFoot ? unavailableColor : count == 0 ? emptyColor : normalTextColor;
            if (capacityText != null) capacityText.color = !onFoot ? unavailableColor : capacityColor;
            if (keyText != null) keyText.color = onFoot ? normalTextColor : unavailableColor;
            if (keyFrame != null) keyFrame.color = onFoot ? capacityColor : unavailableAccent;
            if (grenadeIcon != null) grenadeIcon.color = onFoot ? normalTextColor : unavailableColor;
            if (tintPanelAccent && panel != null && panel.accentColor != accent)
            {
                panel.accentColor = accent;
                panel.Refresh();
            }
        }
        if (statusChanged && statusText != null)
        {
            statusText.text = GetStatus(context, charging, seconds);
            statusText.color = charging ? rechargeColor : onFoot ? readyColor : unavailableColor;
        }
        if (stateChanged || step != lastStep || steps != lastSteps)
        {
            if (rechargeRing != null) rechargeRing.SetValue((float)step / steps, ringColor);
        }

        hasSnapshot = true;
        lastSource = source;
        lastContext = context;
        lastCount = count;
        lastMax = max;
        lastSeconds = seconds;
        lastStep = step;
        lastSteps = steps;
        lastKey = key;
        lastShowFull = showFullStatus;
        UpdatePulse();
    }

    private string GetStatus(Context context, bool charging, int seconds)
    {
        if (context == Context.Missing) return "UNAVAILABLE";
        if (context == Context.Disabled) return "DISABLED";
        string prefix = context == Context.Vehicle ? "VEHICLE" : context == Context.Unavailable ? "UNAVAILABLE" : string.Empty;
        if (charging)
        {
            string next = seconds > 0 ? $"+1 IN {seconds}s" : "RECHARGING";
            return prefix.Length == 0 ? next : prefix + " · " + next;
        }
        return prefix.Length != 0 ? prefix : showFullStatus ? "FULL" : string.Empty;
    }

    private void UpdatePulse()
    {
        if (activePulseTarget == null || pulseRemaining <= 0f || rechargePulseStrength <= 0f)
        {
            RestoreScale();
            return;
        }
        float duration = Mathf.Max(0.01f, rechargePulseDuration);
        float phase = Mathf.Clamp01(pulseRemaining / duration);
        Vector3 scale = baseScale * (1f + Mathf.Clamp(rechargePulseStrength, 0f, 0.3f) * phase * phase);
        if (activePulseTarget.localScale != scale) activePulseTarget.localScale = scale;
        pulseApplied = true;
        pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.deltaTime);
    }

    private void RestoreScale()
    {
        if (pulseApplied && activePulseTarget != null) activePulseTarget.localScale = baseScale;
        pulseApplied = false;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static float Sanitize(float value) => IsFinite(value) ? Mathf.Clamp01(value) : 0f;
}
