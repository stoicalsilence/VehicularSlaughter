using TMPro;
using UnityEngine;

/// <summary>Presentation only. Driven by AmmoHUDSwitcher; never modifies weapon state.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/Ammo Counter Meter")]
public sealed class AmmoCounterMeter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text currentAmmoText;
    [SerializeField] private TMP_Text capacityText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private HUDAmmoPips pips;
    [Tooltip("Optional dedicated child to pulse. Do not assign the panel root or a layout-controlled transform.")]
    [SerializeField] private RectTransform pulseTarget;
    [Header("Display")]
    [SerializeField] private bool padToTwoDigits = true;
    [SerializeField] private bool prefixCapacityWithSlash = true;
    [SerializeField, Range(0f, 1f)] private float lowAmmoThreshold = 0.25f;
    [SerializeField] private Color normalColor = new Color32(244, 246, 247, 255);
    [SerializeField] private Color capacityColor = new Color32(166, 179, 187, 255);
    [SerializeField] private Color lowColor = new Color32(255, 180, 56, 255);
    [SerializeField] private Color emptyColor = new Color32(239, 87, 85, 255);
    [SerializeField] private Color accentColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color offlineColor = new Color32(154, 168, 177, 255);
    [Header("Feedback (optional)")]
    [SerializeField, Range(0f, 0.25f)] private float shotPulse = 0.045f;
    [SerializeField, Range(0f, 0.25f)] private float reloadPulse = 0.09f;
    [SerializeField, Min(0.01f)] private float pulseDuration = 0.13f;

    private enum State { Ready, Low, Empty, Reloading, EMP, Offline, Unavailable }
    private bool hasSample;
    private int previousAmmo, previousCapacity;
    private State previousState;
    private RectTransform cachedTarget;
    private Vector3 restScale = Vector3.one;
    private float pulseRemaining, pulseStrength;

    private void OnEnable()
    {
        cachedTarget = pulseTarget;
        if (cachedTarget != null) restScale = cachedTarget.localScale;
        ResetFeedback();
    }
    private void OnDisable() => ResetFeedback();

    /// <summary>Clear state history on a source change; first sample never animates.</summary>
    public void ResetFeedback()
    {
        hasSample = false;
        pulseRemaining = pulseStrength = 0f;
        if (cachedTarget != null) cachedTarget.localScale = restScale;
    }

    public void Present(int current, int capacity, bool reloading, float reloadProgress,
        bool empDisabled = false, bool offline = false, bool available = true)
    {
        current = Mathf.Max(0, current);
        capacity = Mathf.Max(0, capacity);
        available = available && capacity > 0;
        float fraction = available ? Mathf.Clamp01((float)current / capacity) : 0f;
        State state = !available ? State.Unavailable : empDisabled ? State.EMP : offline ? State.Offline :
            reloading ? State.Reloading : current == 0 ? State.Empty :
            fraction <= lowAmmoThreshold ? State.Low : State.Ready;
        bool operational = available && !offline && !empDisabled;
        bool previousOperational = hasSample && previousState != State.EMP &&
            previousState != State.Offline && previousState != State.Unavailable;
        if (operational && previousOperational && capacity == previousCapacity)
        {
            if (current > previousAmmo && previousState == State.Reloading && !reloading)
                TriggerPulse(reloadPulse);
            else if (current < previousAmmo) TriggerPulse(shotPulse);
        }
        else if (!operational || !previousOperational || capacity != previousCapacity)
        {
            pulseRemaining = pulseStrength = 0f;
        }

        bool stateChanged = !hasSample || previousState != state;
        if (currentAmmoText != null && (!hasSample || current != previousAmmo ||
            (previousState == State.Unavailable) != !available))
            currentAmmoText.text = available ? current.ToString(padToTwoDigits ? "D2" : "D") : "--";
        if (capacityText != null && (!hasSample || capacity != previousCapacity ||
            (previousState == State.Unavailable) != !available))
            capacityText.text = (prefixCapacityWithSlash ? "/ " : "") + (available ? capacity.ToString() : "--");

        Color tint = state == State.Low || state == State.Reloading ? lowColor :
            state == State.Empty ? emptyColor : state == State.Ready ? normalColor : offlineColor;
        if (stateChanged)
        {
            if (currentAmmoText != null) currentAmmoText.color = tint;
            if (capacityText != null) capacityText.color = capacityColor;
            if (statusText != null)
            {
                statusText.color = tint;
                switch (state)
                {
                    case State.Low: statusText.text = "LOW AMMO"; break;
                    case State.Empty: statusText.text = "EMPTY"; break;
                    case State.Reloading: statusText.text = "RELOADING"; break;
                    case State.EMP: statusText.text = "EMP OFFLINE"; break;
                    case State.Offline: statusText.text = ""; break;
                    case State.Unavailable: statusText.text = "NO WEAPON"; break;
                    default: statusText.text = string.Empty; break;
                }
            }
        }
        if (pips != null)
        {
            float shown = !operational ? 0f : reloading ? Mathf.Clamp01(reloadProgress) : fraction;
            Color pipTint = state == State.Low ? lowColor : state == State.Empty ? emptyColor : accentColor;
            pips.SetValue(shown, pipTint);
        }
        previousAmmo = current;
        previousCapacity = capacity;
        previousState = state;
        hasSample = true;
        TickPulse();
    }

    private void TriggerPulse(float strength)
    {
        if (cachedTarget == null || strength <= 0f) return;
        pulseStrength = strength;
        pulseRemaining = Mathf.Max(0.01f, pulseDuration);
    }
    private void TickPulse()
    {
        if (cachedTarget == null) return;
        float t = Mathf.Clamp01(pulseRemaining / Mathf.Max(0.01f, pulseDuration));
        Vector3 desired = restScale * (1f + pulseStrength * t * t);
        if (cachedTarget.localScale != desired) cachedTarget.localScale = desired;
        pulseRemaining = Mathf.Max(0f, pulseRemaining - Time.deltaTime);
    }
}
