using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using DG.Tweening;

[DisallowMultipleComponent]
public sealed class StatTradeSystem : MonoBehaviour
{
    public AudioClip chooseSound;
    public AudioClip breakCrateSound;
    public AudioClip scarySound;

    [Serializable]
    private abstract class TradeStat
    {
        public bool enabled = true;
        public string displayName;
        public bool allowBonus = true;
        public bool allowPenalty = true;
        [Tooltip("True for stats such as gun spread and reload duration.")]
        public bool lowerIsBetter;
        [Tooltip("Smallest value a trade is allowed to reduce this stat to.")]
        [Min(0f)] public float minimumValue;
        [Tooltip("0 = no additional maximum. Built-in stat limits still apply.")]
        [Min(0f)] public float maximumValue;
        [Tooltip("Scales the bonus and penalty strengths for this stat. 0.5 = half strength.")]
        [Min(0f)] public float strengthMultiplier = 1f;

        [Tooltip("Additional strength multiplier when offered as a bonus.")]
        [Min(0f)] public float bonusStrengthMultiplier = 1f;

        [Tooltip("Additional strength multiplier when offered as a penalty.")]
        [Min(0f)] public float penaltyStrengthMultiplier = 1f;
    }

    [Serializable]
    private sealed class PlayerTradeStat : TradeStat
    {
        public PlayerStatToModify stat;
    }

    [Serializable]
    private sealed class VehicleTradeStat : TradeStat
    {
        public CarStatToModify stat;
    }

    [Serializable]
    private sealed class ChoiceUI
    {
        public Button button;
        public TextMeshProUGUI description;
        public HUDStatTradeCard card;
    }

    private sealed class Change
    {
        public TradeStat definition;
        public float before;
        public float after;
        public float Delta => after - before;
        public bool IsVehicle => definition is VehicleTradeStat;
    }

    private sealed class Offer
    {
        public Change bonus;
        public Change penalty;
    }

    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerLevelRewardSystem levelRewards;
    [Tooltip("Optional HUD object hidden while making a deal.")]
    [SerializeField] private GameObject radioUI;

    [Header("Trade Strength")]
    [Tooltip("Percentage strength, not a fraction. 100 doubles an increase or halves a decrease.")]
    [SerializeField] private Vector2 bonusPercent = new Vector2(100f, 100f);
    [SerializeField] private Vector2 penaltyPercent = new Vector2(100f, 100f);
    [Tooltip("Reject rolls whose actual percentage change is smaller than this after clamping/rounding.")]
    [SerializeField, Range(0.1f, 100f)] private float minimumActualChangePercent = 20f;
    [Tooltip("Allow a player stat to be exchanged for a vehicle stat, and vice versa.")]
    [SerializeField] private bool allowMixedTargets;

    [Header("Stat Pools")]
    [SerializeField] private List<PlayerTradeStat> playerStats = DefaultPlayerStats();
    [SerializeField] private List<VehicleTradeStat> vehicleStats = DefaultVehicleStats();

    [Header("Choice UI")]
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private ChoiceUI optionA = new ChoiceUI();
    [SerializeField] private ChoiceUI optionB = new ChoiceUI();
    [SerializeField] private Color bonusColor = new Color(0.45f, 1f, 0.5f);
    [SerializeField] private Color penaltyColor = new Color(1f, 0.4f, 0.4f);

    [Header("Choice Menu Animation")]
    [SerializeField, Min(0f)] private float menuOpenDuration = 0.3f;
    [SerializeField, Range(0.01f, 1f)] private float menuStartScale = 0.8f;
    [SerializeField] private Ease menuOpenEase = Ease.OutBack;

    [Header("Corrupted Crate Presentation (Optional)")]
    [SerializeField] private HUDStatTradeCard declineCard;
    [SerializeField] private Button declineButton;
    [SerializeField] private RectTransform presentationRoot;
    [SerializeField] private HUDRadialRays curseRays;
    [SerializeField] private HUDParticleBurst revealParticles;
    [SerializeField] private RectTransform revealOrigin;
    [SerializeField] private TMP_Text menuHeading;
    [SerializeField] private Color curseColor = new Color32(180, 125, 232, 255);
    [SerializeField, Range(0f, 1f)] private float curseRayOpacity = .23f;
    [SerializeField, Min(0f)] private float confirmationHold = .28f;
    [SerializeField, Range(0f, .15f)] private float confirmationPunch = .085f;
    [SerializeField, Min(0f)] private float menuCloseDuration = .16f;
    [SerializeField] private AudioClip declineSound;
    [SerializeField] private AudioClip hoverSound;
    [SerializeField, Range(0f, 1f)] private float hoverVolume = .15f;
    [Header("Corruption Atmosphere (v1.1)")]
    [SerializeField] private bool enhancedVisibility = true;
    [SerializeField, Range(0f, 1f)] private float minimumBackgroundOpacity = .84f;
    [SerializeField, Range(0f, 1f)] private float tradeTintOpacity = .065f;
    [SerializeField] private bool ambientCorruption = true;
    [SerializeField, Range(0, 64)] private int ambientEmberCount = 36;
    [SerializeField, Range(0f, 1f)] private float ambientEmberOpacity = .48f;
    [SerializeField, Range(0f, 1f)] private float ambientAuraOpacity = .10f;
    [SerializeField] private bool confirmationShockwave = true;
    [SerializeField, Range(0f, 1f)] private float shockwaveOpacity = .65f;
    [SerializeField, Range(0f, .15f)] private float numberPunch = .065f;
    private HUDCorruptionAtmosphere atmosphere;
    private Color BrightCurse => enhancedVisibility ? Color.Lerp(curseColor, Color.white, .32f) : curseColor;
    private bool inputReady;
    private HUDStatTradeCard highlightedCard;
    private float lastHoverSound = -100f;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>(16);
    private CanvasGroup choiceCanvasGroup;
    private Vector3 choicePanelOpenScale = Vector3.one;
    private Sequence menuSequence;

    private static StatTradeSystem activeMenu;
    public static bool IsAnyOpen => activeMenu != null;
    public bool IsOpen => activeMenu == this;

    private readonly Offer[] offers = new Offer[2];
    private StatTradePickup source;
    private CarStats offeredCar;
    private float previousTimeScale;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private bool previousRadioActive;
    private bool resolving;
    private bool ready;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponentInParent<PlayerInventory>();
        if (inventory == null) inventory = FindObjectOfType<PlayerInventory>();
        if (levelRewards == null) levelRewards = FindObjectOfType<PlayerLevelRewardSystem>();

        if (choicePanel == null || choicePanel == gameObject || transform.IsChildOf(choicePanel.transform) ||
            !ValidChoice(optionA) || !ValidChoice(optionB))
        {
            Debug.LogError("StatTradeSystem: Assign an independent choice panel and both choice buttons/texts or cards.", this);
            return;
        }
        if (optionA.card != null) optionA.card.SetFocusHandler(HighlightCard);
        if (optionB.card != null) optionB.card.SetFocusHandler(HighlightCard);
        if (declineCard != null) declineCard.SetFocusHandler(HighlightCard);
        if (declineButton != null) declineButton.onClick.AddListener(Decline);
        optionA.button.onClick.AddListener(SelectA);
        optionB.button.onClick.AddListener(SelectB);
        choicePanelOpenScale = choicePanel.transform.localScale;

        choiceCanvasGroup = choicePanel.GetComponent<CanvasGroup>();

        if (choiceCanvasGroup == null)
            choiceCanvasGroup = choicePanel.AddComponent<CanvasGroup>();

        choiceCanvasGroup.alpha = 1f;
        choiceCanvasGroup.interactable = true;
        choiceCanvasGroup.blocksRaycasts = true;

        SetupAtmosphere();
        choicePanel.SetActive(false);
        ready = true;
    }

    // Create at most one atmosphere graphic per menu, using the existing installed hierarchy.
    // Legacy text-only menus are intentionally left alone.
    private void SetupAtmosphere()
    {
        if (presentationRoot == null) return;
        var go = new GameObject("CorruptionAtmosphere (Runtime)", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = choicePanel.layer;
        var r = (RectTransform)go.transform; r.SetParent(choicePanel.transform, false);
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        r.SetAsFirstSibling();
        atmosphere = go.AddComponent<HUDCorruptionAtmosphere>();
        atmosphere.raycastTarget = false;
        if (enhancedVisibility)
        {
            var background = choicePanel.GetComponent<Image>();
            if (background != null) { Color c = background.color; c.a = Mathf.Max(c.a, minimumBackgroundOpacity); background.color = c; }
            var subtitle = presentationRoot.Find("Subtitle");
            var hint = presentationRoot.Find("DeclineHint");
            if (subtitle != null && subtitle.TryGetComponent<TMP_Text>(out var st)) st.color = new Color32(218, 204, 233, 255);
            if (hint != null && hint.TryGetComponent<TMP_Text>(out var ht))
            {
                ht.color = new Color32(206, 193, 221, 255);
                if (ht.text == "Leave without making a trade. The crate is spent.") ht.text = "";
                ht.fontSize = Mathf.Max(ht.fontSize, 20f);
            }
        }
        foreach (var card in new[] { optionA.card, optionB.card })
            if (card != null) card.ConfigureEnhancements(enhancedVisibility, tradeTintOpacity, numberPunch);
    }

    private void AnimateMenuOpen()
    {
        menuSequence?.Kill();
        inputReady = false;
        choiceCanvasGroup.alpha = 0f;
        choiceCanvasGroup.interactable = false;
        choiceCanvasGroup.blocksRaycasts = true;
        choicePanel.SetActive(true);
        ResetCard(optionA.card); ResetCard(optionB.card); ResetCard(declineCard);
        if (atmosphere != null) atmosphere.Begin(curseColor, ambientCorruption, ambientEmberCount, ambientAuraOpacity, ambientEmberOpacity, confirmationShockwave);
        bool tactical = presentationRoot != null;
        choicePanel.transform.localScale = tactical ? choicePanelOpenScale : choicePanelOpenScale * menuStartScale;
        float d = Mathf.Max(0f, menuOpenDuration);
        if (d <= 0f) { FinishReveal(); return; }
        menuSequence = DOTween.Sequence().SetUpdate(true);
        menuSequence.Join(choiceCanvasGroup.DOFade(1f, d).SetEase(Ease.OutQuad));
        if (tactical)
        {
            if (optionA.card != null) optionA.card.AddReveal(menuSequence, 0f, d);
            if (optionB.card != null) optionB.card.AddReveal(menuSequence, d * .14f, d * .86f);
            if (declineCard != null) declineCard.AddReveal(menuSequence, d * .25f, d * .75f);
        }
        else menuSequence.Join(choicePanel.transform.DOScale(choicePanelOpenScale, d).SetEase(menuOpenEase));
        menuSequence.OnComplete(() => { menuSequence = null; FinishReveal(); });
    }

    private static void ResetCard(HUDStatTradeCard card) { if (card != null) card.ResetVisual(); }
    private void FinishReveal()
    {
        if (!IsOpen) return;
        choiceCanvasGroup.alpha = 1f;
        choicePanel.transform.localScale = choicePanelOpenScale;
        if (curseRays != null) curseRays.Play(curseColor, curseRayOpacity, .95f, true);
        if (revealParticles != null && revealOrigin != null)
        {
            revealParticles.color = curseColor;
            revealParticles.Emit(revealOrigin, true);
        }
        inputReady = true;
        choiceCanvasGroup.interactable = true;
        // Refresh real mouse focus once. No automatic first-option selection.
        if (EventSystem.current != null)
        {
            pointerHits.Clear();
            // Legacy Input is already used by the game. Guard for projects switching input backends.
#if ENABLE_LEGACY_INPUT_MANAGER
            var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            EventSystem.current.RaycastAll(data, pointerHits);
            if (pointerHits.Count > 0 && pointerHits[0].gameObject != null)
            {
                var card = pointerHits[0].gameObject.GetComponentInParent<HUDStatTradeCard>();
                if (card == optionA.card || card == optionB.card || card == declineCard) HighlightCard(card, true);
            }
            pointerHits.Clear();
#endif
        }
    }
    private void HighlightCard(HUDStatTradeCard card, bool enter)
    {
        if (!IsOpen || !inputReady || resolving || card == null) return;
        if (!enter)
        {
            if (highlightedCard == card) { card.Highlight(false); highlightedCard = null; }
            return;
        }
        if (highlightedCard == card) return;
        if (highlightedCard != null) highlightedCard.Highlight(false);
        highlightedCard = card; card.Highlight(true);
        if (hoverSound != null && Time.unscaledTime - lastHoverSound >= .075f)
        { lastHoverSound = Time.unscaledTime; AudioManager.Play2D(hoverSound, hoverVolume); }
    }
    private bool ValidChoice(ChoiceUI choice) => choice != null && choice.button != null &&
        choice.button.transform.IsChildOf(choicePanel.transform) &&
        ((choice.card != null && choice.card.gameObject == choice.button.gameObject) ||
        (choice.description != null && choice.description.transform.IsChildOf(choicePanel.transform)));

    public bool TryOpen(StatTradePickup pickup)
    {
        if (!ready || !isActiveAndEnabled || pickup == null || inventory == null ||
            IsAnyOpen || inventory.IsOpen || Time.timeScale <= 0f)
            return false;

        offeredCar = inventory.CurrentCarStats;
        if (!RollOffers()) return false;

        source = pickup;
        activeMenu = this;
        previousTimeScale = Time.timeScale;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        if (radioUI != null) previousRadioActive = radioUI.activeSelf;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (radioUI != null) radioUI.SetActive(false);

        try
        {
            BindOffer(optionA, offers[0]);
            BindOffer(optionB, offers[1]);
            if (menuHeading != null) { menuHeading.text = "CORRUPTED CRATE"; menuHeading.color = BrightCurse; }
            highlightedCard = null;
            inputReady = false;
            AudioManager.Play2D(breakCrateSound);
            AudioManager.Play2D(scarySound);
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            AnimateMenuOpen();
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Close(true);
            return false;
        }
    }

    private bool RollOffers()
    {
        var bonuses = new List<Change>();
        var penalties = new List<Change>();
        var seenPlayer = new HashSet<PlayerStatToModify>();
        var seenVehicle = new HashSet<CarStatToModify>();

        foreach (PlayerTradeStat stat in playerStats)
            if (stat != null && stat.enabled && stat.stat != PlayerStatToModify.None && seenPlayer.Add(stat.stat))
                AddCandidates(stat, bonuses, penalties);

        if (offeredCar != null)
            foreach (VehicleTradeStat stat in vehicleStats)
                if (stat != null && stat.enabled && stat.stat != CarStatToModify.None && seenVehicle.Add(stat.stat))
                    AddCandidates(stat, bonuses, penalties);

        var pairs = new List<Offer>();
        foreach (Change bonus in bonuses)
            foreach (Change penalty in penalties)
            {
                if (ReferenceEquals(bonus.definition, penalty.definition)) continue;
                if (!allowMixedTargets && bonus.IsVehicle != penalty.IsVehicle) continue;
                pairs.Add(new Offer { bonus = bonus, penalty = penalty });
            }

        // Require two distinct valid trades. Never show a bonus without a real cost.
        if (pairs.Count < 2) return false;
        int first = UnityEngine.Random.Range(0, pairs.Count);
        offers[0] = pairs[first];
        pairs.RemoveAt(first);
        offers[1] = pairs[UnityEngine.Random.Range(0, pairs.Count)];
        return true;
    }

    private void AddCandidates(TradeStat stat, List<Change> bonuses, List<Change> penalties)
    {
        if (!TryRead(stat, out float current) || !Finite(current) || current <= 0f) return;
        // Percentage changes to zero cannot create a meaningful bonus or penalty.
        if (stat.allowBonus)
        {
            Change change = BuildChange(stat, current, true);
            if (change != null) bonuses.Add(change);
        }
        if (stat.allowPenalty)
        {
            Change change = BuildChange(stat, current, false);
            if (change != null) penalties.Add(change);
        }
    }

    private Change BuildChange(TradeStat stat, float current, bool isBonus)
    {
        Vector2 range = isBonus ? bonusPercent : penaltyPercent;
        float low = Mathf.Max(0f, Mathf.Min(range.x, range.y));
        float high = Mathf.Max(low, Mathf.Max(range.x, range.y));
        float directionMultiplier = isBonus
            ? stat.bonusStrengthMultiplier
            : stat.penaltyStrengthMultiplier;

        float strength = UnityEngine.Random.Range(low, high)
            * stat.strengthMultiplier
            * directionMultiplier / 100f;
        if (!Finite(strength) || strength <= 0f) return null;

        bool increase = isBonus != stat.lowerIsBetter;
        float factor = 1f + strength;
        float next = increase ? current * factor : current / factor;
        if (!Finite(next)) return null;

        GetHardLimits(stat, out float hardMin, out float hardMax, out bool integer);
        float minimum = Mathf.Max(hardMin, stat.minimumValue);
        float maximum = stat.maximumValue > 0f ? Mathf.Min(hardMax, stat.maximumValue) : hardMax;
        if (integer)
        {
            minimum = Mathf.Ceil(minimum);
            if (!float.IsInfinity(maximum)) maximum = Mathf.Floor(maximum);
        }
        if (minimum > maximum) return null;

        // If another upgrade took a stat outside the configured trade limits, only
        // allow movements toward that range, without snapping to the opposite bound.
        next = increase ? Mathf.Min(next, maximum) : Mathf.Max(next, minimum);
        if (integer) next = Mathf.Round(next);
        next = Mathf.Clamp(next, hardMin, hardMax);
        float delta = next - current;
        if (!Finite(next) || (increase ? delta <= 0f : delta >= 0f)) return null;
        if (Mathf.Abs(delta) / current * 100f < minimumActualChangePercent) return null;

        return new Change { definition = stat, before = current, after = next };
    }

    // Mirror the hard bounds/rounding of the supplied PlayerInventory stat setters.
    private static void GetHardLimits(TradeStat entry, out float min, out float max, out bool integer)
    {
        min = 0f;
        max = float.PositiveInfinity;
        integer = false;
        if (entry is PlayerTradeStat p)
        {
            switch (p.stat)
            {
                case PlayerStatToModify.XPGain:
                case PlayerStatToModify.MaxHealth:
                case PlayerStatToModify.GunCriticalDamageMultiplier: min = 1f; break;
                case PlayerStatToModify.GunPelletsPerShot:
                case PlayerStatToModify.GunMagazineSize: min = 1f; integer = true; break;
                case PlayerStatToModify.GunFireRate:
                case PlayerStatToModify.GunReloadDuration:
                case PlayerStatToModify.RadioChargeRate: min = 0.01f; break;
                case PlayerStatToModify.RadioAirstrikeScale: min = 1f; break;
                case PlayerStatToModify.GunMaxShotDistance: min = 0.1f; break;
                case PlayerStatToModify.GunAccuracySpreadDegrees: max = 30f; break;
            }
        }
        else if (entry is VehicleTradeStat v)
        {
            switch (v.stat)
            {
                case CarStatToModify.MaxSpeed:
                case CarStatToModify.MaxReverseSpeed:
                case CarStatToModify.AccelerationMultiplier:
                case CarStatToModify.MaxSteeringAngle:
                case CarStatToModify.BrakeForce:
                case CarStatToModify.DecelerationMultiplier: integer = true; break;
                case CarStatToModify.HandbrakeDriftMultiplier:
                case CarStatToModify.TurretMagazineSize: min = 1f; integer = true; break;
                case CarStatToModify.MaxVehicleHealth:
                case CarStatToModify.MaximumImpactSpeedKmh:
                case CarStatToModify.TurretCriticalDamageMultiplier: min = 1f; break;
                case CarStatToModify.SteeringSpeed:
                case CarStatToModify.TurretFireRate:
                case CarStatToModify.TurretReloadTime: min = 0.01f; break;
                case CarStatToModify.EnemyContactDamageCooldown: min = 0.05f; break;
                case CarStatToModify.TurretMaxShotDistance: min = 0.1f; break;
                case CarStatToModify.TurretAccuracySpreadDegrees: max = 20f; break;
            }
        }
        if (integer) max = Mathf.Min(max, 1000000000f); // keep well within int range
    }

    private bool TryRead(TradeStat stat, out float value)
    {
        value = 0f;
        if (inventory == null) return false;
        if (stat is PlayerTradeStat p)
            return Enum.IsDefined(typeof(PlayerStatToModify), p.stat) && p.stat != PlayerStatToModify.None &&
                   inventory.TryGetPlayerStatValue(p.stat, out value);
        if (stat is VehicleTradeStat v)
            return offeredCar != null && inventory.CurrentCarStats == offeredCar &&
                   Enum.IsDefined(typeof(CarStatToModify), v.stat) && v.stat != CarStatToModify.None &&
                   inventory.TryGetCarStatValue(v.stat, out value);
        return false;
    }

    private bool TryAdd(TradeStat stat, float delta)
    {
        if (inventory == null) return false;
        if (stat is PlayerTradeStat p) return inventory.TryAddPlayerStat(p.stat, delta);
        if (stat is VehicleTradeStat v && offeredCar != null && inventory.CurrentCarStats == offeredCar)
            return inventory.TryAddCarStat(v.stat, delta);
        return false;
    }

    private bool IsCurrent(Change change) => TryRead(change.definition, out float value) &&
        Finite(value) && Mathf.Approximately(value, change.before);

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private string Describe(Offer offer) => FormatChange(offer.bonus, bonusColor) + "\n\n" +
        FormatChange(offer.penalty, penaltyColor);

    private static string FormatChange(Change change, Color color)
    {
        string name = change.definition.displayName;
        if (string.IsNullOrWhiteSpace(name))
            name = change.definition is PlayerTradeStat p ? p.stat.ToString() : ((VehicleTradeStat)change.definition).stat.ToString();
        if (change.IsVehicle) name = "Vehicle: " + name;
        float percent = change.Delta / change.before * 100f;
        string amount = percent.ToString("+0.#;-0.#;0") + "%";
        return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{amount} {name}</b></color>\n" +
               $"{change.before:0.###} -> {change.after:0.###}";
    }

    private void BindOffer(ChoiceUI ui, Offer offer)
    {
        if (ui.card != null)
        {
            BindChange(ui.card, offer.bonus, true);
            BindChange(ui.card, offer.penalty, false);
        }
        else if (ui.description != null)
        { ui.description.richText = true; ui.description.text = Describe(offer); }
    }
    private void BindChange(HUDStatTradeCard card, Change change, bool benefit)
    {
        string name = change.definition.displayName;
        if (string.IsNullOrWhiteSpace(name))
            name = change.definition is PlayerTradeStat p ? p.stat.ToString() : ((VehicleTradeStat)change.definition).stat.ToString();
        string category = change.IsVehicle ? "VEHICLE" : "PLAYER";
        string amount = (change.Delta / change.before * 100f).ToString("+0.#;-0.#;0") + "%";
        Color resultTint = benefit ? bonusColor : penaltyColor;
        string comparison = enhancedVisibility
            ? $"{change.before:0.###} -> <color=#{ColorUtility.ToHtmlStringRGB(resultTint)}>{change.after:0.###}</color>"
            : $"{change.before:0.###} -> {change.after:0.###}";
        if (benefit) card.SetBenefit(category, name, amount, comparison, bonusColor);
        else card.SetCost(category, name, amount, comparison, penaltyColor);
    }

    private void SelectA() => Select(0);
    private void SelectB() => Select(1);

    private void Select(int index)
    {
        if (!IsOpen || resolving || !inputReady) return;
        if (source == null) { Close(true); return; }
        Offer offer = offers[index];
        if (offer == null || !IsCurrent(offer.bonus) || !IsCurrent(offer.penalty))
        {
            Debug.LogWarning("StatTradeSystem: Stats or vehicle changed; trade cancelled without consuming pickup.", this);
            Close(true);
            return;
        }

        resolving = true;
        inputReady = false;
        choiceCanvasGroup.interactable = false;
        try
        {
            // Apply the cost first. Both changes were validated against the visible offer.
            if (!TryAdd(offer.penalty.definition, offer.penalty.Delta)) { Close(true); return; }
            if (!IsCurrent(offer.bonus) || !TryAdd(offer.bonus.definition, offer.bonus.Delta))
            {
                if (!TryAdd(offer.penalty.definition, -offer.penalty.Delta))
                    Debug.LogError("StatTradeSystem: Could not roll back the penalty. Check stat-change callbacks.", this);
                Close(true);
                return;
            }
            AudioManager.Play2D(chooseSound);
            if (source != null) source.CompleteChoice();
            AnimateResolution(index == 0 ? optionA.card : optionB.card, false);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Close(true);
        }
        finally { resolving = false; }
    }

    // Declining spends no stat, XP, item, or banish charge. Consume the pickup to prevent rerolls.
    public void Decline()
    {
        if (!IsOpen || resolving || !inputReady) return;
        resolving = true; inputReady = false; choiceCanvasGroup.interactable = false;
        try
        {
            if (source == null) { Close(true); return; }
            if (declineSound != null) AudioManager.Play2D(declineSound);
            source.CompleteChoice();
            AnimateResolution(declineCard, true);
        }
        catch (Exception ex) { Debug.LogException(ex, this); Close(true); }
        finally { resolving = false; }
    }
    private void AnimateResolution(HUDStatTradeCard chosen, bool declined)
    {
        if (!IsOpen) return;
        if (presentationRoot == null || choicePanel == null || !choicePanel.activeInHierarchy)
        { Close(true); return; }
        menuSequence?.Kill();
        if (highlightedCard != null) highlightedCard.Highlight(false);
        highlightedCard = null;
        if (curseRays != null) curseRays.Clear();
        if (revealParticles != null) revealParticles.Clear();
        if (menuHeading != null)
        { menuHeading.text = declined ? "BARGAIN DECLINED" : "BARGAIN SEALED"; menuHeading.color = declined ? (Color) new Color32(166, 179, 187, 255) : BrightCurse; }
        float hold = declined ? Mathf.Min(.12f, confirmationHold) : Mathf.Max(0f, confirmationHold);
        float close = Mathf.Max(0f, menuCloseDuration);
        if (hold + close <= 0f) { Close(true); return; }
        if (!declined && chosen != null && atmosphere != null)
            atmosphere.Shockwave((RectTransform)chosen.transform, Mathf.Min(.48f, hold + close), shockwaveOpacity);
        menuSequence = DOTween.Sequence().SetUpdate(true);
        if (hold > 0f) menuSequence.AppendInterval(hold);
        if (chosen != null) chosen.AddConfirmation(menuSequence, hold, declined ? .025f : confirmationPunch,
                declined ? (Color) new Color32(166, 179, 187, 255) : curseColor, !declined);
        if (optionA.card != null && optionA.card != chosen) optionA.card.AddDim(menuSequence, hold, .42f);
        if (optionB.card != null && optionB.card != chosen) optionB.card.AddDim(menuSequence, hold, .42f);
        if (declineCard != null && declineCard != chosen) declineCard.AddDim(menuSequence, hold, .42f);
        if (close > 0f) menuSequence.Insert(hold, choiceCanvasGroup.DOFade(0f, close).SetEase(Ease.InQuad));
        menuSequence.OnComplete(() => { menuSequence = null; Close(true); });
    }

    private void Close(bool retryLevels)
    {
        if (!IsOpen) return;
        inputReady = false;
        highlightedCard = null;
        if (EventSystem.current != null && choicePanel != null)
        {
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(choicePanel.transform))
                EventSystem.current.SetSelectedGameObject(null);
        }
        menuSequence?.Kill();
        menuSequence = null;
        if (atmosphere != null) atmosphere.Stop();

        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
            choicePanel.transform.localScale = choicePanelOpenScale;
        }

        if (choiceCanvasGroup != null)
        {
            choiceCanvasGroup.alpha = 1f;
            choiceCanvasGroup.interactable = false;
            choiceCanvasGroup.blocksRaycasts = false;
        }
        ResetCard(optionA.card); ResetCard(optionB.card); ResetCard(declineCard);
        if (radioUI != null) radioUI.SetActive(previousRadioActive);
        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        source = null;
        offeredCar = null;
        Array.Clear(offers, 0, offers.Length);
        activeMenu = null;
        resolving = false;
        if (retryLevels) ResumeLevelRewards();
    }

    private void ResumeLevelRewards()
    {
        if (levelRewards == null) levelRewards = FindObjectOfType<PlayerLevelRewardSystem>();
        if (levelRewards != null && levelRewards.PendingChoices > 0)
            levelRewards.RetryPendingChoices();
    }

    private void OnDisable() => Close(false);

    private void OnDestroy()
    {
        if (!ready) return;
        if (optionA.button != null) optionA.button.onClick.RemoveListener(SelectA);
        if (optionB.button != null) optionB.button.onClick.RemoveListener(SelectB);
        if (declineButton != null) declineButton.onClick.RemoveListener(Decline);
        if (optionA.card != null) optionA.card.SetFocusHandler(null);
        if (optionB.card != null) optionB.card.SetFocusHandler(null);
        if (declineCard != null) declineCard.SetFocusHandler(null);
        Close(false);
    }

    private static PlayerTradeStat P(PlayerStatToModify stat, string name, float min, float max,
        bool lower = false, bool enabled = true, float strength = 1f) => new PlayerTradeStat
        {
            stat = stat,
            displayName = name,
            minimumValue = min,
            maximumValue = max,
            lowerIsBetter = lower,
            enabled = enabled,
            strengthMultiplier = strength
        };

    private static VehicleTradeStat V(CarStatToModify stat, string name, float min, float max,
        bool lower = false, bool enabled = true, float strength = 1f) => new VehicleTradeStat
        {
            stat = stat,
            displayName = name,
            minimumValue = min,
            maximumValue = max,
            lowerIsBetter = lower,
            enabled = enabled,
            strengthMultiplier = strength
        };

    private static List<PlayerTradeStat> DefaultPlayerStats() => new List<PlayerTradeStat>
    {
        P(PlayerStatToModify.GunDamagePerPellet, "Gun Damage", 1, 0),
        P(PlayerStatToModify.GunFireRate, "Gun Fire Rate", 0.5f, 0),
        P(PlayerStatToModify.GunAccuracySpreadDegrees, "Gun Spread", 0.15f, 30, true),
        P(PlayerStatToModify.GunReloadDuration, "Gun Reload Duration", 0.25f, 0, true),
        P(PlayerStatToModify.GunMagazineSize, "Gun Magazine Size", 1, 0),
        P(PlayerStatToModify.GunKnockbackForce, "Gun Knockback", 0.1f, 0),
        P(PlayerStatToModify.GunMaxShotDistance, "Gun Range", 5, 0),
        P(PlayerStatToModify.MaxHealth, "Maximum Health", 10, 0),
        P(PlayerStatToModify.GroundMoveSpeed, "Ground Speed", 2, 20, strength: 0.5f),
        P(PlayerStatToModify.AirMoveSpeed, "Air Speed", 2, 20, enabled: false),
        P(PlayerStatToModify.JumpForce, "Jump Force", 2, 16, enabled: false),
        P(PlayerStatToModify.DamageCooldown, "Damage Invulnerability Time", 0.1f, 3, enabled: false),
        P(PlayerStatToModify.GunPelletsPerShot, "Pellets Per Shot", 1, 8, enabled: false),
        P(PlayerStatToModify.GunCriticalChance, "Critical Chance", 0, 0, enabled: false),
        P(PlayerStatToModify.GunCriticalDamageMultiplier, "Critical Damage Multiplier", 1, 0, enabled: false),
        P(PlayerStatToModify.HPRegeneration, "Health Regeneration", 0, 0, enabled: false),
        P(PlayerStatToModify.ThornsDamage, "Thorns Damage", 0, 0, enabled: false),
        P(PlayerStatToModify.RadioChargeRate, "Radio Charge Rate", 0.01f, 2, enabled: false),
        P(PlayerStatToModify.RadioAirstrikeCooldown, "Airstrike Cooldown", 5, 0, true, false),
        P(PlayerStatToModify.RadioAirstrikeDamage, "Airstrike Damage", 1, 0, enabled: false)
    };

    private static List<VehicleTradeStat> DefaultVehicleStats() => new List<VehicleTradeStat>
    {
        V(CarStatToModify.TurretDamagePerShot, "Turret Damage", 1, 0),
        V(CarStatToModify.TurretFireRate, "Turret Fire Rate", 0.5f, 0),
        V(CarStatToModify.TurretAccuracySpreadDegrees, "Turret Spread", 0.1f, 20, true),
        V(CarStatToModify.TurretReloadTime, "Turret Reload Duration", 0.25f, 0, true),
        V(CarStatToModify.TurretMagazineSize, "Turret Magazine Size", 1, 0),
        V(CarStatToModify.TurretKnockbackForce, "Turret Knockback", 0.1f, 0),
        V(CarStatToModify.TurretMaxShotDistance, "Turret Range", 5, 0),
        V(CarStatToModify.MaxVehicleHealth, "Maximum Vehicle Health", 10, 0),
        V(CarStatToModify.MaxSpeed, "Maximum Speed", 30, 250, strength: 0.5f),
        V(CarStatToModify.AccelerationMultiplier, "Acceleration", 1, 30, enabled: false),
        V(CarStatToModify.BrakeForce, "Brake Force", 50, 1000, enabled: false),
        V(CarStatToModify.TurretCriticalChance, "Turret Critical Chance", 0, 0, enabled: false),
        V(CarStatToModify.TurretDetectionRange, "Turret Detection Range", 1, 100, enabled: false),
        V(CarStatToModify.VehicleThornsDamage, "Vehicle Thorns Damage", 0, 0, enabled: false)
    };

    [ContextMenu("Restore Default Trade Pools")]
    private void RestoreDefaultTradePools()
    {
        playerStats = DefaultPlayerStats();
        vehicleStats = DefaultVehicleStats();
    }
}
