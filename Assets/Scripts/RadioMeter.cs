using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation only. PlayerRadio owns charge, selection, placement and cooldowns.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Radio Meter")]
public sealed class RadioMeter : MonoBehaviour
{
    [Serializable]
    private sealed class AbilityCard
    {
        public HUDPanel panel;
        public Graphic icon;
        public TMP_Text title;
        public TMP_Text stateText;
        [Tooltip("Optional pulse target; use an icon or inner content, not a layout-driven panel.")]
        public RectTransform pulseTarget;
    }

    [Header("Source")]
    [SerializeField] private PlayerRadio radio;

    [Header("Persistent status")]
    [SerializeField] private HUDPanel statusPanel;
    [SerializeField] private HUDArc cooldownRing;
    [SerializeField] private Graphic radioIcon;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text keyOrTimeText;
    [SerializeField] private RectTransform readyPulseTarget;

    [Header("Charge selector (child of an always-active controller)")]
    [SerializeField] private GameObject selectorRoot;
    [SerializeField] private HUDProgressBar chargeBar;
    [SerializeField] private TMP_Text chargePercentText;
    [SerializeField] private TMP_Text selectorHint;
    [SerializeField] private AbilityCard reposition = new AbilityCard();
    [SerializeField] private AbilityCard respawn = new AbilityCard();
    [SerializeField] private AbilityCard airstrike = new AbilityCard();

    [Header("Placement prompt (separate child)")]
    [SerializeField] private GameObject placementRoot;
    [SerializeField] private HUDPanel placementPanel;
    [SerializeField] private TMP_Text placementTitle;
    [SerializeField] private TMP_Text placementStatus;
    [SerializeField] private TMP_Text placementControls;

    [Header("Colors")]
    [SerializeField] private Color readyColor = new Color32(112, 231, 237, 255);
    [SerializeField] private Color respawnColor = new Color32(117, 239, 190, 255);
    [SerializeField] private Color airstrikeColor = new Color32(255, 98, 107, 255);
    [SerializeField] private Color cooldownColor = new Color32(255, 180, 56, 255);
    [SerializeField] private Color mutedColor = new Color32(132, 148, 160, 255);
    [SerializeField] private Color normalTextColor = new Color32(244, 246, 247, 255);
    [SerializeField] private Color idleCardFill = new Color32(30, 42, 51, 225);
    [SerializeField] private Color selectedCardFill = new Color32(37, 66, 75, 240);
    [SerializeField] private Color idleBorder = new Color32(103, 120, 132, 255);
    [Header("Feedback")]
    [SerializeField, Range(24, 360)] private int cooldownSteps = 120;
    [SerializeField, Range(30, 600)] private int chargeSteps = 240;
    [SerializeField, Range(0f, 0.25f)] private float pulseStrength = 0.09f;
    [SerializeField, Min(0.01f)] private float pulseDuration = 0.20f;

    [Header("Audio Feedback (optional)")]
    [SerializeField] private bool playThresholdSounds = false;
    [SerializeField] private AudioClip repositionThresholdSound;
    [SerializeField] private AudioClip respawnThresholdSound;
    [SerializeField] private AudioClip airstrikeThresholdSound;
    [SerializeField] private AudioClip cooldownReadySound;
    [SerializeField, Range(0f, 1f)] private float thresholdSoundVolume = 0.3f;
    [SerializeField, Range(0f, 1f)] private float readySoundVolume = 0.3f;

    private enum Status { Missing, Ready, Active, Cooldown, Vehicle }
    private bool initialized, lastSelector, lastPlacement, lastValidPlacement;
    private PlayerRadio lastSource;
    private Status lastStatus;
    private PlayerRadio.RadioState lastState;
    private PlayerRadio.CallType lastPreview, lastSelected;
    private int lastSeconds = -1, lastChargeStep = -1, lastChargeSteps = -1, lastPercent = -1;
    private int lastCooldownStep = -1, lastCooldownSteps = -1;
    private RectTransform pulseTarget;
    private Vector3 pulseBaseScale;
    private float pulseLeft;

    private void OnEnable()
    {
        initialized = false;
        RestorePulse();
    }

    private void Start()
    {
        if (radio == null) Debug.LogWarning("RadioMeter: Assign PlayerRadio.", this);
        ValidateRoot(selectorRoot, "Selector Root");
        ValidateRoot(placementRoot, "Placement Root");
        if (selectorRoot != null && placementRoot != null &&
            (selectorRoot.transform.IsChildOf(placementRoot.transform) || placementRoot.transform.IsChildOf(selectorRoot.transform)))
            Debug.LogWarning("RadioMeter: Selector and placement roots must be separate siblings.", this);
    }

    private void OnDisable()
    {
        RestorePulse();
        SetVisible(selectorRoot, false);
        SetVisible(placementRoot, false);
        initialized = false;
    }

    private bool SafeRoot(GameObject root) => root != null && !transform.IsChildOf(root.transform);
    private void ValidateRoot(GameObject root, string label)
    {
        if (root != null && !SafeRoot(root))
            Debug.LogWarning("RadioMeter: " + label + " cannot contain the meter itself.", this);
    }
    private void SetVisible(GameObject root, bool visible)
    {
        if (SafeRoot(root) && root.activeSelf != visible) root.SetActive(visible);
    }

    private void LateUpdate()
    {
        bool available = radio != null && radio.isActiveAndEnabled && radio.playerStats != null;
        var state = available ? radio.State : PlayerRadio.RadioState.WeaponReady;
        var preview = available ? radio.PreviewCall : PlayerRadio.CallType.None;
        var selected = available ? radio.SelectedCall : PlayerRadio.CallType.None;
        float remaining = available ? Mathf.Max(0f, radio.CooldownRemaining) : 0f;
        bool equipped = available && state != PlayerRadio.RadioState.WeaponReady;
        bool selector = available && (state == PlayerRadio.RadioState.Opening || state == PlayerRadio.RadioState.Charging);
        bool placing = available && state == PlayerRadio.RadioState.Placing;
        bool validPlacement = placing && radio.HasValidPlacement;
        Status status = !available ? Status.Missing : remaining > 0f ? Status.Cooldown :
            equipped ? Status.Active : radio.IsInVehicle ? Status.Vehicle : Status.Ready;
        bool first = !initialized || lastSource != radio;
        if (first) RestorePulse();
        SetVisible(selectorRoot, selector);
        SetVisible(placementRoot, placing);

        Color statusColor = status == Status.Cooldown ? cooldownColor :
            status == Status.Ready || status == Status.Active ? readyColor : mutedColor;
        int seconds = Mathf.CeilToInt(remaining);
        if (first || status != lastStatus)
        {
            SetText(statusText, status == Status.Ready ? "READY" : status == Status.Cooldown ? "RECHARGING" :
                status == Status.Active ? "ACTIVE" : status == Status.Vehicle ? "IN VEHICLE" : "OFFLINE");
            Tint(statusText, statusColor);
            Tint(radioIcon, statusColor);
            Tint(keyOrTimeText, statusColor);
            SetAccent(statusPanel, statusColor);
        }
        if (first || status != lastStatus || seconds != lastSeconds)
        {
            SetText(keyOrTimeText, status == Status.Cooldown ? seconds + "s" :
                status == Status.Ready ? "TAB" : status == Status.Active ? "ON" : "—");
        }
        int ringSteps = Mathf.Max(1, cooldownSteps);
        float fraction = status == Status.Cooldown && radio.CooldownDuration > 0f ?
            Mathf.Clamp01(1f - remaining / radio.CooldownDuration) : status == Status.Missing ? 0f : 1f;
        int ringStep = Mathf.Clamp(Mathf.FloorToInt(fraction * ringSteps), 0, ringSteps);
        if (cooldownRing != null && (first || ringStep != lastCooldownStep || ringSteps != lastCooldownSteps || status != lastStatus))
            cooldownRing.SetValue((float)ringStep / ringSteps, statusColor);
        if (!first && lastStatus == Status.Cooldown && status == Status.Ready)
        {
            StartPulse(readyPulseTarget);
        }
        // Cooldown may expire while the player is in the vehicle.
        if (!first && lastStatus == Status.Cooldown && available && remaining <= 0f)
        {
            if (cooldownReadySound != null && readySoundVolume > 0f)
                AudioManager.Play2D(cooldownReadySound, readySoundVolume);
        }

        if (selector)
        {
            float charge = radio.Charge01;
            int steps = Mathf.Max(1, chargeSteps);
            // Floor prevents premature threshold visuals; pin to exact thresholds when crossed.
            float shown = Mathf.Floor(charge * steps) / steps;
            if (preview == PlayerRadio.CallType.RepositionCar) shown = Mathf.Max(shown, 1f / 3f);
            if (preview == PlayerRadio.CallType.RespawnCar) shown = Mathf.Max(shown, 2f / 3f);
            if (preview == PlayerRadio.CallType.Airstrike) shown = 1f;
            int step = Mathf.FloorToInt(charge * steps);
            int percent = preview == PlayerRadio.CallType.Airstrike ? 100 : Mathf.FloorToInt(charge * 100f);
            bool tierChanged = first || !lastSelector || preview != lastPreview || state != lastState;
            if (chargeBar != null)
            {
                if (tierChanged)
                {
                    chargeBar.fillColor = CallColor(preview);
                    chargeBar.Refresh();
                }
                if (tierChanged || step != lastChargeStep || steps != lastChargeSteps)
                    chargeBar.SnapToValue(shown);
            }
            if (first || !lastSelector || percent != lastPercent)
                SetText(chargePercentText, percent + "%");
            if (tierChanged)
            {
                DrawCard(reposition, PlayerRadio.CallType.RepositionCar, preview, "REPOSITION", "");
                DrawCard(respawn, PlayerRadio.CallType.RespawnCar, preview, "RESPAWN", "");
                DrawCard(airstrike, PlayerRadio.CallType.Airstrike, preview, "AIRSTRIKE", "");
                SetText(selectorHint, state == PlayerRadio.RadioState.Opening ? "CONNECTING…" :
                    preview == PlayerRadio.CallType.None ? "HOLD TAB TO CHARGE  ·  RELEASE TO CANCEL" :
                    "RELEASE TAB TO SELECT  ·  C TO CANCEL");
                if (!first && lastSelector && preview != lastPreview && preview != PlayerRadio.CallType.None)
                {
                    AbilityCard card = preview == PlayerRadio.CallType.RepositionCar ? reposition :
                        preview == PlayerRadio.CallType.RespawnCar ? respawn : airstrike;
                    if (card != null) StartPulse(card.pulseTarget);
                    if (state == PlayerRadio.RadioState.Charging &&
                        (int)preview > (int)lastPreview)
                        PlayThresholdSound(preview);
                }
            }
            lastChargeStep = step; lastChargeSteps = steps; lastPercent = percent;
        }
        else if (lastSelector) RestorePulse();

        if (placing && (first || !lastPlacement || selected != lastSelected || validPlacement != lastValidPlacement))
        {
            Color c = validPlacement ? CallColor(selected) : airstrikeColor;
            SetText(placementTitle, CallName(selected));
            SetText(placementStatus, validPlacement ? "TARGET VALID" : "NO VALID TARGET");
            Tint(placementStatus, c);
            SetAccent(placementPanel, c);
            SetText(placementControls, selected == PlayerRadio.CallType.Airstrike ?
                "HOLD MOUSE2 TO ROTATE  /  C TO CANCEL" :
                "C TO CANCEL");
        }
        TickPulse();
        initialized = true; lastSource = radio; lastStatus = status; lastState = state;
        lastSeconds = seconds; lastCooldownStep = ringStep; lastCooldownSteps = ringSteps;
        lastSelector = selector; lastPlacement = placing; lastPreview = preview;
        lastSelected = selected; lastValidPlacement = validPlacement;
    }

    private void PlayThresholdSound(PlayerRadio.CallType type)
    {
        if (!playThresholdSounds || thresholdSoundVolume <= 0f) return;
        AudioClip clip;
        switch (type)
        {
            case PlayerRadio.CallType.RepositionCar: clip = repositionThresholdSound; break;
            case PlayerRadio.CallType.RespawnCar: clip = respawnThresholdSound; break;
            case PlayerRadio.CallType.Airstrike: clip = airstrikeThresholdSound; break;
            default: return;
        }
        if (clip != null) AudioManager.Play2D(clip, thresholdSoundVolume);
    }

    private Color CallColor(PlayerRadio.CallType type)
    {
        switch (type)
        {
            case PlayerRadio.CallType.RepositionCar: return readyColor;
            case PlayerRadio.CallType.RespawnCar: return respawnColor;
            case PlayerRadio.CallType.Airstrike: return airstrikeColor;
            default: return mutedColor;
        }
    }
    private static string CallName(PlayerRadio.CallType type)
    {
        switch (type)
        {
            case PlayerRadio.CallType.RepositionCar: return "REPOSITION CAR";
            case PlayerRadio.CallType.RespawnCar: return "RESPAWN CAR";
            case PlayerRadio.CallType.Airstrike: return "AIRSTRIKE";
            default: return "TACTICAL SUPPORT";
        }
    }
    private void DrawCard(AbilityCard card, PlayerRadio.CallType type, PlayerRadio.CallType preview, string label, string threshold)
    {
        if (card == null) return;
        bool selected = type == preview;
        bool passed = (int)preview > (int)type;
        Color c = selected ? CallColor(type) : passed ? normalTextColor : mutedColor;
        if (card.panel != null)
        {
            card.panel.fillColor = selected ? selectedCardFill : idleCardFill;
            card.panel.borderColor = selected ? c : idleBorder;
            card.panel.accentColor = selected ? c : idleBorder;
            card.panel.Refresh();
        }
        SetText(card.title, label);
        Tint(card.title, c); Tint(card.icon, c);
        SetText(card.stateText, selected ? "SELECTED" : passed ? "" : threshold);
        Tint(card.stateText, c);
    }
    private static void SetText(TMP_Text t, string value) { if (t != null) t.text = value; }
    private static void Tint(Graphic g, Color c) { if (g != null && g.color != c) g.color = c; }
    private static void SetAccent(HUDPanel p, Color c)
    {
        if (p != null && p.accentColor != c) { p.accentColor = c; p.Refresh(); }
    }
    private void StartPulse(RectTransform target)
    {
        RestorePulse();
        if (target == null || pulseStrength <= 0f) return;
        pulseTarget = target; pulseBaseScale = target.localScale;
        pulseLeft = Mathf.Max(0.01f, pulseDuration);
    }
    private void TickPulse()
    {
        if (pulseTarget == null) return;
        float t = Mathf.Clamp01(pulseLeft / Mathf.Max(0.01f, pulseDuration));
        pulseTarget.localScale = pulseBaseScale * (1f + pulseStrength * t * t);
        pulseLeft -= Time.deltaTime;
        if (pulseLeft <= 0f) RestorePulse();
    }
    private void RestorePulse()
    {
        if (pulseTarget != null) pulseTarget.localScale = pulseBaseScale;
        pulseTarget = null; pulseLeft = 0f;
    }
}
