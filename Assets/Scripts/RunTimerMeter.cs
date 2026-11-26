using TMPro;
using UnityEngine;

/// <summary>Presentation only: reads the authoritative HordeManager clock.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Run Timer Meter")]
public sealed class RunTimerMeter : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Optional. Falls back to HordeManager.Instance if left empty.")]
    [SerializeField] private HordeManager hordeManager;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private HUDArc stopwatch;
    [SerializeField] private HUDPanel panel;
    [SerializeField] private TextMeshProUGUI statusText;
    [Tooltip("Optional stopwatch cap or other static graphic to tint with the arc.")]
    [SerializeField] private UnityEngine.UI.Graphic stopwatchCap;
    [SerializeField] private UnityEngine.UI.Graphic stopwatchStem;

    [Header("Countdown States (seconds)")]
    [SerializeField, Min(0f)] private float warningSeconds = 60f;
    [SerializeField, Min(0f)] private float criticalSeconds = 10f;
    [SerializeField] private bool showWarningStatus = true;
    [SerializeField] private bool showOvertimeStatus = true;

    [Header("Appearance")]
    [SerializeField] private Color normalTextColor = new Color32(244, 246, 247, 255);
    [SerializeField] private Color normalAccentColor = new Color32(255, 180, 56, 255);
    [SerializeField] private Color warningColor = new Color32(255, 208, 113, 255);
    [SerializeField] private Color criticalColor = new Color32(255, 87, 85, 255);
    [SerializeField] private Color unavailableColor = new Color32(154, 168, 177, 255);
    [SerializeField] private bool tintPanelAccent = true;
    [SerializeField] private bool tintStopwatchNeedle = true;
    [Tooltip("Quantizes only the radial display, not the countdown. 180 steps is ample for a small icon.")]
    [SerializeField, Range(30, 720)] private int arcSteps = 180;

    [Header("Final Seconds Pulse")]
    [SerializeField] private bool pulseFinalSeconds = true;
    [SerializeField, Range(0f, 0.2f)] private float pulseStrength = 0.045f;
    [SerializeField, Range(0.01f, 1f)] private float pulseDuration = 0.18f;

    private enum ClockState { Missing, Ready, Normal, Warning, Critical, Overtime, Stopped }
    private ClockState lastState;
    private bool hasState;
    private int lastSeconds;
    private int lastArcStep;
    private int lastArcSteps;
    private HordeManager lastSource;
    private RectTransform pulseTarget;
    private Vector3 baseScale;
    private bool pulseApplied;

    private void OnEnable()
    {
        pulseTarget = timerText != null ? timerText.rectTransform : null;
        if (pulseTarget != null) baseScale = pulseTarget.localScale;
        InvalidateCache();
        RefreshDisplay();
    }

    private void LateUpdate()
    {
        RefreshDisplay();
    }

    private void OnDisable()
    {
        RestoreScale();
    }

    private void InvalidateCache()
    {
        hasState = false;
        lastSeconds = int.MinValue;
        lastArcStep = -1;
        lastArcSteps = -1;
    }

    private void RefreshDisplay()
    {
        // No scene searches: the manager already exposes a singleton.
        HordeManager source = hordeManager != null ? hordeManager : HordeManager.Instance;
        if (lastSource != source)
        {
            lastSource = source;
            InvalidateCache();
            RestoreScale();
        }

        float remaining = 0f;
        float fraction = 0f;
        int seconds = -1;
        ClockState state = ClockState.Missing;
        if (source != null)
        {
            float duration = source.RunDurationSeconds;
            remaining = source.RemainingSeconds;
            if (IsFinite(duration) && duration > 0f && IsFinite(remaining))
            {
                remaining = Mathf.Max(0f, remaining);
                seconds = Mathf.CeilToInt(remaining);
                fraction = Mathf.Clamp01(remaining / duration);
                if (!source.IsRunning)
                    state = source.ElapsedSeconds <= 0f ? ClockState.Ready : ClockState.Stopped;
                else if (remaining <= 0f)
                    state = ClockState.Overtime;
                else if (criticalSeconds > 0f && remaining <= criticalSeconds)
                    state = ClockState.Critical;
                else if (warningSeconds > 0f && remaining <= warningSeconds)
                    state = ClockState.Warning;
                else
                    state = ClockState.Normal;
            }
        }

        if (seconds != lastSeconds)
        {
            lastSeconds = seconds;
            if (timerText != null)
                timerText.text = seconds < 0 ? "--:--" : $"{seconds / 60:00}:{seconds % 60:00}";
        }

        bool stateChanged = !hasState || state != lastState;
        Color accent = GetAccent(state);
        if (stateChanged)
        {
            hasState = true;
            lastState = state;
            if (timerText != null)
                timerText.color = state == ClockState.Normal || state == ClockState.Ready
                    ? normalTextColor : accent;
            if (statusText != null)
            {
                statusText.color = accent;
                statusText.text = GetStatus(state);
            }
            if (tintPanelAccent && panel != null && panel.accentColor != accent)
            {
                panel.accentColor = accent;
                panel.Refresh();
            }
            if (stopwatchCap != null) stopwatchCap.color = accent;
            if (stopwatchStem != null) stopwatchStem.color = accent;
            if (tintStopwatchNeedle && stopwatch != null && stopwatch.needleColor != accent)
            {
                stopwatch.needleColor = accent;
                stopwatch.Refresh();
            }
        }

        // Ceiling keeps a nonempty ring until the timer actually reaches zero.
        // No mesh rebuild every frame for a 15-minute clock.
        int steps = Mathf.Clamp(arcSteps, 30, 720);
        int step = Mathf.Clamp(Mathf.CeilToInt(fraction * steps), 0, steps);
        if (stateChanged || step != lastArcStep || steps != lastArcSteps)
        {
            lastArcStep = step;
            lastArcSteps = steps;
            if (stopwatch != null) stopwatch.SetValue((float)step / steps, accent);
        }

        UpdatePulse(state, remaining, seconds);
    }

    private Color GetAccent(ClockState state)
    {
        switch (state)
        {
            case ClockState.Warning: return warningColor;
            case ClockState.Critical:
            case ClockState.Overtime: return criticalColor;
            case ClockState.Missing:
            case ClockState.Stopped: return unavailableColor;
            default: return normalAccentColor;
        }
    }

    private string GetStatus(ClockState state)
    {
        switch (state)
        {
            case ClockState.Warning:
            case ClockState.Critical: return showWarningStatus ? "TIME LOW" : string.Empty;
            case ClockState.Overtime: return showOvertimeStatus ? "OVERTIME" : string.Empty;
            case ClockState.Stopped: return "STOPPED";
            default: return string.Empty;
        }
    }

    private void UpdatePulse(ClockState state, float remaining, int seconds)
    {
        if (!pulseFinalSeconds || pulseTarget == null || state != ClockState.Critical || seconds <= 0)
        {
            RestoreScale();
            return;
        }
        // Phase comes from gameplay time, not an independent tween/clock.
        // A paused run freezes the phase; after zero the scale is restored.
        float phase = Mathf.Clamp01((seconds - remaining) / Mathf.Max(0.01f, pulseDuration));
        if (phase >= 1f || pulseStrength <= 0f)
        {
            RestoreScale();
            return;
        }
        float envelope = (1f - phase) * (1f - phase);
        Vector3 scale = baseScale * (1f + Mathf.Clamp(pulseStrength, 0f, 0.2f) * envelope);
        if (pulseTarget.localScale != scale) pulseTarget.localScale = scale;
        pulseApplied = true;
    }

    private void RestoreScale()
    {
        if (pulseApplied && pulseTarget != null) pulseTarget.localScale = baseScale;
        pulseApplied = false;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
