using System;
using System.Collections.Generic;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class PlayerLevelRewardSystem : MonoBehaviour
{
    public GameObject radioUI;
    private enum RewardTarget
    {
        Player,
        Vehicle
    }

    private enum MagnitudeMode
    {
        PercentOfCurrentValue,
        FlatAmount,
        IntegerStep
    }

    private enum DisplayMode
    {
        Auto,
        PercentageOfCurrentValue,
        PercentagePoints,
        Number,
        Integer
    }

    [Serializable]
    private sealed class FloatRarityRanges
    {
        [Tooltip("Minimum and maximum reward magnitude for Common rarity.")]
        public Vector2 common = new Vector2(0.04f, 0.06f);
        [Tooltip("Minimum and maximum reward magnitude for Rare rarity.")]
        public Vector2 rare = new Vector2(0.07f, 0.10f);
        [Tooltip("Minimum and maximum reward magnitude for Epic rarity.")]
        public Vector2 epic = new Vector2(0.11f, 0.15f);
        [Tooltip("Minimum and maximum reward magnitude for Legendary rarity.")]
        public Vector2 legendary = new Vector2(0.17f, 0.22f);

        public Vector2 Get(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return common;
                case ItemRarity.Rare: return rare;
                case ItemRarity.Epic: return epic;
                case ItemRarity.Legendary: return legendary;
                default: return common;
            }
        }

        public void Normalize(bool clampToOne = false)
        {
            common = PlayerLevelRewardSystem.NormalizeRange(common, clampToOne);
            rare = PlayerLevelRewardSystem.NormalizeRange(rare, clampToOne);
            epic = PlayerLevelRewardSystem.NormalizeRange(epic, clampToOne);
            legendary = PlayerLevelRewardSystem.NormalizeRange(legendary, clampToOne);
        }
    }

    [Serializable]
    private sealed class IntegerRarityRanges
    {
        [Tooltip("Inclusive minimum and maximum integer step for Common rarity.")]
        public Vector2Int common = new Vector2Int(1, 1);
        [Tooltip("Inclusive minimum and maximum integer step for Rare rarity.")]
        public Vector2Int rare = new Vector2Int(1, 1);
        [Tooltip("Inclusive minimum and maximum integer step for Epic rarity.")]
        public Vector2Int epic = new Vector2Int(1, 2);
        [Tooltip("Inclusive minimum and maximum integer step for Legendary rarity.")]
        public Vector2Int legendary = new Vector2Int(2, 3);

        public Vector2Int Get(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return common;
                case ItemRarity.Rare: return rare;
                case ItemRarity.Epic: return epic;
                case ItemRarity.Legendary: return legendary;
                default: return common;
            }
        }

        public void Normalize()
        {
            common = PlayerLevelRewardSystem.NormalizeRange(common);
            rare = PlayerLevelRewardSystem.NormalizeRange(rare);
            epic = PlayerLevelRewardSystem.NormalizeRange(epic);
            legendary = PlayerLevelRewardSystem.NormalizeRange(legendary);
        }
    }

    /// <summary>
    /// One configurable reward entry. The same configuration type is used by the
    /// Player Rewards and Vehicle Rewards lists; the target is determined by its list.
    /// </summary>
    [Serializable]
    private sealed class RewardDefinition
    {
        [Header("Reward Identity")]
        public bool enabled = true;
        [Tooltip("The PlayerStatToModify value used by the Player Rewards list.")]
        public PlayerStatToModify playerStat;
        [Tooltip("The CarStatToModify value used by the Vehicle Rewards list.")]
        public CarStatToModify carStat;
        [Tooltip("Name shown on the reward card. Leave empty to use the enum name.")]
        public string displayName;
        [Tooltip("Display-only conversion for PercentagePoints. Use 100 if the underlying stat is a fraction, e.g. 0.05 = 5%.")]
        [Min(0.0001f)] public float percentagePointsDisplayScale = 1f;
        [Min(0f)]
        [Tooltip("Relative chance for this stat to be selected. This does not affect rarity.")]
        public float selectionWeight = 1f;

        [Header("Reward Behaviour")]
        public MagnitudeMode magnitudeMode = MagnitudeMode.PercentOfCurrentValue;
        [Tooltip("Enable when a negative change to this stat is beneficial (for example, weapon spread or reload duration).")]
        public bool lowerIsBetter;
        [Tooltip("Round the final stat change to a whole number. Useful for magazine size and integer vehicle stats.")]
        public bool roundToInteger;
        public DisplayMode displayMode = DisplayMode.Auto;
        [Min(0f)]
        [Tooltip("Multiplier applied to the global flat reward range. Example: 0.02 turns a flat roll of 1-2 into +0.02 to +0.04.")]
        public float flatScale = 1f;
        [Min(0f)]
        [Tooltip("Reward amount used when a percentage-based stat is currently zero. It is multiplied by the rarity fallback multiplier.")]
        public float zeroValueFallback;

        [Header("Final Stat Limits")]
        [Tooltip("When enabled, the reward cannot reduce the final stat below Minimum Value.")]
        public bool useMinimumValue;
        public float minimumValue;
        [Tooltip("When enabled, the reward cannot increase the final stat above Maximum Value.")]
        public bool useMaximumValue;
        public float maximumValue = 100f;

        [Header("Optional Per-Stat Rarity Ranges")]
        [Tooltip("When enabled, this reward uses the ranges below instead of the global rarity ranges.")]
        public bool overrideRarityRanges;
        public FloatRarityRanges customPercentRanges = new FloatRarityRanges();
        public FloatRarityRanges customFlatRanges = PlayerLevelRewardSystem.CreateDefaultFlatRanges();
        public IntegerRarityRanges customIntegerRanges = new IntegerRarityRanges();

        [NonSerialized] public RewardTarget runtimeTarget;
    }

    private sealed class RewardOption
    {
        public RewardDefinition definition;
        public RewardTarget target;
        public ItemRarity rarity;
        public float delta;
        public float originalValue;
        public float resultingValue;
        public object vehicleIdentity;
        public string amountText;
        public string statName;
    }

    // ==================================================
    // DEFAULT REWARD DEFINITIONS
    // ==================================================

    [Header("Reward Pools")]
    [Tooltip("Enable, remove, reorder, rename and tune individual player rewards here. Each stat can have its own weight, direction, scaling and value limits.")]
    [SerializeField] private List<RewardDefinition> playerRewards = CreateDefaultPlayerRewards();

    [Tooltip("Vehicle rewards are included only while PlayerInventory.CurrentCarStats is assigned. Configure them independently from player rewards.")]
    [SerializeField] private List<RewardDefinition> vehicleRewards = CreateDefaultVehicleRewards();

    [Header("References")]
    [SerializeField] private PlayerLevelSystem levelSystem;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerStats playerStats;

    // ==================================================
    // RARITY WEIGHTS
    // ==================================================

    [Header("Rarity Weights")]
    [Min(0f)] [SerializeField] private float commonWeight = 60f;
    [Min(0f)] [SerializeField] private float rareWeight = 10f;
    [Min(0f)] [SerializeField] private float epicWeight = 4f;
    [Min(0f)] [SerializeField] private float legendaryWeight = 1f;

    [Min(0f)]
    [Tooltip("Luck increases higher-tier rarity weights by this amount per item-luck point and rarity tier. Example: 0.1 means each luck point adds 10% weight per tier.")]
    [SerializeField] private float luckWeightPerPoint = 0.1f;

    // ==================================================
    // GLOBAL MAGNITUDE RANGES
    // ==================================================

    [Header("Global Percentage Ranges")]
    [Tooltip("Used by Percent Of Current Value rewards unless that reward overrides these ranges. Values are fractions: 0.05 = 5%.")]
    [SerializeField] private FloatRarityRanges percentageRanges = CreateDefaultPercentageRanges();

    [Header("Global Flat Ranges")]
    [Tooltip("Used by Flat Amount rewards before the individual reward's Flat Scale is applied.")]
    [SerializeField] private FloatRarityRanges flatRanges = CreateDefaultFlatRanges();

    [Header("Global Integer Step Ranges")]
    [Tooltip("Inclusive integer amounts used by Integer Step rewards unless the individual reward overrides these ranges.")]
    [SerializeField] private IntegerRarityRanges integerRanges = new IntegerRarityRanges();

    [Header("Zero-Value Reward Scaling")]
    [Tooltip("Fallback amount multipliers in Common, Rare, Epic, Legendary order. Used when a percentage reward starts at zero.")]
    [SerializeField]
    private Vector4 zeroValueFallbackRarityMultipliers =
        new Vector4(1f, 1.5f, 2.25f, 3.25f);

    // ==================================================
    // UI
    // ==================================================

    [Header("Choice UI")]
    [Tooltip("How many different rewards to offer on each level-up. Switch between 2 and 3 for testing.")]
    [SerializeField, Range(2, 3)] private int offeredChoiceCount = 2;
    [SerializeField] private GameObject choicePanel;
    [SerializeField] private Button[] rewardButtons = new Button[3];
    [SerializeField] private TextMeshProUGUI[] rewardNameTexts = new TextMeshProUGUI[3];
    [SerializeField] private TextMeshProUGUI[] rarityTexts = new TextMeshProUGUI[3];
    [SerializeField] private TextMeshProUGUI[] descriptionTexts = new TextMeshProUGUI[3];
    [SerializeField] private Image[] rewardFrames = new Image[3];
    [SerializeField] private TextMeshProUGUI remainingChoicesText;
    [Tooltip("Optional current earned level, independent of remaining reward choices.")]
    [SerializeField] private TextMeshProUGUI currentLevelText;
    [SerializeField] private string currentLevelPrefix = "LEVEL ";

    [Header("Tactical Card Presentation (Optional)")]
    [SerializeField] private HUDRewardCard[] tacticalCards = new HUDRewardCard[3];
    [SerializeField, Range(0f, 0.6f)] private float selectionHoldSeconds = 0.28f;
    [SerializeField] private bool rareRevealParticles = true;

    [Header("Selection Impact")]
    [Tooltip("Scale overshoot of the entire selected card. Zero disables the punch.")]
    [SerializeField, Range(0f, 0.15f)] private float confirmationPunchStrength = 0.06f;
    [Tooltip("Brief white rim flash on successful application. Zero disables the flash.")]
    [SerializeField, Range(0f, 1f)] private float confirmationFlashStrength = 0.8f;
    [Tooltip("Opacity of other cards during the selection hold. One disables dimming.")]
    [SerializeField, Range(0f, 1f)] private float unchosenCardOpacity = 0.45f;

    [Header("Celebration (Optional)")]
    [SerializeField] private HUDRadialRays celebrationRays;
    [SerializeField] private bool celebrateMenuOpening = true;
    [SerializeField] private bool celebrateHighRarity = true;
    [SerializeField] private bool rarityCardShine = true;
    [SerializeField, Range(0f, 1f)] private float openingRayOpacity = .10f;
    [SerializeField, Range(0f, 1f)] private float epicRayOpacity = .14f;
    [SerializeField, Range(0f, 1f)] private float legendaryRayOpacity = .28f;
    [SerializeField, Min(.01f)] private float celebrationDuration = .85f;

    [Header("Progression Atmosphere (v1.4)")]
    [SerializeField] private bool ambientProgression = true;
    [SerializeField] private Color atmosphereColor = new Color32(112, 231, 237, 255);
    [SerializeField, Range(0, 64)] private int ambientSparkCount = 36;
    [SerializeField, Range(0f, 1f)] private float ambientSparkOpacity = .55f;
    [SerializeField, Range(0f, 1f)] private float ambientRayOpacity = .10f;
    [SerializeField] private bool confirmationShockwave = true;
    [SerializeField, Range(0f, 1f)] private float shockwaveOpacity = .65f;
    [SerializeField, Range(0f, .2f)] private float confirmationNumberPunch = .10f;
    [SerializeField] private bool enhancedCardRims = true;
    [SerializeField] private bool improveMenuContrast = true;
    [SerializeField, Range(0f, 1f)] private float minimumBackgroundOpacity = .78f;
    [Tooltip("Fade the full-screen background without scaling it. Card reveal animation is retained.")]
    [SerializeField] private bool stationaryMenuBackground = true;
    private HUDRewardAtmosphere rewardAtmosphere;

    [Header("Reward Queue")]
    [Tooltip("Keep unspent levels when no useful reward remains. Retry through RetryPendingChoices when stats change.")]
    [SerializeField] private bool retainUnofferableChoices;

    // ==================================================
    // CARD COLOURS
    // ==================================================

    [Header("Card Colours")]
    [SerializeField] private Color commonColor = new Color(0.65f, 0.65f, 0.65f);
    [SerializeField] private Color cardBackground = new Color(0.035f, 0.075f, 0.13f, 0.96f);
    [SerializeField, Range(0f, 1f)] private float rarityBackgroundTint = 0.22f;
    [SerializeField] private Color titleColor = Color.white;
    [SerializeField] private Color playerAccent = new Color(0.36f, 0.8f, 1f);
    [SerializeField] private Color vehicleAccent = new Color(0.36f, 0.8f, 1f);
    [SerializeField] private Color improvementColor = new Color(0.45f, 1f, 0.5f);
    [SerializeField] private Color rareColor = new Color(0.2f, 0.65f, 1f);
    [SerializeField] private Color epicColor = new Color(0.75f, 0.3f, 1f);
    [SerializeField] private Color legendaryColor = new Color(1f, 0.7f, 0.15f);

    // ==================================================
    // MENU ANIMATION
    // ==================================================

    [Header("Menu Animation")]
    [SerializeField, Min(0f)] private float openDuration = 0.25f;
    [SerializeField, Min(0f)] private float closeDuration = 0.15f;
    [SerializeField, Range(0.1f, 1f)] private float startScale = 0.85f;

    // ==================================================
    // AUDIO
    // ==================================================

    [Header("Audio")]
    [SerializeField] private AudioClip rewardSelectedSound;
    [Tooltip("Optional: plays once when an offer containing an Epic reward finishes revealing.")]
    [SerializeField] private AudioClip epicRevealSound;
    [Tooltip("Optional: takes precedence over the Epic sound when both are offered.")]
    [SerializeField] private AudioClip legendaryRevealSound;
    [SerializeField, Range(0f, 1f)] private float rarityRevealVolume = 0.45f;

    // ==================================================
    // RUNTIME STATE
    // ==================================================

    [Header("Additional UI Audio (Optional)")]
    [SerializeField] private AudioClip menuOpenSound, cardHoverSound, queueCompleteSound;
    [SerializeField, Range(0f, 1f)] private float menuOpenVolume = .35f;
    [SerializeField, Range(0f, 1f)] private float cardHoverVolume = .15f;
    [SerializeField, Range(0f, 1f)] private float queueCompleteVolume = .4f;
    [SerializeField, Min(0f)] private float hoverSoundInterval = .075f;
    private float nextHoverSoundTime;
    private bool firstOfferInSession, completionSoundPending;

    private const int MaxChoiceCount = 3;
    private const float StatEpsilon = 0.0001f;
    // Keep room for all three UI cards so the Inspector setting can be changed without rebuilding the UI.
    private readonly RewardOption[] currentOptions = new RewardOption[MaxChoiceCount];
    private readonly List<RewardDefinition> candidates = new List<RewardDefinition>(64);
    private bool isApplying;
    private bool inputReady;
    private int pendingChoices;
    private bool isShowing;
    private bool isClosing;
    private bool hasSavedGameState;
    private bool listenersRegistered;
    private bool initialized;
    private PlayerLevelSystem subscribedLevelSystem;
    private int lastDisplayedLevel = -1;

    private float previousTimeScale;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GameObject hiddenRadioUI;
    private bool previousRadioActive;

    private CanvasGroup canvasGroup;
    private Vector3 panelOpenScale = Vector3.one;
    private Sequence menuSequence;
    private readonly List<RaycastResult> pointerHits = new List<RaycastResult>(16);
    private Vector2 lastPointerPosition;

    public int PendingChoices => pendingChoices;
    public PlayerLevelSystem LevelSystem => levelSystem;

    // ==================================================
    // DEFAULT CONFIGURATION FACTORIES
    // ==================================================

    private static FloatRarityRanges CreateDefaultPercentageRanges()
    {
        return new FloatRarityRanges
        {
            common = new Vector2(0.04f, 0.06f),
            rare = new Vector2(0.07f, 0.10f),
            epic = new Vector2(0.11f, 0.15f),
            legendary = new Vector2(0.17f, 0.22f)
        };
    }

    private static FloatRarityRanges CreateDefaultFlatRanges()
    {
        return new FloatRarityRanges
        {
            common = new Vector2(1f, 2f),
            rare = new Vector2(2f, 3f),
            epic = new Vector2(3f, 4f),
            legendary = new Vector2(4f, 6f)
        };
    }

    private static RewardDefinition PlayerReward(
        PlayerStatToModify stat,
        string name,
        MagnitudeMode mode = MagnitudeMode.PercentOfCurrentValue,
        bool lowerIsBetter = false,
        bool roundToInteger = false,
        float flatScale = 1f,
        float zeroFallback = 0f,
        DisplayMode displayMode = DisplayMode.Auto,
        float? minimumValue = null,
        float? maximumValue = null,
        float weight = 1f)
    {
        RewardDefinition reward = new RewardDefinition
        {
            enabled = true,
            playerStat = stat,
            displayName = name,
            selectionWeight = weight,
            magnitudeMode = mode,
            lowerIsBetter = lowerIsBetter,
            roundToInteger = roundToInteger,
            flatScale = flatScale,
            zeroValueFallback = zeroFallback,
            displayMode = displayMode
        };

        if (minimumValue.HasValue)
        {
            reward.useMinimumValue = true;
            reward.minimumValue = minimumValue.Value;
        }

        if (maximumValue.HasValue)
        {
            reward.useMaximumValue = true;
            reward.maximumValue = maximumValue.Value;
        }

        return reward;
    }

    private static RewardDefinition VehicleReward(
        CarStatToModify stat,
        string name,
        MagnitudeMode mode = MagnitudeMode.PercentOfCurrentValue,
        bool lowerIsBetter = false,
        bool roundToInteger = false,
        float flatScale = 1f,
        float zeroFallback = 0f,
        DisplayMode displayMode = DisplayMode.Auto,
        float? minimumValue = null,
        float? maximumValue = null,
        float weight = 1f)
    {
        RewardDefinition reward = new RewardDefinition
        {
            enabled = true,
            carStat = stat,
            displayName = name,
            selectionWeight = weight,
            magnitudeMode = mode,
            lowerIsBetter = lowerIsBetter,
            roundToInteger = roundToInteger,
            flatScale = flatScale,
            zeroValueFallback = zeroFallback,
            displayMode = displayMode
        };

        if (minimumValue.HasValue)
        {
            reward.useMinimumValue = true;
            reward.minimumValue = minimumValue.Value;
        }

        if (maximumValue.HasValue)
        {
            reward.useMaximumValue = true;
            reward.maximumValue = maximumValue.Value;
        }

        return reward;
    }

    private static List<RewardDefinition> CreateDefaultPlayerRewards()
    {
        return new List<RewardDefinition>
        {
            // Survivability
            PlayerReward(PlayerStatToModify.MaxHealth, "Maximum Health", maximumValue: 500f),
            PlayerReward(PlayerStatToModify.DamageCooldown, "Damage Invulnerability Time", maximumValue: 3f),
            PlayerReward(PlayerStatToModify.ThornsDamage, "Thorns Damage", zeroFallback: 1f, maximumValue: 100f),
            PlayerReward(PlayerStatToModify.HPRegeneration, "Health Regeneration", zeroFallback: 1f, maximumValue: 10f),

            // Progression
            PlayerReward(PlayerStatToModify.ItemLuck, "Luck", MagnitudeMode.FlatAmount, flatScale: 0.1f, maximumValue: 10f),
            PlayerReward(PlayerStatToModify.XPGain, "XP Gain", MagnitudeMode.FlatAmount, flatScale: 0.02f,
                displayMode: DisplayMode.PercentagePoints, maximumValue: 5f),

            // Ground and air movement
            PlayerReward(PlayerStatToModify.GroundMoveSpeed, "Ground Movement Speed", maximumValue: 12f),
            PlayerReward(PlayerStatToModify.GroundAcceleration, "Ground Acceleration", maximumValue: 30f),
            PlayerReward(PlayerStatToModify.Friction, "Ground Friction", lowerIsBetter: true, minimumValue: 1.5f),
            PlayerReward(PlayerStatToModify.AirMoveSpeed, "Air Movement Speed", maximumValue: 12f),
            PlayerReward(PlayerStatToModify.AirAcceleration, "Air Acceleration", maximumValue: 8f),
            PlayerReward(PlayerStatToModify.AirControl, "Air Control", maximumValue: 1f),
            PlayerReward(PlayerStatToModify.JumpForce, "Jump Force", maximumValue: 12f),
            PlayerReward(PlayerStatToModify.StrafeMaxSpeed, "Air Strafe Speed", maximumValue: 5f),

            // Player weapon
            PlayerReward(PlayerStatToModify.GunDamagePerPellet, "Gun Damage", maximumValue: 100f),
            PlayerReward(PlayerStatToModify.GunKnockbackForce, "Gun Knockback", maximumValue: 30f),
            PlayerReward(PlayerStatToModify.GunFireRate, "Gun Fire Rate", maximumValue: 20f),
            PlayerReward(PlayerStatToModify.GunAccuracySpreadDegrees, "Gun Spread", lowerIsBetter: true, minimumValue: 0.15f),
            PlayerReward(PlayerStatToModify.GunCriticalChance, "Gun Critical Chance", MagnitudeMode.FlatAmount,
                flatScale: 1f, displayMode: DisplayMode.PercentagePoints),
            PlayerReward(PlayerStatToModify.GunCriticalDamageMultiplier, "Critical Damage Multiplier", maximumValue: 5f),
            PlayerReward(PlayerStatToModify.GunMagazineSize, "Gun Magazine Size", roundToInteger: true, maximumValue: 120f),
            PlayerReward(PlayerStatToModify.GunReloadDuration, "Gun Reload Duration", lowerIsBetter: true, minimumValue: 0.35f),
            PlayerReward(PlayerStatToModify.GunMaxShotDistance, "Gun Range", maximumValue: 150f),

            // Radio
            PlayerReward(PlayerStatToModify.RadioChargeRate, "Radio Charge Rate", maximumValue: 2f),
            PlayerReward(PlayerStatToModify.RadioRepositionCooldown, "Radio Reposition Cooldown", lowerIsBetter: true, minimumValue: 0.5f),
            PlayerReward(PlayerStatToModify.RadioRespawnCooldown, "Radio Respawn Cooldown", lowerIsBetter: true, minimumValue: 1f),
            PlayerReward(PlayerStatToModify.RadioAirstrikeCooldown, "Airstrike Cooldown", lowerIsBetter: true, minimumValue: 5f),
            PlayerReward(PlayerStatToModify.RadioAirstrikeDelay, "Airstrike Activation Delay", lowerIsBetter: true, minimumValue: 0.25f),
            PlayerReward(PlayerStatToModify.RadioAirstrikeScale, "Airstrike Size", maximumValue: 8f),
            PlayerReward(PlayerStatToModify.RadioAirstrikeDamage, "Airstrike Damage", maximumValue: 500f),
            PlayerReward(PlayerStatToModify.RadioAirstrikeDamageMultiplier, "Airstrike Damage Multiplier", maximumValue: 5f),

            
 // Grenades
 PlayerReward(
     PlayerStatToModify.GrenadeCapacity,
     "Grenade Capacity",
     MagnitudeMode.IntegerStep,
     maximumValue: 8f,
     weight: 0.45f),

 PlayerReward(
     PlayerStatToModify.GrenadeRechargeSeconds,
     "Grenade Recharge Time",
     lowerIsBetter: true,
     minimumValue: 5f),

 PlayerReward(
     PlayerStatToModify.GrenadeDamage,
     "Grenade Damage",
     maximumValue: 500f),

 PlayerReward(
     PlayerStatToModify.GrenadeExplosionRadius,
     "Grenade Blast Radius",
     maximumValue: 12f),

 PlayerReward(
     PlayerStatToModify.GrenadeKnockbackForce,
     "Grenade Knockback",
     maximumValue: 35f),

 PlayerReward(
     PlayerStatToModify.GrenadeThrowSpeed,
     "Grenade Throw Velocity",
     maximumValue: 30f,
     weight: 0.5f),

 PlayerReward(
     PlayerStatToModify.GrenadeFuseSeconds,
     "Grenade Fuse Time",
     lowerIsBetter: true,
     minimumValue: 0.5f,
     weight: 0.5f),

        };
    }

    private static List<RewardDefinition> CreateDefaultVehicleRewards()
    {
        return new List<RewardDefinition>
        {
            // Driving
            VehicleReward(CarStatToModify.MaxSpeed, "Maximum Vehicle Speed", roundToInteger: true, maximumValue: 220f),
            VehicleReward(CarStatToModify.MaxReverseSpeed, "Reverse Speed", roundToInteger: true, maximumValue: 110f),
            VehicleReward(CarStatToModify.AccelerationMultiplier, "Vehicle Acceleration", roundToInteger: true, maximumValue: 20f),
            VehicleReward(CarStatToModify.MaxSteeringAngle, "Maximum Steering Angle", roundToInteger: true, maximumValue: 60f),
            VehicleReward(CarStatToModify.SteeringSpeed, "Steering Responsiveness", maximumValue: 3f),
            VehicleReward(CarStatToModify.BrakeForce, "Brake Force", roundToInteger: true, maximumValue: 1000f),

            // Durability and offensive vehicle effects
            VehicleReward(CarStatToModify.MaxVehicleHealth, "Vehicle Maximum Health", maximumValue: 500f),
            VehicleReward(CarStatToModify.VehicleThornsDamage, "Vehicle Thorns Damage", zeroFallback: 1f, maximumValue: 100f),
            VehicleReward(CarStatToModify.CarExplosionDamage, "Vehicle Explosion Damage", maximumValue: 500f),
            VehicleReward(CarStatToModify.CarExplosionDamageRange, "Vehicle Explosion Range", maximumValue: 30f),
            VehicleReward(CarStatToModify.MaximumEnemyDamage, "Maximum Run-Over Damage", maximumValue: 250f),
            VehicleReward(CarStatToModify.MaximumEnemyKnockback, "Run-Over Knockback", maximumValue: 50f),
            VehicleReward(CarStatToModify.EnemyContactDamageCooldown, "Contact Protection Interval", maximumValue: 3f),

            // Mounted turret
            VehicleReward(CarStatToModify.TurretDamagePerShot, "Turret Damage", maximumValue: 100f),
            VehicleReward(CarStatToModify.TurretKnockbackForce, "Turret Knockback", maximumValue: 30f),
            VehicleReward(CarStatToModify.TurretFireRate, "Turret Fire Rate", maximumValue: 20f),
            VehicleReward(CarStatToModify.TurretAccuracySpreadDegrees, "Turret Spread", lowerIsBetter: true, minimumValue: 0.1f),
            VehicleReward(CarStatToModify.TurretCriticalChance, "Turret Critical Chance", MagnitudeMode.FlatAmount,
                flatScale: 1f, displayMode: DisplayMode.PercentagePoints),
            VehicleReward(CarStatToModify.TurretCriticalDamageMultiplier, "Turret Critical Damage Multiplier", maximumValue: 5f),
            VehicleReward(CarStatToModify.TurretMagazineSize, "Turret Magazine Size", roundToInteger: true, maximumValue: 120f),
            VehicleReward(CarStatToModify.TurretReloadTime, "Turret Reload Duration", lowerIsBetter: true, minimumValue: 0.35f),
            VehicleReward(CarStatToModify.TurretDetectionRange, "Turret Detection Range", maximumValue: 100f),
            VehicleReward(CarStatToModify.TurretMaxShotDistance, "Turret Shooting Range", maximumValue: 150f)
        };
    }

    /// <summary>
    /// Restores the recommended defaults for the reward pools and global tuning.
    /// This intentionally overwrites existing inspector customizations.
    /// </summary>
    [ContextMenu("Restore Recommended Reward Defaults")]
    public void RestoreRecommendedRewardDefaults()
    {
        playerRewards = CreateDefaultPlayerRewards();
        vehicleRewards = CreateDefaultVehicleRewards();
        percentageRanges = CreateDefaultPercentageRanges();
        flatRanges = CreateDefaultFlatRanges();
        integerRanges = new IntegerRarityRanges();

        commonWeight = 60f;
        rareWeight = 10f;
        epicWeight = 4f;
        legendaryWeight = 1f;
        luckWeightPerPoint = 0.1f;
        zeroValueFallbackRarityMultipliers = new Vector4(1f, 1.5f, 2.25f, 3.25f);

        Debug.Log("PlayerLevelRewardSystem: Recommended reward defaults restored. Review and save the component.", this);
    }

    /// <summary>
    /// Checks reward entries for obvious inspector configuration mistakes.
    /// Stat mappings are still verified at runtime through PlayerInventory.
    /// </summary>
    [ContextMenu("Validate Reward Configuration")]
    public void ValidateRewardConfiguration()
    {
        int enabledPlayer = CountEnabled(playerRewards);
        int enabledVehicle = CountEnabled(vehicleRewards);

        if (enabledPlayer < offeredChoiceCount)
            Debug.LogWarning($"PlayerLevelRewardSystem: Fewer than {offeredChoiceCount} player rewards are enabled. Some offers may contain fewer than {offeredChoiceCount} cards.", this);

        if (enabledVehicle > 0 && enabledVehicle < offeredChoiceCount)
            Debug.LogWarning($"PlayerLevelRewardSystem: Fewer than {offeredChoiceCount} vehicle rewards are enabled. Vehicle-only offers may contain fewer than {offeredChoiceCount} cards.", this);

        ValidateDuplicateStats(playerRewards, RewardTarget.Player);
        ValidateDuplicateStats(vehicleRewards, RewardTarget.Vehicle);

        Debug.Log(
            $"PlayerLevelRewardSystem validation finished. Enabled player rewards: {enabledPlayer}. Enabled vehicle rewards: {enabledVehicle}. " +
            "At runtime, PlayerInventory must support each selected enum in TryGet/ TryAdd stat methods.",
            this);
    }

    private static int CountEnabled(List<RewardDefinition> rewards)
    {
        if (rewards == null)
            return 0;

        int count = 0;
        for (int i = 0; i < rewards.Count; i++)
        {
            if (rewards[i] != null && rewards[i].enabled && rewards[i].selectionWeight > 0f)
                count++;
        }
        return count;
    }

    private void ValidateDuplicateStats(List<RewardDefinition> rewards, RewardTarget target)
    {
        if (rewards == null)
            return;

        for (int i = 0; i < rewards.Count; i++)
        {
            RewardDefinition a = rewards[i];
            if (a == null || !a.enabled)
                continue;

            for (int j = i + 1; j < rewards.Count; j++)
            {
                RewardDefinition b = rewards[j];
                if (b == null || !b.enabled)
                    continue;

                bool duplicate = target == RewardTarget.Player
                    ? a.playerStat.Equals(b.playerStat)
                    : a.carStat.Equals(b.carStat);

                if (duplicate)
                {
                    Debug.LogWarning(
                        $"PlayerLevelRewardSystem: Duplicate enabled {target} reward stat at list entries {i} and {j}. Only one copy will be offered per selection.",
                        this);
                }
            }
        }
    }

    // ==================================================
    // INITIALIZATION
    // ==================================================

    private void Reset()
    {
        playerRewards = CreateDefaultPlayerRewards();
        vehicleRewards = CreateDefaultVehicleRewards();
        percentageRanges = CreateDefaultPercentageRanges();
        flatRanges = CreateDefaultFlatRanges();
        integerRanges = new IntegerRarityRanges();
    }

    private void Awake()
    {
        if (levelSystem == null)
            levelSystem = GetComponentInParent<PlayerLevelSystem>();

        if (inventory == null)
            inventory = GetComponentInParent<PlayerInventory>();

        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerRewards == null)
            playerRewards = CreateDefaultPlayerRewards();

        if (vehicleRewards == null)
            vehicleRewards = CreateDefaultVehicleRewards();

        if (percentageRanges == null)
            percentageRanges = CreateDefaultPercentageRanges();

        if (flatRanges == null)
            flatRanges = CreateDefaultFlatRanges();

        if (integerRanges == null)
            integerRanges = new IntegerRarityRanges();

        if (choicePanel != null && (choicePanel.transform == transform || transform.IsChildOf(choicePanel.transform)))
        {
            Debug.LogError("PlayerLevelRewardSystem must live outside the panel it hides.", this);
            choicePanel = null;
            enabled = false;
            return;
        }

        if (choicePanel != null)
        {
            // v1.0 generated hierarchy: hide the redundant heading without rebuilding.
            Transform eyebrow = choicePanel.transform.Find("Content/Eyebrow");
            if (eyebrow != null) eyebrow.gameObject.SetActive(false);
            panelOpenScale = choicePanel.transform.localScale;
            canvasGroup = choicePanel.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = choicePanel.AddComponent<CanvasGroup>();

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            SetupRewardAtmosphere();
            choicePanel.SetActive(false);
        }

        if (!HasValidUI())
        {
            Debug.LogError(
                "PlayerLevelRewardSystem: Assign a reward panel and three distinct child reward buttons.",
                this);
        }
        else
        {
            rewardButtons[0].onClick.AddListener(SelectReward0);
            rewardButtons[1].onClick.AddListener(SelectReward1);
            rewardButtons[2].onClick.AddListener(SelectReward2);
            listenersRegistered = true;
        }

        if (levelSystem == null)
            Debug.LogError("PlayerLevelRewardSystem: PlayerLevelSystem is missing.", this);

        if (inventory == null)
            Debug.LogError("PlayerLevelRewardSystem: PlayerInventory is missing.", this);

        NormalizeRewardList(playerRewards);
        NormalizeRewardList(vehicleRewards);
        initialized = true;
    }

    // Build one non-interactive graphic behind the existing cards. No editor rebuild.
    private void SetupRewardAtmosphere()
    {
        if (choicePanel == null || !(choicePanel.transform is RectTransform)) return;
        bool hasTacticalCard = false;
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            HUDRewardCard card = At(tacticalCards, i);
            if (card != null && card.transform.IsChildOf(choicePanel.transform))
                hasTacticalCard = true;
        }
        if (!hasTacticalCard) return;
        var go = new GameObject("ProgressionAtmosphere (Runtime)", typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = choicePanel.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(choicePanel.transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();
        rewardAtmosphere = go.AddComponent<HUDRewardAtmosphere>();
        rewardAtmosphere.raycastTarget = false;
        if (improveMenuContrast)
        {
            Image scrim = choicePanel.GetComponent<Image>();
            if (scrim != null)
            {
                Color c = scrim.color;
                c.a = Mathf.Max(c.a, minimumBackgroundOpacity);
                scrim.color = c;
            }
        }
    }

    private bool KeepBackgroundStationary => stationaryMenuBackground && rewardAtmosphere != null;

    private void OnEnable()
    {
        if (initialized && levelSystem != null)
        {
            subscribedLevelSystem = levelSystem;
            subscribedLevelSystem.LevelEarned += OnLevelEarned;
            lastDisplayedLevel = -1;
            UpdateCurrentLevelText();
        }

        if (initialized && pendingChoices > 0)
            ShowNextOffer();
    }

    private void OnLevelEarned(int ignoredEarnedLevel)
    {
        UpdateCurrentLevelText();
    }

    private void UpdateCurrentLevelText()
    {
        if (currentLevelText == null || levelSystem == null) return;
        // TotalLevelsEarned already contains the final total for a multi-level award.
        int level = levelSystem.TotalLevelsEarned + 1;
        if (level == lastDisplayedLevel) return;
        lastDisplayedLevel = level;
        currentLevelText.text = currentLevelPrefix + level.ToString();
    }

    private bool HasValidUI()
    {
        return choicePanel != null &&
               rewardButtons != null &&
               rewardButtons.Length >= 3 &&
               rewardButtons[0] != null &&
               rewardButtons[1] != null &&
               rewardButtons[2] != null &&
               rewardButtons[0] != rewardButtons[1] &&
               rewardButtons[0] != rewardButtons[2] &&
               rewardButtons[1] != rewardButtons[2] &&
               IsPanelChild(rewardButtons[0]) && IsPanelChild(rewardButtons[1]) && IsPanelChild(rewardButtons[2]) &&
               canvasGroup != null;
    }

    private bool IsPanelChild(Button button) => button.transform != choicePanel.transform &&
        button.transform.IsChildOf(choicePanel.transform);

    private void OnValidate()
    {
        offeredChoiceCount = Mathf.Clamp(offeredChoiceCount, 2, MaxChoiceCount);
        commonWeight = Mathf.Max(0f, commonWeight);
        rareWeight = Mathf.Max(0f, rareWeight);
        epicWeight = Mathf.Max(0f, epicWeight);
        legendaryWeight = Mathf.Max(0f, legendaryWeight);
        luckWeightPerPoint = Mathf.Max(0f, luckWeightPerPoint);

        if (percentageRanges != null)
            percentageRanges.Normalize(true);
        if (flatRanges != null)
            flatRanges.Normalize();
        if (integerRanges != null)
            integerRanges.Normalize();

        zeroValueFallbackRarityMultipliers.x = Mathf.Max(0f, zeroValueFallbackRarityMultipliers.x);
        zeroValueFallbackRarityMultipliers.y = Mathf.Max(0f, zeroValueFallbackRarityMultipliers.y);
        zeroValueFallbackRarityMultipliers.z = Mathf.Max(0f, zeroValueFallbackRarityMultipliers.z);
        zeroValueFallbackRarityMultipliers.w = Mathf.Max(0f, zeroValueFallbackRarityMultipliers.w);

        NormalizeRewardList(playerRewards);
        NormalizeRewardList(vehicleRewards);

        openDuration = Mathf.Max(0f, openDuration);
        closeDuration = Mathf.Max(0f, closeDuration);
        startScale = Mathf.Clamp(startScale, 0.1f, 1f);
    }

    private static void NormalizeRewardList(List<RewardDefinition> rewards)
    {
        if (rewards == null)
            return;

        for (int i = 0; i < rewards.Count; i++)
        {
            RewardDefinition reward = rewards[i];
            if (reward == null)
                continue;

            reward.selectionWeight = Mathf.Max(0f, reward.selectionWeight);
            reward.flatScale = Mathf.Max(0f, reward.flatScale);
            if (!IsFinite(reward.percentagePointsDisplayScale) || reward.percentagePointsDisplayScale <= 0f)
                reward.percentagePointsDisplayScale = 1f;
            reward.zeroValueFallback = Mathf.Max(0f, reward.zeroValueFallback);
            reward.minimumValue = Mathf.Max(0f, reward.minimumValue);
            reward.maximumValue = Mathf.Max(0f, reward.maximumValue);

            if (reward.useMaximumValue && reward.useMinimumValue &&
                reward.maximumValue < reward.minimumValue)
            {
                reward.maximumValue = reward.minimumValue;
            }

            if (reward.customPercentRanges == null)
                reward.customPercentRanges = CreateDefaultPercentageRanges();
            if (reward.customFlatRanges == null)
                reward.customFlatRanges = CreateDefaultFlatRanges();
            if (reward.customIntegerRanges == null)
                reward.customIntegerRanges = new IntegerRarityRanges();

            reward.customPercentRanges.Normalize(true);
            reward.customFlatRanges.Normalize();
            reward.customIntegerRanges.Normalize();
        }
    }

    private static Vector2 NormalizeRange(Vector2 range, bool clampToOne)
    {
        range.x = Mathf.Max(0f, range.x);
        range.y = Mathf.Max(range.x, range.y);
        if (clampToOne)
        {
            range.x = Mathf.Clamp01(range.x);
            range.y = Mathf.Clamp01(range.y);
        }
        return range;
    }

    private static Vector2Int NormalizeRange(Vector2Int range)
    {
        range.x = Mathf.Max(0, range.x);
        range.y = Mathf.Max(range.x, range.y);
        return range;
    }

    // ==================================================
    // QUEUE
    // ==================================================

    /// <summary>Called by the XP Fountain after its XP visuals have completed.</summary>
    public void QueueLevelUps(int amount)
    {
        if (amount <= 0)
            return;

        long combined = (long)pendingChoices + amount;
        pendingChoices = combined > int.MaxValue ? int.MaxValue : (int)combined;

        UpdateRemainingChoicesText();

        if (!isShowing && !isClosing && isActiveAndEnabled)
            ShowNextOffer();
    }

    // ==================================================
    // GENERATE OFFERS
    // ==================================================

    public void RetryPendingChoices()
    {
        if (initialized && isActiveAndEnabled)
            ShowNextOffer();
    }

    private void ShowNextOffer()
    {
        if (!initialized || !isActiveAndEnabled || pendingChoices <= 0 || isShowing || isClosing || isApplying)
            return;

        if (!HasValidUI() || !listenersRegistered || inventory == null)
        {
            Debug.LogError("PlayerLevelRewardSystem: Reward UI or inventory is missing.", this);
            HideChoicePanel();
            RestoreGameState();
            return;
        }

        Array.Clear(currentOptions, 0, currentOptions.Length);

        candidates.Clear();
        AddEligibleDefinitions(playerRewards, RewardTarget.Player, candidates);

        // Car rewards are available only when the inventory exposes an assigned current car.
        if (inventory.CurrentCarStats != null)
            AddEligibleDefinitions(vehicleRewards, RewardTarget.Vehicle, candidates);

        int generatedCount = 0;
        while (generatedCount < offeredChoiceCount && candidates.Count > 0)
        {
            int selectedIndex = GetWeightedCandidateIndex(candidates);
            RewardDefinition selectedDefinition = candidates[selectedIndex];
            candidates.RemoveAt(selectedIndex);

            RewardOption option = GenerateReward(
                selectedDefinition,
                selectedDefinition.runtimeTarget);
            if (option == null)
                continue;

            currentOptions[generatedCount] = option;
            generatedCount++;
        }

        if (generatedCount == 0)
        {
            Debug.LogWarning("PlayerLevelRewardSystem: No useful rewards remain. " +
                (retainUnofferableChoices ? "Pending levels retained." : "Pending levels cleared."), this);
            if (!retainUnofferableChoices)
                pendingChoices = 0;
            UpdateRemainingChoicesText();
            HideChoicePanel();
            RestoreGameState();
            return;
        }

        // The root stays visible throughout a queue. Only the cards are revealed again.
        bool continuing = hasSavedGameState && choicePanel.activeSelf;
        firstOfferInSession = !continuing;
        completionSoundPending = false;
        if (celebrationRays != null) celebrationRays.Clear();
        isShowing = true;
        isClosing = false;
        inputReady = false;
        SaveAndPauseGame();
        KillMenuTween();
        ClearMenuFocus();
        choicePanel.transform.localScale = continuing || KeepBackgroundStationary ? panelOpenScale : panelOpenScale * startScale;
        canvasGroup.alpha = continuing ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;
        choicePanel.SetActive(true);
        if (firstOfferInSession)
        {
            if (rewardAtmosphere != null)
                rewardAtmosphere.Begin(atmosphereColor, ambientProgression, ambientSparkCount,
                    ambientRayOpacity, ambientSparkOpacity, confirmationShockwave);
            PlayOptional(menuOpenSound, menuOpenVolume);
        }
        SetRewardButtonsInteractable(false);
        UpdateCardUI();
        SetRewardButtonsInteractable(false);

        if (openDuration <= 0f)
        {
            FinishOpeningOffer();
            return;
        }
        menuSequence = DOTween.Sequence().SetUpdate(true);
        if (!continuing)
        {
            if (!KeepBackgroundStationary)
                menuSequence.Join(choicePanel.transform.DOScale(panelOpenScale, openDuration).SetEase(Ease.OutCubic));
            menuSequence.Join(canvasGroup.DOFade(1f, openDuration).SetEase(Ease.OutQuad));
        }
        else
        {
            // Keeps the sequence valid even with legacy cards or missing visual references.
            menuSequence.AppendInterval(openDuration);
        }
        // Stagger within the original opening time; input still unlocks on completion.
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            HUDRewardCard card = At(tacticalCards, i);
            if (currentOptions[i] == null || card == null) continue;
            float offset = openDuration * 0.18f * i;
            card.AddReveal(menuSequence, offset, openDuration - offset);
        }
        menuSequence.OnComplete(FinishOpeningOffer);
    }

    private void FinishOpeningOffer()
    {
        menuSequence = null;
        if (!isShowing || !isActiveAndEnabled) return;
        choicePanel.transform.localScale = panelOpenScale;
        canvasGroup.alpha = 1f;
        inputReady = true;
        canvasGroup.interactable = true;
        SetRewardButtonsInteractable(true);
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            HUDRewardCard card = At(tacticalCards, i);
            if (currentOptions[i] == null || card == null) continue;
            card.FinishReveal();
            if (rareRevealParticles) card.PlayRareReveal();
            if (rarityCardShine) card.PlayRarityShine();
        }
        // No default selection: highlight the actual card under the mouse, if any.
        RefreshPointerFocus();
        PlayOfferRevealSound();
        PlayCelebration();
    }

    private static void PlayOptional(AudioClip clip, float volume)
    {
        if (clip != null && volume > 0f) AudioManager.Play2D(clip, Mathf.Clamp01(volume));
    }

    private void PlayCardHoverSound()
    {
        if (!CanChoose || cardHoverSound == null || cardHoverVolume <= 0f || Time.unscaledTime < nextHoverSoundTime) return;
        nextHoverSoundTime = Time.unscaledTime + Mathf.Max(0f, hoverSoundInterval);
        PlayOptional(cardHoverSound, cardHoverVolume);
    }

    private void PlayCelebration()
    {
        if (celebrationRays == null) return;
        bool epic = false, legendary = false;
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            RewardOption option = currentOptions[i];
            if (option == null) continue;
            epic |= option.rarity == ItemRarity.Epic;
            legendary |= option.rarity == ItemRarity.Legendary;
        }
        if (celebrateHighRarity && legendary)
            celebrationRays.Play(legendaryColor, legendaryRayOpacity, celebrationDuration, true);
        else if (celebrateHighRarity && epic)
            celebrationRays.Play(epicColor, epicRayOpacity, celebrationDuration * .8f, true);
        else if (firstOfferInSession && celebrateMenuOpening)
            celebrationRays.Play(playerAccent, openingRayOpacity, celebrationDuration * .7f, true);
    }

    private void PlayOfferRevealSound()
    {
        // Independent of the optional particle/card references. At most one clip per offer.
        // If a Legendary clip is not assigned, an offered Epic may still use its own clip.
        if (rarityRevealVolume <= 0f) return;
        bool epic = false, legendary = false;
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            RewardOption option = currentOptions[i];
            if (option == null) continue;
            if (option.rarity == ItemRarity.Legendary) legendary = true;
            else if (option.rarity == ItemRarity.Epic) epic = true;
        }
        AudioClip clip = legendary && legendaryRevealSound != null ? legendaryRevealSound :
            epic && epicRevealSound != null ? epicRevealSound : null;
        if (clip != null) AudioManager.Play2D(clip, Mathf.Clamp01(rarityRevealVolume));
    }

    private void RefreshPointerFocus(bool forceLayout = true)
    {
        EventSystem es = EventSystem.current;
        if (es == null || !CanChoose) return;
        // A stationary pointer may not produce a new enter event when cards are rebound.
        if (forceLayout) Canvas.ForceUpdateCanvases();
        pointerHits.Clear();
        lastPointerPosition = Input.mousePosition;
        var pointer = new PointerEventData(es) { position = lastPointerPosition };
        es.RaycastAll(pointer, pointerHits);
        HUDRewardCard hit = pointerHits.Count > 0 && pointerHits[0].gameObject != null
            ? pointerHits[0].gameObject.GetComponentInParent<HUDRewardCard>() : null;
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            HUDRewardCard card = At(tacticalCards, i);
            if (card != null && card.isActiveAndEnabled) card.SetPointerHover(card == hit, false);
        }
        pointerHits.Clear();
    }

    // Keyboard users can explicitly enter navigation with an arrow key. No implicit
    // first-card selection; normal EventSystem navigation handles subsequent moves.
    private void LateUpdate()
    {
        if (!CanChoose || EventSystem.current == null) return;
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        bool menuSelected = selected != null && selected.transform.IsChildOf(choicePanel.transform);
        Vector2 pointerPosition = Input.mousePosition;
        if (pointerPosition != lastPointerPosition)
        {
            lastPointerPosition = pointerPosition;
            // Mouse movement takes precedence over a previous keyboard selection.
            // Normally pointer enter/exit handles this; re-raycast only when taking
            // over from keyboard focus (including movement inside the same card).
            if (menuSelected)
            {
                ClearMenuFocus();
                RefreshPointerFocus(false);
                return;
            }
        }
        if (menuSelected) return;
        bool reverse = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.UpArrow);
        if (!reverse && !Input.GetKeyDown(KeyCode.RightArrow) && !Input.GetKeyDown(KeyCode.DownArrow)) return;
        for (int n = 0; n < MaxChoiceCount; n++)
        {
            int index = reverse ? MaxChoiceCount - 1 - n : n;
            Button candidate = At(rewardButtons, index);
            if (candidate == null || !candidate.isActiveAndEnabled || !candidate.IsInteractable()) continue;
            EventSystem.current.SetSelectedGameObject(candidate.gameObject);
            break;
        }
    }

    private void AddEligibleDefinitions(
        List<RewardDefinition> source,
        RewardTarget target,
        List<RewardDefinition> destination)
    {
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            RewardDefinition definition = source[i];
            if (definition == null || !definition.enabled || !IsFinite(definition.selectionWeight) || definition.selectionWeight <= 0f)
                continue;

            if (!TryGetStatValue(definition, target, out float currentValue) || !IsFinite(currentValue))
                continue;
            if (definition.magnitudeMode == MagnitudeMode.PercentOfCurrentValue &&
                Mathf.Approximately(currentValue, 0f) && definition.zeroValueFallback <= 0f)
                continue;

            if (definition.useMinimumValue && currentValue <= definition.minimumValue + 0.0001f && definition.lowerIsBetter)
                continue;

            if (definition.useMaximumValue && currentValue >= definition.maximumValue - 0.0001f && !definition.lowerIsBetter)
                continue;

            // Prevent accidentally offering the same enum stat twice in one level-up.
            if (ContainsSameStat(destination, definition, target))
                continue;

            definition.runtimeTarget = target;
            destination.Add(definition);
        }
    }

    private static bool ContainsSameStat(
        List<RewardDefinition> definitions,
        RewardDefinition candidate,
        RewardTarget target)
    {
        for (int i = 0; i < definitions.Count; i++)
        {
            RewardDefinition existing = definitions[i];
            if (existing.runtimeTarget != target)
                continue;

            if (target == RewardTarget.Player && existing.playerStat.Equals(candidate.playerStat))
                return true;
            if (target == RewardTarget.Vehicle && existing.carStat.Equals(candidate.carStat))
                return true;
        }
        return false;
    }

    private int GetWeightedCandidateIndex(List<RewardDefinition> candidates)
    {
        float totalWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
            totalWeight += Mathf.Max(0f, candidates[i].selectionWeight);

        if (totalWeight <= 0f)
            return UnityEngine.Random.Range(0, candidates.Count);

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= Mathf.Max(0f, candidates[i].selectionWeight);
            if (roll <= 0f)
                return i;
        }

        return candidates.Count - 1;
    }

    // ==================================================
    // STAT ACCESS
    // ==================================================

    private bool TryGetStatValue(RewardDefinition definition, RewardTarget target, out float value)
    {
        value = 0f;

        if (definition == null || inventory == null)
            return false;

        if (target == RewardTarget.Player)
        {
            return inventory.TryGetPlayerStatValue(definition.playerStat, out value);
        }

        return inventory.TryGetCarStatValue(definition.carStat, out value);
    }

    private bool TryApplyReward(RewardOption option)
    {
        if (option == null || option.definition == null || inventory == null)
            return false;

        if (option.target == RewardTarget.Player)
            return inventory.TryAddPlayerStat(option.definition.playerStat, option.delta);

        return inventory.TryAddCarStat(option.definition.carStat, option.delta);
    }

    // ==================================================
    // REWARD GENERATION
    // ==================================================

    private RewardOption GenerateReward(RewardDefinition definition, RewardTarget target)
    {
        if (!TryGetStatValue(definition, target, out float currentValue) || !IsFinite(currentValue))
            return null;

        ItemRarity rarity = RollRarity();
        float magnitude;
        bool usedZeroFallback = false;

        switch (definition.magnitudeMode)
        {
            case MagnitudeMode.PercentOfCurrentValue:
                {
                    Vector2 range = GetPercentRange(definition, rarity);
                    float percentage = UnityEngine.Random.Range(range.x, range.y);

                    if (Mathf.Approximately(currentValue, 0f))
                    {
                        if (definition.zeroValueFallback <= 0f)
                            return null;

                        magnitude = definition.zeroValueFallback * GetZeroFallbackRarityMultiplier(rarity);
                        usedZeroFallback = true;
                    }
                    else
                    {
                        magnitude = Mathf.Abs(currentValue) * percentage;
                    }
                    break;
                }

            case MagnitudeMode.FlatAmount:
                {
                    Vector2 range = GetFlatRange(definition, rarity);
                    magnitude = UnityEngine.Random.Range(range.x, range.y) * definition.flatScale;
                    break;
                }

            case MagnitudeMode.IntegerStep:
                {
                    Vector2Int range = GetIntegerRange(definition, rarity);
                    magnitude = range.y == int.MaxValue
                        ? (float)System.Math.Min(range.y, System.Math.Floor(UnityEngine.Random.value * ((double)range.y - range.x + 1) + range.x))
                        : UnityEngine.Random.Range(range.x, range.y + 1);
                    break;
                }

            default:
                return null;
        }

        if (!IsFinite(magnitude) || magnitude <= 0f)
            return null;
        bool integerReward = definition.roundToInteger || definition.magnitudeMode == MagnitudeMode.IntegerStep;
        if (integerReward)
            magnitude = Mathf.Max(1f, Mathf.Round(magnitude));

        float requestedDelta = definition.lowerIsBetter ? -magnitude : magnitude;
        float resultingValue = currentValue + requestedDelta;

        float minimumAllowed = definition.useMinimumValue ? definition.minimumValue : 0f;
        float maximumAllowed = definition.useMaximumValue ? definition.maximumValue : float.PositiveInfinity;
        if (integerReward)
        {
            minimumAllowed = Mathf.Ceil(minimumAllowed);
            maximumAllowed = Mathf.Floor(maximumAllowed);
            resultingValue = Mathf.Round(resultingValue);
        }
        if (minimumAllowed > maximumAllowed || !IsFinite(resultingValue))
            return null;
        resultingValue = Mathf.Clamp(resultingValue, minimumAllowed, maximumAllowed);
        float actualDelta = resultingValue - currentValue;
        // A cap or unusual input value must never turn an upgrade into a downgrade.
        if (!IsFinite(actualDelta) || (definition.lowerIsBetter ? actualDelta >= -StatEpsilon : actualDelta <= StatEpsilon))
            return null;

        string statName = string.IsNullOrWhiteSpace(definition.displayName)
            ? NicifyEnumName(target == RewardTarget.Player
                ? definition.playerStat.ToString()
                : definition.carStat.ToString())
            : definition.displayName.Trim();

        return new RewardOption
        {
            definition = definition,
            target = target,
            rarity = rarity,
            delta = actualDelta,
            originalValue = currentValue,
            resultingValue = resultingValue,
            vehicleIdentity = target == RewardTarget.Vehicle ? (object)inventory.CurrentCarStats : null,
            amountText = BuildAmountText(definition, currentValue, actualDelta, usedZeroFallback),
            statName = statName
        };
    }

    private Vector2 GetPercentRange(RewardDefinition definition, ItemRarity rarity)
    {
        if (definition.overrideRarityRanges && definition.customPercentRanges != null)
            return definition.customPercentRanges.Get(rarity);
        return percentageRanges.Get(rarity);
    }

    private Vector2 GetFlatRange(RewardDefinition definition, ItemRarity rarity)
    {
        if (definition.overrideRarityRanges && definition.customFlatRanges != null)
            return definition.customFlatRanges.Get(rarity);
        return flatRanges.Get(rarity);
    }

    private Vector2Int GetIntegerRange(RewardDefinition definition, ItemRarity rarity)
    {
        if (definition.overrideRarityRanges && definition.customIntegerRanges != null)
            return definition.customIntegerRanges.Get(rarity);
        return integerRanges.Get(rarity);
    }

    private float GetZeroFallbackRarityMultiplier(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return zeroValueFallbackRarityMultipliers.x;
            case ItemRarity.Rare: return zeroValueFallbackRarityMultipliers.y;
            case ItemRarity.Epic: return zeroValueFallbackRarityMultipliers.z;
            case ItemRarity.Legendary: return zeroValueFallbackRarityMultipliers.w;
            default: return 1f;
        }
    }

    // ==================================================
    // RARITY
    // ==================================================

    private ItemRarity RollRarity()
    {
        float luck = playerStats != null ? Mathf.Max(0f, playerStats.itemLuck) : 0f;
        if (!IsFinite(luck)) luck = 0f;
        float common = Mathf.Max(0f, commonWeight);
        float rare = GetLuckAdjustedWeight(rareWeight, luck, 1);
        float epic = GetLuckAdjustedWeight(epicWeight, luck, 2);
        float legendary = GetLuckAdjustedWeight(legendaryWeight, luck, 3);
        float total = common + rare + epic + legendary;
        if (total <= 0f || !IsFinite(total)) return ItemRarity.Common;
        float roll = UnityEngine.Random.Range(0f, total);
        if (common > 0f && roll < common) return ItemRarity.Common;
        roll -= common;
        if (rare > 0f && roll < rare) return ItemRarity.Rare;
        roll -= rare;
        if (epic > 0f && roll < epic) return ItemRarity.Epic;
        // Random.Range(float,float) includes the upper endpoint.
        if (legendary > 0f) return ItemRarity.Legendary;
        if (epic > 0f) return ItemRarity.Epic;
        if (rare > 0f) return ItemRarity.Rare;
        return ItemRarity.Common;
    }

    private float GetLuckAdjustedWeight(float weight, float luck, int tier)
    {
        // Match PlayerInventory's uncapped, tier-scaled Luck formula.
        return Mathf.Max(0f, weight) * (1f + luck * tier * luckWeightPerPoint);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // ==================================================
    // CARD TEXT AND COLOURS
    // ==================================================

    private string BuildAmountText(
        RewardDefinition definition,
        float currentValue,
        float actualDelta,
        bool usedZeroFallback)
    {
        float actualAmount = Mathf.Abs(actualDelta);
        DisplayMode mode = definition.displayMode;

        if (mode == DisplayMode.Auto)
        {
            if (definition.magnitudeMode == MagnitudeMode.PercentOfCurrentValue &&
                !usedZeroFallback && Mathf.Abs(currentValue) > 0.0001f)
            {
                mode = DisplayMode.PercentageOfCurrentValue;
            }
            else if (definition.roundToInteger || definition.magnitudeMode == MagnitudeMode.IntegerStep)
            {
                mode = DisplayMode.Integer;
            }
            else
            {
                mode = DisplayMode.Number;
            }
        }

        switch (mode)
        {
            case DisplayMode.PercentageOfCurrentValue:
                {
                    if (usedZeroFallback || Mathf.Abs(currentValue) <= StatEpsilon)
                        return FormatAmount(actualAmount);
                    return (actualAmount / Mathf.Abs(currentValue)).ToString("0.##%", CultureInfo.InvariantCulture);
                }

            case DisplayMode.PercentagePoints:
                return FormatAmount(actualAmount * definition.percentagePointsDisplayScale) + " pp";

            case DisplayMode.Integer:
                return FormatAmount(actualAmount);

            default:
                return FormatAmount(actualAmount);
        }
    }

    private static string FormatAmount(float value)
    {
        // Avoid showing tiny but nonzero improvements as zero.
        return value.ToString(Mathf.Abs(value) < 0.01f && value != 0f ? "0.####" : "0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatStatValue(float value, RewardDefinition definition)
    {
        if (definition.displayMode == DisplayMode.PercentagePoints)
            return FormatAmount(value * definition.percentagePointsDisplayScale) + "%";
        return FormatAmount(value);
    }

    private Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return commonColor;
            case ItemRarity.Rare: return rareColor;
            case ItemRarity.Epic: return epicColor;
            case ItemRarity.Legendary: return legendaryColor;
            default: return commonColor;
        }
    }

    private static T At<T>(T[] items, int index) where T : UnityEngine.Object => items != null && index < items.Length ? items[index] : null;

    private void UpdateCardUI()
    {
        // Always visit all three slots so the unused card is hidden in two-choice mode.
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            RewardOption option = i < offeredChoiceCount ? currentOptions[i] : null;
            bool hasOption = option != null;
            TextMeshProUGUI name = At(rewardNameTexts, i);
            TextMeshProUGUI rarity = At(rarityTexts, i);
            TextMeshProUGUI description = At(descriptionTexts, i);
            Image frame = At(rewardFrames, i);
            rewardButtons[i].gameObject.SetActive(hasOption);
            if (!hasOption)
            {
                if (name != null) name.text = string.Empty;
                if (rarity != null) rarity.text = string.Empty;
                if (description != null) description.text = string.Empty;
                continue;
            }
            Color accent = GetRarityColor(option.rarity);
            Color targetColor = option.target == RewardTarget.Player ? playerAccent : vehicleAccent;
            string target = option.target == RewardTarget.Player ? "PLAYER" : "VEHICLE";
            string amount = (option.delta < 0f ? "-" : "+") + option.amountText;
            string before = FormatStatValue(option.originalValue, option.definition);
            string after = FormatStatValue(option.resultingValue, option.definition);
            string green = ColorUtility.ToHtmlStringRGB(improvementColor);
            string comparison = $"{before}  ->  <color=#{green}>{after}</color>";

            HUDRewardCard tactical = At(tacticalCards, i);
            if (tactical != null)
            {
                tactical.SetEnhancedRim(enhancedCardRims);
                tactical.Bind(target, option.statName, option.rarity, amount, comparison,
                    accent, targetColor, titleColor, improvementColor);
                tactical.SetFocusSound(PlayCardHoverSound);
                continue;
            }

            if (name != null)
            {
                name.richText = true;
                name.color = titleColor;
                name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(targetColor)}>{target}</color>  |  {option.statName}";
            }
            if (rarity != null)
            {
                rarity.text = option.rarity.ToString().ToUpperInvariant();
                rarity.color = accent;
            }
            Color background = Color.Lerp(cardBackground, accent, rarityBackgroundTint);
            background.a = cardBackground.a;
            if (frame != null) frame.color = background;
            if (description != null)
            {
                description.richText = true;
                description.color = titleColor;
                string richAmount = $"<color=#{green}><b>{amount}</b></color>";
                description.text = richAmount + "\n" + comparison;
            }
        }
        UpdateRemainingChoicesText();
    }

    private void UpdateRemainingChoicesText()
    {
        // Also refresh before a newly queued/opened reward offer is presented.
        UpdateCurrentLevelText();
        if (remainingChoicesText != null)
        {
            remainingChoicesText.richText = true;
            remainingChoicesText.text = $"<b>{pendingChoices}</b> {(pendingChoices == 1 ? "UPGRADE" : "UPGRADES")} REMAINING";
        }
    }

    private void SetRewardButtonsInteractable(bool value)
    {
        if (rewardButtons != null)
        {
            for (int i = 0; i < Mathf.Min(MaxChoiceCount, rewardButtons.Length); i++)
            {
                if (rewardButtons[i] != null)
                    rewardButtons[i].interactable = value && i < offeredChoiceCount && currentOptions[i] != null;
                HUDRewardCard card = At(tacticalCards, i);
                if (card != null && card.isActiveAndEnabled) card.RefreshInteractionState();
            }
        }
    }

    private static string NicifyEnumName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "Unknown Stat";

        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]))
                builder.Append(' ');
            builder.Append(c);
        }
        return builder.ToString();
    }

    // ==================================================
    // BUTTON CALLBACKS AND SELECTION
    // ==================================================

    private void SelectReward0() => SelectReward(0);
    private void SelectReward1() => SelectReward(1);
    private void SelectReward2() => SelectReward(2);

    private bool CanChoose => isShowing && inputReady && !isClosing && !isApplying;

    private void SelectReward(int index)
    {
        if (!CanChoose || index < 0 || index >= offeredChoiceCount) return;
        RewardOption option = currentOptions[index];
        if (option == null) return;
        if (!IsOfferCurrent(option))
        {
            // Do not apply a delta calculated for a different car or stale stat.
            // Rebuild the offer without consuming the level.
            Debug.LogWarning("PlayerLevelRewardSystem: Stat or vehicle changed while choosing. Refreshing offer.", this);
            BeginClose();
            return;
        }
        isApplying = true;
        SetRewardButtonsInteractable(false);
        bool applied;
        try
        {
            applied = TryApplyReward(option);
        }
        catch
        {
            isApplying = false;
            if (isShowing && inputReady) SetRewardButtonsInteractable(true);
            throw;
        }
        finally
        {
            isApplying = false;
        }
        if (!applied)
        {
            if (isShowing && inputReady)
            {
                SetRewardButtonsInteractable(true);
                RefreshPointerFocus();
            }
            Debug.LogWarning("PlayerLevelRewardSystem: Could not apply selected reward. Check inventory stat mapping.", this);
            return;
        }
        // Applying a stat may invoke other game callbacks (including disabling this component).
        float hold = 0f;
        HUDRewardCard card = At(tacticalCards, index);
        if (isActiveAndEnabled && isShowing && card != null)
        {
            card.Confirm();
            hold = Mathf.Max(0f, selectionHoldSeconds);
        }
        ConsumeChoice(hold);
    }

    private bool IsOfferCurrent(RewardOption option)
    {
        if (inventory == null) return false;
        if (option.target == RewardTarget.Vehicle && (inventory.CurrentCarStats == null ||
            !ReferenceEquals(option.vehicleIdentity, inventory.CurrentCarStats))) return false;
        return TryGetStatValue(option.definition, option.target, out float value) && IsFinite(value) &&
            Mathf.Abs(value - option.originalValue) <= StatEpsilon;
    }

    private void ConsumeChoice(float hold = 0f)
    {
        pendingChoices = Mathf.Max(0, pendingChoices - 1);
        completionSoundPending = pendingChoices == 0;
        UpdateRemainingChoicesText();
        if (isActiveAndEnabled && isShowing) BeginClose(hold);
        else if (isActiveAndEnabled && !isClosing && pendingChoices > 0) ShowNextOffer();
        if (rewardSelectedSound != null) AudioManager.Play2D(rewardSelectedSound);
    }

    private void BeginClose(float hold = 0f)
    {
        isShowing = false;
        isClosing = true;
        inputReady = false;
        SetRewardButtonsInteractable(false);
        canvasGroup.interactable = false;
        if (celebrationRays != null) celebrationRays.Clear();
        for (int i = 0; i < MaxChoiceCount; i++)
        {
            HUDRewardCard card = At(tacticalCards, i);
            if (card != null) card.ClearRarityShine();
        }
        CloseCurrentOffer(hold);
    }

    // ==================================================
    // MENU ANIMATION
    // ==================================================

    private void CloseCurrentOffer(float hold)
    {
        KillMenuTween();
        bool continuing = pendingChoices > 0;
        float duration = Mathf.Max(0f, closeDuration);
        if (duration <= 0f && hold <= 0f)
        {
            FinishClosingOffer(continuing);
            return;
        }
        menuSequence = DOTween.Sequence().SetUpdate(true);
        if (hold > 0f)
        {
            menuSequence.AppendInterval(hold);
            // A successful selection has one confirmed card. Stale/failed offers do not punch.
            HUDRewardCard confirmedCard = null;
            for (int i = 0; i < MaxChoiceCount; i++)
            {
                HUDRewardCard card = At(tacticalCards, i);
                if (currentOptions[i] != null && card != null && card.isActiveAndEnabled && card.IsConfirmed)
                {
                    confirmedCard = card;
                    break;
                }
            }
            if (confirmedCard != null)
            {
                confirmedCard.AddConfirmation(menuSequence, hold, confirmationPunchStrength,
                    confirmationFlashStrength, confirmationNumberPunch);
                if (rewardAtmosphere != null)
                {
                    Color ringTint = atmosphereColor;
                    for (int i = 0; i < MaxChoiceCount; i++)
                    {
                        if (At(tacticalCards, i) != confirmedCard || currentOptions[i] == null) continue;
                        ItemRarity tier = currentOptions[i].rarity;
                        if (tier == ItemRarity.Epic || tier == ItemRarity.Legendary)
                            ringTint = GetRarityColor(tier);
                        break;
                    }
                    rewardAtmosphere.Shockwave((RectTransform)confirmedCard.transform,
                        Mathf.Min(.44f, hold + duration), shockwaveOpacity, ringTint);
                }
                for (int i = 0; i < MaxChoiceCount; i++)
                {
                    HUDRewardCard card = At(tacticalCards, i);
                    if (currentOptions[i] != null && card != null && card != confirmedCard && card.isActiveAndEnabled)
                        card.AddUnchosenDim(menuSequence, hold, unchosenCardOpacity);
                }
            }
        }
        if (duration > 0f)
        {
            if (continuing)
            {
                // Fade the old cards, not the scrim/header. Keep the menu blocking clicks.
                menuSequence.AppendInterval(duration);
                for (int i = 0; i < MaxChoiceCount; i++)
                {
                    HUDRewardCard card = At(tacticalCards, i);
                    if (currentOptions[i] != null && card != null)
                        card.AddExit(menuSequence, Mathf.Max(0f, hold), duration);
                }
            }
            else
            {
                // Fade the scrim at full size; never expose its edges during closing.
                if (KeepBackgroundStationary)
                    menuSequence.Append(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
                else
                {
                    menuSequence.Append(choicePanel.transform
                        .DOScale(panelOpenScale * startScale, duration).SetEase(Ease.InBack));
                    menuSequence.Join(canvasGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
                }
            }
        }
        menuSequence.OnComplete(() => FinishClosingOffer(continuing));
    }

    private void FinishClosingOffer(bool continuing)
    {
        menuSequence = null;
        ClearMenuFocus();
        isClosing = false;
        Array.Clear(currentOptions, 0, currentOptions.Length);
        if (continuing && pendingChoices > 0 && isActiveAndEnabled)
        {
            // Preserve full background, header, pause and cursor state across offers.
            ShowNextOffer();
            return;
        }
        HideChoicePanel();
        if (pendingChoices > 0 && isActiveAndEnabled) ShowNextOffer();
        else
        {
            bool playComplete = completionSoundPending && pendingChoices == 0 && isActiveAndEnabled;
            completionSoundPending = false;
            RestoreGameState();
            if (playComplete) PlayOptional(queueCompleteSound, queueCompleteVolume);
        }
    }

    private void HideChoicePanel()
    {
        if (rewardAtmosphere != null) rewardAtmosphere.Stop();
        if (celebrationRays != null) celebrationRays.Clear();
        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
            choicePanel.transform.localScale = panelOpenScale;
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    // ==================================================
    // GAME PAUSE AND CURSOR
    // ==================================================

    private void SaveAndPauseGame()
    {
        if (!hasSavedGameState)
        {
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            hasSavedGameState = true;
            // Preserve the radio's original visibility through the entire reward queue.
            if (radioUI != null && !transform.IsChildOf(radioUI.transform) &&
                !choicePanel.transform.IsChildOf(radioUI.transform) &&
                !radioUI.transform.IsChildOf(choicePanel.transform))
            {
                hiddenRadioUI = radioUI;
                previousRadioActive = radioUI.activeSelf;
                radioUI.SetActive(false);
            }
        }

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreGameState()
    {
        if (!hasSavedGameState)
            return;

        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        hasSavedGameState = false;
        if (hiddenRadioUI != null) hiddenRadioUI.SetActive(previousRadioActive);
        hiddenRadioUI = null;
    }

    // ==================================================
    // CLEANUP
    // ==================================================

    private void KillMenuTween()
    {
        if (menuSequence == null) return;
        menuSequence.Kill(false);
        menuSequence = null;
    }

    private void ClearMenuFocus()
    {
        if (choicePanel == null || EventSystem.current == null) return;
        GameObject focused = EventSystem.current.currentSelectedGameObject;
        if (focused != null && focused.transform.IsChildOf(choicePanel.transform))
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDisable()
    {
        if (!ReferenceEquals(subscribedLevelSystem, null))
            subscribedLevelSystem.LevelEarned -= OnLevelEarned;
        subscribedLevelSystem = null;

        KillMenuTween();
        ClearMenuFocus();
        completionSoundPending = false;
        if (rewardAtmosphere != null) rewardAtmosphere.Stop();
        if (celebrationRays != null) celebrationRays.Clear();
        inputReady = false;

        isShowing = false;
        isClosing = false;

        if (choicePanel != null)
        {
            choicePanel.SetActive(false);
            choicePanel.transform.localScale = panelOpenScale;
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        RestoreGameState();
    }

    private void OnDestroy()
    {
        KillMenuTween();
        if (listenersRegistered && rewardButtons != null && rewardButtons.Length >= 3)
        {
            if (rewardButtons[0] != null)
                rewardButtons[0].onClick.RemoveListener(SelectReward0);
            if (rewardButtons[1] != null)
                rewardButtons[1].onClick.RemoveListener(SelectReward1);
            if (rewardButtons[2] != null)
                rewardButtons[2].onClick.RemoveListener(SelectReward2);
        }

        RestoreGameState();
    }
}
