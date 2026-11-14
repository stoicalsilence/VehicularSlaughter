using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ======================================================
// ITEM DATA
// ======================================================

public enum ItemRarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public enum PlayerStatToModify
{
    None,

    ItemLuck,
    XPGain,
    MaxHealth,
    DamageCooldown,
    ThornsDamage,
    HPRegeneration,

    GroundMoveSpeed,
    GroundAcceleration,
    GroundDeceleration,

    AirMoveSpeed,
    AirAcceleration,
    AirDeceleration,

    StrafeMaxSpeed,
    StrafeAcceleration,
    StrafeDeceleration,

    Friction,
    Gravity,
    JumpForce,
    AirControl,

    GunDamagePerPellet,
    GunPelletsPerShot,
    GunKnockbackForce,
    GunFireRate,
    GunAccuracySpreadDegrees,
    GunCriticalChance,
    GunCriticalDamageMultiplier,
    GunMagazineSize,
    GunReloadDuration,
    GunMaxShotDistance,
    GunPierces
}

public enum ItemStatTarget
{
    Player,
    Car
}

/// <summary>
/// Linear adds the same percentage of the stat's initial value per stack.
/// Hyperbolic converts the accumulated raw percentage into diminishing returns.
/// </summary>
public enum ItemStatStackingMode
{
    Linear,
    Hyperbolic
}

public enum CarStatToModify
{
    None,

    // Driving
    MaxSpeed,
    MaxReverseSpeed,
    AccelerationMultiplier,
    MaxSteeringAngle,
    SteeringSpeed,
    BrakeForce,
    DecelerationMultiplier,
    HandbrakeDriftMultiplier,

    // Durability and enemy impacts
    MaxVehicleHealth,
    MinimumRunOverSpeedKmh,
    MaximumImpactSpeedKmh,
    MinimumEnemyDamage,
    MaximumEnemyDamage,
    MinimumEnemyKnockback,
    MaximumEnemyKnockback,
    MinimumVehicleDamagePerHit,
    MaximumVehicleDamagePerHit,
    EnemyContactDamageCooldown,
    VehicleThornsDamage,
    CarExplosionDamage,
    CarExplosionDamageRange,

    // Mounted turret
    TurretDamagePerShot,
    TurretKnockbackForce,
    TurretFireRate,
    TurretAccuracySpreadDegrees,
    TurretCriticalChance,
    TurretCriticalDamageMultiplier,
    TurretMagazineSize,
    TurretReloadTime,
    TurretDetectionRange,
    TurretMaxShotDistance
}

[Serializable]
public sealed class ItemStatModifier
{
    [Tooltip("Choose whether this modifier affects the player or the currently assigned car.")]
    public ItemStatTarget target = ItemStatTarget.Player;

    // Selects the PlayerStats value when Target is Player.
    [Tooltip("Used when Target is Player.")]
    public PlayerStatToModify playerStat;

    [Tooltip("Used when Target is Car.")]
    public CarStatToModify carStat;

    [Tooltip("Flat amount added for every stack. This is always linear, regardless of stacking mode.")]
    public float flatPerStack;

    [Tooltip("Linear: percentage of the stat's value when this modifier first applies, added per stack. Hyperbolic: raw percentage accumulated across stacks before diminishing-returns conversion. 0.15 = 15%.")]
    [Range(-0.99f, 3f)]
    public float percentPerStack;

    [Tooltip("Linear gives the same percentage bonus per stack. Hyperbolic applies diminishing returns to the accumulated percentage.")]
    public ItemStatStackingMode stackingMode = ItemStatStackingMode.Linear;
}

[Serializable]
public sealed class ItemDefinition
{
    [Header("Display")]
    public string displayName;

    [TextArea(2, 5)]
    public string description;

    public ItemRarity rarity = ItemRarity.Common;

    [Header("Stacking")]
    public bool allowDuplicates = true;

    [Tooltip("0 means unlimited stacks.")]
    [Min(0)]
    public int maxStacks;

    [Header("3D Preview")]
    public GameObject previewPrefab;

    [Header("Stat Effects")]
    public List<ItemStatModifier> statModifiers =
        new List<ItemStatModifier>();
}

// ======================================================
// INVENTORY
// ======================================================

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public sealed class ItemStack
    {
        public ItemDefinition definition;
        public int stackCount;
    }

    // Stores the initial value and accumulated item bonuses for a single stat.
    // We apply only the change in the calculated item bonus, preserving unrelated
    // runtime changes made by other gameplay systems between item pickups.
    private sealed class StatModifierTotals
    {
        public float baseValue;
        public float flatTotal;
        public float linearPercentTotal;
        public float hyperbolicRawPercentTotal;

        public StatModifierTotals(float initialValue)
        {
            baseValue = initialValue;
        }

        public float CalculateValue()
        {
            float hyperbolicPercent =
                PlayerInventory.CalculateHyperbolicPercent(hyperbolicRawPercentTotal);

            float totalPercent = linearPercentTotal + hyperbolicPercent;

            return baseValue * (1f + totalPercent) + flatTotal;
        }

        public void Add(ItemStatModifier modifier, int addedStacks)
        {
            flatTotal += modifier.flatPerStack * addedStacks;

            float percent = modifier.percentPerStack * addedStacks;

            if (modifier.stackingMode == ItemStatStackingMode.Hyperbolic)
                hyperbolicRawPercentTotal += percent;
            else
                linearPercentTotal += percent;
        }
    }

    [Header("Player References")]
    [SerializeField] private PlayerStats playerStats;

    [Header("Vehicle Reference")]
    [Tooltip("Assign the currently controlled vehicle here, or call SetCurrentCarStats when entering/leaving a car.")]
    [SerializeField] private CarStats carStats;

    public CarStats CurrentCarStats => carStats;

    /// <summary>
    /// Assigns the vehicle whose stats should receive Car-targeted item modifiers.
    /// Pass null when the player is no longer using a vehicle.
    /// </summary>
    public void SetCurrentCarStats(CarStats currentCar)
    {
        carStats = currentCar;
        ApplyPendingCarStatModifiers();
    }

    [Header("Item Pool")]
    [SerializeField]
    private List<ItemDefinition> itemPool =
        new List<ItemDefinition>();

    [Header("Rarity Drop Weights")]
    [Tooltip("These are relative weights, not individual item weights.")]
    [Min(0f)] [SerializeField] private float commonWeight = 60f;
    [Min(0f)] [SerializeField] private float rareWeight = 10f;
    [Min(0f)] [SerializeField] private float epicWeight = 4f;
    [Min(0f)] [SerializeField] private float legendaryWeight = 1f;

    [Header("Luck")]
    [Tooltip("Additional rarity weight per luck point and rarity tier.")]
    [Min(0f)]
    [SerializeField] private float luckWeightPerPointPerTier = 0.1f;

    [Header("Banishes")]
    [Min(0)]
    [SerializeField] private int startingBanishes = 1;
    
    [Header("Runtime Inventory")]
    public List<ItemStack> ownedItems =
        new List<ItemStack>();

    private readonly HashSet<ItemDefinition> banishedItems =
        new HashSet<ItemDefinition>();

    // Tracks how many stacks of each item have already been applied to each vehicle.
    // This lets a vehicle receive only newly acquired stacks when the player re-enters it.
    private readonly Dictionary<CarStats, Dictionary<ItemDefinition, int>>
        appliedCarStacksByVehicle =
            new Dictionary<CarStats, Dictionary<ItemDefinition, int>>();

    // Percentage bonuses are calculated against the stat's value when its first
    // item modifier is applied. Separate totals are kept for each player/car stat.
    private readonly Dictionary<PlayerStatToModify, StatModifierTotals>
        playerStatModifierTotals =
            new Dictionary<PlayerStatToModify, StatModifierTotals>();

    private readonly Dictionary<CarStats, Dictionary<CarStatToModify, StatModifierTotals>>
        carStatModifierTotalsByVehicle =
            new Dictionary<CarStats, Dictionary<CarStatToModify, StatModifierTotals>>();

    public AudioClip getCommonSound;
    public AudioClip getRareSound;
    public AudioClip getEpicSound;
    public AudioClip getLegendarySound;
    public AudioClip choirSound;

    /// <summary>
    /// Collected items, grouped by ItemDefinition. Each entry contains the
    /// definition and the player's current stack count for that item.
    /// The returned list cannot be modified through this property.
    /// </summary>
    public IReadOnlyList<ItemStack> OwnedItems => ownedItems;

    public int BanishesRemaining => banishesRemaining;

    private int banishesRemaining;

    public event Action InventoryChanged;

    // Future interaction hooks.
    public event Action<GameObject, float, int> OnItemHit;
    public event Action<GameObject> OnItemKill;

    // ==================================================
    // ITEM CHOICE UI
    // ==================================================

    [Header("Choice Menu")]
    [SerializeField] private GameObject choicePanel;

    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI rarityText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI banishCountText;

    [SerializeField] private Button collectButton;
    [SerializeField] private Button discardButton;
    [SerializeField] private Button banishButton;

    [Header("Choice Menu Animation")]
    [SerializeField, Min(0f)] private float menuOpenDuration = 0.3f;
    [SerializeField, Min(0f)] private float menuCloseDuration = 0.2f;
    [SerializeField, Min(0.01f), Range(0.01f, 1f)] private float menuStartScale = 0.8f;
    [SerializeField] private Ease menuOpenEase = Ease.OutBack;
    [SerializeField] private Ease menuCloseEase = Ease.InBack;

    [Header("3D Preview")]
    [SerializeField] private RawImage previewImage;
    [SerializeField] private Camera previewCamera;
    [SerializeField] private Transform previewRoot;

    [Range(0, 31)]
    [SerializeField] private int previewLayer = 30;

    [SerializeField] private float previewRotationSpeed = 25f;

    private GameObject previewInstance;

    private ItemDefinition offeredItem;
    private ItemCrate sourceCrate;

    private bool isOpen;
    private bool isClosing;

    public bool IsOpen => isOpen;

    private float previousTimeScale;
    private CursorLockMode previousCursorLockState;
    private bool previousCursorVisibility;
    private bool hasSavedGameState;

    private CanvasGroup choiceCanvasGroup;
    private Vector3 choicePanelOpenScale = Vector3.one;
    private Sequence menuSequence;

    [Header("Kill Tracking")]
    [SerializeField, Min(1)] private int cauldronKillsPerHealth = 50;

    private int totalEnemiesKilled;
    private int cauldronBonusHealthGranted;

    public int TotalEnemiesKilled => totalEnemiesKilled;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponent<PlayerStats>();

        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogError(
                "PlayerInventory: PlayerStats could not be found.",
                this);
        }

        banishesRemaining = Mathf.Max(0, startingBanishes);

        if (choicePanel != null)
        {
            choicePanelOpenScale = choicePanel.transform.localScale;
            choiceCanvasGroup = choicePanel.GetComponent<CanvasGroup>();

            if (choiceCanvasGroup == null)
                choiceCanvasGroup = choicePanel.AddComponent<CanvasGroup>();

            choiceCanvasGroup.alpha = 1f;
            choiceCanvasGroup.interactable = true;
            choiceCanvasGroup.blocksRaycasts = true;
            choicePanel.SetActive(false);
        }

        if (collectButton != null)
            collectButton.onClick.AddListener(CollectCurrentItem);

        if (discardButton != null)
            discardButton.onClick.AddListener(DiscardCurrentItem);

        if (banishButton != null)
            banishButton.onClick.AddListener(BanishCurrentItem);

        if (previewCamera != null)
        {
            previewCamera.enabled = false;
            previewCamera.cullingMask = 1 << previewLayer;
        }

        if (previewImage != null && previewCamera != null)
            previewImage.texture = previewCamera.targetTexture;
    }

    private void Update()
    {
        if (!isOpen || previewInstance == null)
            return;

        previewInstance.transform.Rotate(
            Vector3.up,
            previewRotationSpeed * Time.unscaledDeltaTime,
            Space.World);
    }

    // ==================================================
    // CRATE OPENING
    // ==================================================

    public bool TryOpenCrate(ItemCrate crate)
    {
        if (isOpen || isClosing || crate == null || choicePanel == null)
            return false;

        ItemDefinition item = RollItem();

        if (item == null)
        {
            Debug.LogWarning(
                "PlayerInventory: No eligible items remain.",
                this);

            return false;
        }

        OpenChoiceMenu(item, crate);

        return true;
    }

    // ==================================================
    // RARITY AND ITEM SELECTION
    // ==================================================

    private ItemDefinition RollItem()
    {
        // One candidate list per rarity.
        List<ItemDefinition>[] candidatesByRarity =
            new List<ItemDefinition>[5];

        for (int i = 0; i < candidatesByRarity.Length; i++)
            candidatesByRarity[i] = new List<ItemDefinition>();

        // First remove banished, exhausted and otherwise
        // ineligible items.
        foreach (ItemDefinition item in itemPool)
        {
            if (CanAcquire(item))
                candidatesByRarity[(int)item.rarity].Add(item);
        }

        float luck = playerStats != null
            ? Mathf.Max(0f, playerStats.itemLuck)
            : 0f;

        // Calculate the available rarity weights.
        float totalWeight = 0f;
        float[] availableWeights = new float[5];

        for (int i = 0; i < 5; i++)
        {
            // A rarity without eligible items cannot be selected.
            if (candidatesByRarity[i].Count == 0)
                continue;

            ItemRarity rarity = (ItemRarity)i;

            float baseWeight = GetRarityWeight(rarity);

            float luckMultiplier =
                1f + luck * i * luckWeightPerPointPerTier;

            float weight = baseWeight * luckMultiplier;

            availableWeights[i] = Mathf.Max(0f, weight);

            totalWeight += availableWeights[i];
        }

        if (totalWeight <= 0f)
            return null;

        // STAGE 1: Select a rarity.
        float roll = UnityEngine.Random.Range(0f, totalWeight);

        int selectedRarity = -1;

        for (int i = 0; i < 5; i++)
        {
            roll -= availableWeights[i];

            if (roll <= 0f && availableWeights[i] > 0f)
            {
                selectedRarity = i;
                break;
            }
        }

        if (selectedRarity < 0)
        {
            for (int i = 4; i >= 0; i--)
            {
                if (availableWeights[i] > 0f)
                {
                    selectedRarity = i;
                    break;
                }
            }
        }

        if (selectedRarity < 0)
            return null;

        // STAGE 2: Uniformly select an item of that rarity.
        List<ItemDefinition> selectedPool =
            candidatesByRarity[selectedRarity];

        int selectedIndex = UnityEngine.Random.Range(
            0,
            selectedPool.Count);

        return selectedPool[selectedIndex];
    }

    private float GetRarityWeight(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common:
                return commonWeight;

            case ItemRarity.Rare:
                return rareWeight;

            case ItemRarity.Epic:
                return epicWeight;

            case ItemRarity.Legendary:
                return legendaryWeight;
        }

        return 0f;
    }

    // ==================================================
    // ITEM ELIGIBILITY
    // ==================================================

    private bool CanAcquire(ItemDefinition item)
    {
        if (item == null || banishedItems.Contains(item))
            return false;

        int currentStacks = GetStackCount(item);

        if (!item.allowDuplicates && currentStacks > 0)
            return false;

        if (item.maxStacks > 0 &&
            currentStacks >= item.maxStacks)
        {
            return false;
        }

        return true;
    }

    private ItemStack FindStack(ItemDefinition item)
    {
        foreach (ItemStack stack in ownedItems)
        {
            if (stack.definition == item)
                return stack;
        }

        return null;
    }

    public int GetStacksByName(string itemName)
    {
        foreach (ItemStack stack in ownedItems)
        {
            if (stack.definition.displayName.Equals(itemName))
                return stack.stackCount;
        }

        return 0;
    }

    /// <summary>
    /// Returns how many stacks of this exact ItemDefinition instance the player owns.
    /// Returns 0 if the item has not been collected.
    /// </summary>
    public int GetStackCount(ItemDefinition item)
    {
        ItemStack stack = FindStack(item);

        return stack != null ? stack.stackCount : 0;
    }

    /// <summary>
    /// Returns true if the player owns at least one stack of this item.
    /// Pass the same ItemDefinition instance used by this inventory's item pool.
    /// </summary>
    public bool HasItem(ItemDefinition item)
    {
        return GetStackCount(item) > 0;
    }

    public bool HasItemByName(string itemName)
    {
        foreach(ItemStack item in ownedItems)
        {
            if (item.definition.displayName.Equals(itemName))
            {
                return true;
            }
        }

        return false;
    }

    // ==================================================
    // COLLECTION
    // ==================================================

    public bool CollectItem(ItemDefinition item)
    {
        if (!CanAcquire(item))
            return false;

        ItemStack stack = FindStack(item);

        if (stack == null)
        {
            stack = new ItemStack
            {
                definition = item,
                stackCount = 0
            };

            ownedItems.Add(stack);
        }

        stack.stackCount++;

        // Player effects apply immediately. Car effects are applied to the current
        // vehicle if one is assigned, or deferred until SetCurrentCarStats assigns one.
        ApplyPlayerStatModifiers(item, 1);
        ApplyPendingCarStatModifiers();

        // Check whether collecting this item unlocks Cauldron milestones.
        UpdateCauldronBonus();

        InventoryChanged?.Invoke();

        return true;
    }

    // ==================================================
    // STAT MODIFIERS
    // ==================================================

    private void ApplyPlayerStatModifiers(
        ItemDefinition item,
        int addedStacks)
    {
        if (item == null || item.statModifiers == null ||
            playerStats == null || addedStacks <= 0)
            return;

        foreach (ItemStatModifier modifier in item.statModifiers)
        {
            if (modifier == null || modifier.target != ItemStatTarget.Player)
                continue;

            PlayerStatToModify stat = modifier.playerStat;

            if (!playerStatModifierTotals.TryGetValue(stat, out StatModifierTotals totals))
            {
                totals = new StatModifierTotals(GetStatValue(stat, item.displayName));
                playerStatModifierTotals.Add(stat, totals);
            }

            float previousItemBonusValue = totals.CalculateValue();
            totals.Add(modifier, addedStacks);
            float newItemBonusValue = totals.CalculateValue();

            // Apply only the item's incremental contribution. This avoids
            // re-multiplying bonuses already applied on previous pickups.
            float currentValue = GetStatValue(stat, item.displayName);
            SetStatValue(stat, currentValue + (newItemBonusValue - previousItemBonusValue));
        }
    }

    private static float CalculateHyperbolicPercent(float rawPercent)
    {
        // Symmetric diminishing returns: approaches +100% or -100% without
        // reaching either limit or creating a divide-by-zero at -100%.
        return rawPercent / (1f + Mathf.Abs(rawPercent));
    }

    private float GetStatValue(PlayerStatToModify stat, string itemName)
    {
        PlayerStats s = playerStats;

        switch (stat)
        {
            //for nones i set one time trigger items
            case PlayerStatToModify.None:
                if(itemName.Equals("An Actual Rabbit"))
                {
                    FindObjectOfType<Q3Movement.Q3PlayerController>().m_AutoBunnyHop = true;
                }
                return 0f;
            //for nones i set one time trigger items

            case PlayerStatToModify.ItemLuck:
                return s.itemLuck;

            case PlayerStatToModify.XPGain:
                return s.xpGain;

            case PlayerStatToModify.MaxHealth:
                return s.MaxHealth;

            case PlayerStatToModify.DamageCooldown:
                return s.damageCooldown;

            case PlayerStatToModify.ThornsDamage:
                return s.thornsDamage;

            case PlayerStatToModify.HPRegeneration:
                return s.hpRegeneration;

            case PlayerStatToModify.GroundMoveSpeed:
                return s.groundMoveSpeed;

            case PlayerStatToModify.GroundAcceleration:
                return s.groundAcceleration;

            case PlayerStatToModify.GroundDeceleration:
                return s.groundDeceleration;

            case PlayerStatToModify.AirMoveSpeed:
                return s.airMoveSpeed;

            case PlayerStatToModify.AirAcceleration:
                return s.airAcceleration;

            case PlayerStatToModify.AirDeceleration:
                return s.airDeceleration;

            case PlayerStatToModify.StrafeMaxSpeed:
                return s.strafeMaxSpeed;

            case PlayerStatToModify.StrafeAcceleration:
                return s.strafeAcceleration;

            case PlayerStatToModify.StrafeDeceleration:
                return s.strafeDeceleration;

            case PlayerStatToModify.Friction:
                return s.friction;

            case PlayerStatToModify.Gravity:
                return s.gravity;

            case PlayerStatToModify.JumpForce:
                return s.jumpForce;

            case PlayerStatToModify.AirControl:
                return s.airControl;

            case PlayerStatToModify.GunDamagePerPellet:
                return s.gunDamagePerPellet;

            case PlayerStatToModify.GunPelletsPerShot:
                return s.gunPelletsPerShot;

            case PlayerStatToModify.GunKnockbackForce:
                return s.gunKnockbackForce;

            case PlayerStatToModify.GunFireRate:
                return s.gunFireRate;

            case PlayerStatToModify.GunAccuracySpreadDegrees:
                return s.gunAccuracySpreadDegrees;

            case PlayerStatToModify.GunCriticalChance:
                return s.gunCriticalChance;

            case PlayerStatToModify.GunCriticalDamageMultiplier:
                return s.gunCriticalDamageMultiplier;

            case PlayerStatToModify.GunMagazineSize:
                return s.gunMagazineSize;

            case PlayerStatToModify.GunReloadDuration:
                return s.gunReloadDuration;

            case PlayerStatToModify.GunMaxShotDistance:
                return s.gunMaxShotDistance;

            case PlayerStatToModify.GunPierces:
                return s.pierces;
        }

        return 0f;
    }

    private void ApplyPendingCarStatModifiers()
    {
        if (carStats == null)
            return;

        if (!appliedCarStacksByVehicle.TryGetValue(
                carStats,
                out Dictionary<ItemDefinition, int> appliedStacksByItem))
        {
            appliedStacksByItem = new Dictionary<ItemDefinition, int>();
            appliedCarStacksByVehicle.Add(carStats, appliedStacksByItem);
        }

        foreach (ItemStack stack in ownedItems)
        {
            if (stack == null || stack.definition == null || stack.stackCount <= 0)
                continue;

            appliedStacksByItem.TryGetValue(
                stack.definition,
                out int alreadyAppliedStacks);

            int pendingStacks = stack.stackCount - alreadyAppliedStacks;
            if (pendingStacks <= 0)
                continue;

            ApplyCarStatModifiers(stack.definition, pendingStacks);
            appliedStacksByItem[stack.definition] = stack.stackCount;
        }
    }

    private void ApplyCarStatModifiers(ItemDefinition item, int addedStacks)
    {
        if (carStats == null || item == null || item.statModifiers == null ||
            addedStacks <= 0)
            return;

        if (!carStatModifierTotalsByVehicle.TryGetValue(
                carStats,
                out Dictionary<CarStatToModify, StatModifierTotals> totalsByStat))
        {
            totalsByStat = new Dictionary<CarStatToModify, StatModifierTotals>();
            carStatModifierTotalsByVehicle.Add(carStats, totalsByStat);
        }

        foreach (ItemStatModifier modifier in item.statModifiers)
        {
            if (modifier == null || modifier.target != ItemStatTarget.Car)
                continue;

            CarStatToModify stat = modifier.carStat;

            if (!totalsByStat.TryGetValue(stat, out StatModifierTotals totals))
            {
                totals = new StatModifierTotals(GetCarStatValue(stat));
                totalsByStat.Add(stat, totals);
            }

            float previousItemBonusValue = totals.CalculateValue();
            totals.Add(modifier, addedStacks);
            float newItemBonusValue = totals.CalculateValue();

            float currentValue = GetCarStatValue(stat);
            SetCarStatValue(stat, currentValue + (newItemBonusValue - previousItemBonusValue));
        }
    }

    private float GetCarStatValue(CarStatToModify stat)
    {
        CarStats s = carStats;

        switch (stat)
        {
            case CarStatToModify.MaxSpeed: return s.maxSpeed;
            case CarStatToModify.MaxReverseSpeed: return s.maxReverseSpeed;
            case CarStatToModify.AccelerationMultiplier: return s.accelerationMultiplier;
            case CarStatToModify.MaxSteeringAngle: return s.maxSteeringAngle;
            case CarStatToModify.SteeringSpeed: return s.steeringSpeed;
            case CarStatToModify.BrakeForce: return s.brakeForce;
            case CarStatToModify.DecelerationMultiplier: return s.decelerationMultiplier;
            case CarStatToModify.HandbrakeDriftMultiplier: return s.handbrakeDriftMultiplier;

            case CarStatToModify.MaxVehicleHealth: return s.MaxVehicleHealth;
            case CarStatToModify.MinimumRunOverSpeedKmh: return s.minimumRunOverSpeedKmh;
            case CarStatToModify.MaximumImpactSpeedKmh: return s.maximumImpactSpeedKmh;
            case CarStatToModify.MinimumEnemyDamage: return s.minimumEnemyDamage;
            case CarStatToModify.MaximumEnemyDamage: return s.maximumEnemyDamage;
            case CarStatToModify.MinimumEnemyKnockback: return s.minimumEnemyKnockback;
            case CarStatToModify.MaximumEnemyKnockback: return s.maximumEnemyKnockback;
            case CarStatToModify.MinimumVehicleDamagePerHit: return s.minimumVehicleDamagePerHit;
            case CarStatToModify.MaximumVehicleDamagePerHit: return s.maximumVehicleDamagePerHit;
            case CarStatToModify.EnemyContactDamageCooldown: return s.enemyContactDamageCooldown;
            case CarStatToModify.VehicleThornsDamage: return s.vehicleThornsDamage;
            case CarStatToModify.CarExplosionDamage: return s.carExplosionDamage;
            case CarStatToModify.CarExplosionDamageRange: return s.carExplosionDamageRange;

            case CarStatToModify.TurretDamagePerShot: return s.turretDamagePerShot;
            case CarStatToModify.TurretKnockbackForce: return s.turretKnockbackForce;
            case CarStatToModify.TurretFireRate: return s.turretFireRate;
            case CarStatToModify.TurretAccuracySpreadDegrees: return s.turretAccuracySpreadDegrees;
            case CarStatToModify.TurretCriticalChance: return s.turretCriticalChance;
            case CarStatToModify.TurretCriticalDamageMultiplier: return s.turretCriticalDamageMultiplier;
            case CarStatToModify.TurretMagazineSize: return s.turretMagazineSize;
            case CarStatToModify.TurretReloadTime: return s.turretReloadTime;
            case CarStatToModify.TurretDetectionRange: return s.turretDetectionRange;
            case CarStatToModify.TurretMaxShotDistance: return s.turretMaxShotDistance;
        }

        return 0f;
    }

    private void SetCarStatValue(CarStatToModify stat, float value)
    {
        CarStats s = carStats;

        switch (stat)
        {
            // Driving
            case CarStatToModify.MaxSpeed:
                s.maxSpeed = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.MaxReverseSpeed:
                s.maxReverseSpeed = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.AccelerationMultiplier:
                s.accelerationMultiplier = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.MaxSteeringAngle:
                s.maxSteeringAngle = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.SteeringSpeed:
                s.steeringSpeed = Mathf.Max(0.01f, value);
                break;
            case CarStatToModify.BrakeForce:
                s.brakeForce = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.DecelerationMultiplier:
                s.decelerationMultiplier = Mathf.Max(0, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.HandbrakeDriftMultiplier:
                s.handbrakeDriftMultiplier = Mathf.Max(1, Mathf.RoundToInt(value));
                break;

            // Durability and enemy impacts
            case CarStatToModify.MaxVehicleHealth:
                s.MaxVehicleHealth = Mathf.Max(1f, value);
                break;
            case CarStatToModify.MinimumRunOverSpeedKmh:
                s.minimumRunOverSpeedKmh = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MaximumImpactSpeedKmh:
                s.maximumImpactSpeedKmh = Mathf.Max(1f, value);
                break;
            case CarStatToModify.MinimumEnemyDamage:
                s.minimumEnemyDamage = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MaximumEnemyDamage:
                s.maximumEnemyDamage = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MinimumEnemyKnockback:
                s.minimumEnemyKnockback = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MaximumEnemyKnockback:
                s.maximumEnemyKnockback = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MinimumVehicleDamagePerHit:
                s.minimumVehicleDamagePerHit = Mathf.Max(0f, value);
                break;
            case CarStatToModify.MaximumVehicleDamagePerHit:
                s.maximumVehicleDamagePerHit = Mathf.Max(0f, value);
                break;
            case CarStatToModify.EnemyContactDamageCooldown:
                s.enemyContactDamageCooldown = Mathf.Max(0.05f, value);
                break;
            case CarStatToModify.VehicleThornsDamage:
                s.vehicleThornsDamage = Mathf.Max(0f, value);
                break;
            case CarStatToModify.CarExplosionDamage:
                s.carExplosionDamage = Mathf.Max(0f, value);
                break;
            case CarStatToModify.CarExplosionDamageRange:
                s.carExplosionDamageRange = Mathf.Max(0f, value);
                break;

            // Mounted turret
            case CarStatToModify.TurretDamagePerShot:
                s.turretDamagePerShot = Mathf.Max(0f, value);
                break;
            case CarStatToModify.TurretKnockbackForce:
                s.turretKnockbackForce = Mathf.Max(0f, value);
                break;
            case CarStatToModify.TurretFireRate:
                s.turretFireRate = Mathf.Max(0.01f, value);
                break;
            case CarStatToModify.TurretAccuracySpreadDegrees:
                s.turretAccuracySpreadDegrees = Mathf.Clamp(value, 0f, 20f);
                break;
            case CarStatToModify.TurretCriticalChance:
                s.turretCriticalChance = Mathf.Max(0f, value);
                break;
            case CarStatToModify.TurretCriticalDamageMultiplier:
                s.turretCriticalDamageMultiplier = Mathf.Max(1f, value);
                break;
            case CarStatToModify.TurretMagazineSize:
                s.turretMagazineSize = Mathf.Max(1, Mathf.RoundToInt(value));
                break;
            case CarStatToModify.TurretReloadTime:
                s.turretReloadTime = Mathf.Max(0.01f, value);
                break;
            case CarStatToModify.TurretDetectionRange:
                s.turretDetectionRange = Mathf.Max(0f, value);
                break;
            case CarStatToModify.TurretMaxShotDistance:
                s.turretMaxShotDistance = Mathf.Max(0.1f, value);
                break;
        }
    }

    private void SetStatValue(
        PlayerStatToModify stat,
        float value)
    {
        PlayerStats s = playerStats;

        switch (stat)
        {
            case PlayerStatToModify.ItemLuck:
                s.itemLuck = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.XPGain:
                s.xpGain = Mathf.Max(1f, value);
                break;

            case PlayerStatToModify.MaxHealth:
                s.MaxHealth = Mathf.Max(1f, value);
                break;

            case PlayerStatToModify.DamageCooldown:
                s.damageCooldown = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.ThornsDamage:
                s.thornsDamage = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.HPRegeneration:
                s.hpRegeneration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GroundMoveSpeed:
                s.groundMoveSpeed = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GroundAcceleration:
                s.groundAcceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GroundDeceleration:
                s.groundDeceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.AirMoveSpeed:
                s.airMoveSpeed = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.AirAcceleration:
                s.airAcceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.AirDeceleration:
                s.airDeceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.StrafeMaxSpeed:
                s.strafeMaxSpeed = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.StrafeAcceleration:
                s.strafeAcceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.StrafeDeceleration:
                s.strafeDeceleration = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.Friction:
                s.friction = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.Gravity:
                s.gravity = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.JumpForce:
                s.jumpForce = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.AirControl:
                s.airControl = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GunDamagePerPellet:
                s.gunDamagePerPellet = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GunPelletsPerShot:
                s.gunPelletsPerShot = Mathf.Max(
                    1, Mathf.RoundToInt(value));
                break;

            case PlayerStatToModify.GunKnockbackForce:
                s.gunKnockbackForce = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GunFireRate:
                s.gunFireRate = Mathf.Max(0.01f, value);
                break;

            case PlayerStatToModify.GunAccuracySpreadDegrees:
                s.gunAccuracySpreadDegrees = Mathf.Clamp(value, 0f, 30f);
                break;

            case PlayerStatToModify.GunCriticalChance:
                s.gunCriticalChance = Mathf.Max(0f, value);
                break;

            case PlayerStatToModify.GunCriticalDamageMultiplier:
                s.gunCriticalDamageMultiplier = Mathf.Max(1f, value);
                break;

            case PlayerStatToModify.GunMagazineSize:
                s.gunMagazineSize = Mathf.Max(
                    1, Mathf.RoundToInt(value));
                break;

            case PlayerStatToModify.GunReloadDuration:
                s.gunReloadDuration = Mathf.Max(0.01f, value);
                break;

            case PlayerStatToModify.GunMaxShotDistance:
                s.gunMaxShotDistance = Mathf.Max(0.1f, value);
                break;

            case PlayerStatToModify.GunPierces:
                s.pierces = Mathf.Max(0, value);
                break;
        }
    }

    // ==================================================
    // BANISHING
    // ==================================================

    public bool BanishItem(ItemDefinition item)
    {
        if (item == null || banishesRemaining <= 0)
            return false;

        if (!banishedItems.Add(item))
            return false;

        banishesRemaining--;

        InventoryChanged?.Invoke();

        return true;
    }

    public void AddBanishes(int amount)
    {
        if (amount <= 0)
            return;

        banishesRemaining += amount;

        InventoryChanged?.Invoke();
    }

    public bool IsBanished(ItemDefinition item)
    {
        return item != null && banishedItems.Contains(item);
    }

    // ==================================================
    // ITEM CHOICE MENU
    // ==================================================

    private void OpenChoiceMenu(
        ItemDefinition item,
        ItemCrate crate)
    {
        offeredItem = item;
        sourceCrate = crate;

        previousTimeScale = Time.timeScale;
        previousCursorLockState = Cursor.lockState;
        previousCursorVisibility = Cursor.visible;
        hasSavedGameState = true;

        isOpen = true;
        isClosing = false;

        switch (item.rarity)
        {
            case ItemRarity.Common:
                AudioManager.Play2D(getCommonSound);
                break;
            case ItemRarity.Rare:
                AudioManager.Play2D(getRareSound);
                break;
            case ItemRarity.Epic:
                AudioManager.Play2D(getEpicSound);
                break;
            case ItemRarity.Legendary:
                AudioManager.Play2D(getLegendarySound);
                break;
        }

        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UpdateChoiceInformation();
        CreatePreview();

        menuSequence?.Kill();

        choicePanel.SetActive(true);
        choicePanel.transform.localScale = choicePanelOpenScale * menuStartScale;

        if (choiceCanvasGroup != null)
        {
            choiceCanvasGroup.alpha = 0f;
            choiceCanvasGroup.interactable = true;
            choiceCanvasGroup.blocksRaycasts = true;
        }

        // SetUpdate(true) makes this sequence use unscaled time while Time.timeScale is 0.
        menuSequence = DOTween.Sequence().SetUpdate(true);
        menuSequence.Join(
            choicePanel.transform
                .DOScale(choicePanelOpenScale, menuOpenDuration)
                .SetEase(menuOpenEase)).OnComplete( () => {
                    AudioManager.Play2D(choirSound);
                });

        if (choiceCanvasGroup != null)
        {
            menuSequence.Join(
                choiceCanvasGroup
                    .DOFade(1f, menuOpenDuration)
                    .SetEase(Ease.OutQuad));
        }
    }

    private void UpdateChoiceInformation()
    {
        if (itemNameText != null)
        {
            itemNameText.text =
                string.IsNullOrWhiteSpace(offeredItem.displayName)
                ? "Unknown Item"
                : offeredItem.displayName;
        }

        if (rarityText != null)
            rarityText.text = offeredItem.rarity.ToString();

        switch (offeredItem.rarity)
        {
            case ItemRarity.Common:
                rarityText.color = Color.white;
                break;
            case ItemRarity.Rare:
                rarityText.color = Color.blue;
                break;
            case ItemRarity.Epic:
                rarityText.color = Color.magenta;
                break;
            case ItemRarity.Legendary:
                rarityText.color = Color.yellow;
                break;
        }

        if (descriptionText != null)
            descriptionText.text = offeredItem.description;

        if (banishCountText != null)
        {
            banishCountText.text = banishesRemaining.ToString() + " left";
        }

        if (banishButton != null)
            banishButton.interactable = banishesRemaining > 0;
    }

    // ==================================================
    // 3D PREVIEW
    // ==================================================

    private void CreatePreview()
    {
        DestroyPreview();

        if (previewImage != null)
            previewImage.gameObject.SetActive(false);

        // A 3D preview is optional. The choice menu can still be used without one.
        if (offeredItem == null || offeredItem.previewPrefab == null ||
            previewImage == null || previewCamera == null || previewRoot == null)
            return;

        previewInstance = Instantiate(
            offeredItem.previewPrefab,
            previewRoot,
            false);

        previewInstance.transform.localPosition = Vector3.zero;
        previewInstance.transform.localRotation = Quaternion.identity;

        SetLayerRecursively(previewInstance.transform, previewLayer);

        previewCamera.cullingMask = 1 << previewLayer;
        previewCamera.enabled = true;
        previewImage.texture = previewCamera.targetTexture;
        previewImage.gameObject.SetActive(true);
    }

    private void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;

        foreach (Transform child in root)
            SetLayerRecursively(child, layer);
    }

    private void DestroyPreview()
    {
        if (previewInstance != null)
        {
            Destroy(previewInstance);
            previewInstance = null;
        }

        if (previewCamera != null)
            previewCamera.enabled = false;
    }

    // ==================================================
    // PLAYER CHOICES
    // ==================================================

    private void CollectCurrentItem()
    {
        if (!isOpen)
            return;

        if (CollectItem(offeredItem))
            ResolveChoice();
    }

    private void DiscardCurrentItem()
    {
        if (!isOpen)
            return;

        // Discarding does not banish the item.
        ResolveChoice();
    }

    private void BanishCurrentItem()
    {
        if (!isOpen)
            return;

        if (BanishItem(offeredItem))
            ResolveChoice();
    }

    // ==================================================
    // CLOSE MENU
    // ==================================================

    private void ResolveChoice()
    {
        ItemCrate crate = sourceCrate;

        CloseMenu(() =>
        {
            if (crate != null)
                crate.CompleteChoice();
        });
    }

    private void CloseMenu(Action onClosed = null)
    {
        if (!isOpen)
            return;

        isOpen = false;
        isClosing = true;
        menuSequence?.Kill();

        // Keep the game paused until the unscaled close animation finishes.
        if (choicePanel == null || !choicePanel.activeInHierarchy)
        {
            FinishClosingMenu(onClosed);
            return;
        }

        menuSequence = DOTween.Sequence().SetUpdate(true);
        menuSequence.Join(
            choicePanel.transform
                .DOScale(choicePanelOpenScale * menuStartScale, menuCloseDuration)
                .SetEase(menuCloseEase));

        if (choiceCanvasGroup != null)
        {
            menuSequence.Join(
                choiceCanvasGroup
                    .DOFade(0f, menuCloseDuration)
                    .SetEase(Ease.InQuad));
        }

        menuSequence.OnComplete(() =>
        {
            if (choicePanel != null)
            {
                choicePanel.SetActive(false);
                choicePanel.transform.localScale = choicePanelOpenScale;
            }

            if (choiceCanvasGroup != null)
                choiceCanvasGroup.alpha = 1f;

            FinishClosingMenu(onClosed);
        });
    }

    private void FinishClosingMenu(Action onClosed)
    {
        isClosing = false;
        DestroyPreview();

        if (choiceCanvasGroup != null)
        {
            choiceCanvasGroup.interactable = true;
            choiceCanvasGroup.blocksRaycasts = true;
            choiceCanvasGroup.alpha = 1f;
        }

        RestoreGameState();

        offeredItem = null;
        sourceCrate = null;

        onClosed?.Invoke();
    }

    private void RestoreGameState()
    {
        if (!hasSavedGameState)
            return;

        Time.timeScale = previousTimeScale;
        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisibility;
        hasSavedGameState = false;
    }

    // ==================================================
    // FUTURE ITEM INTERACTIONS
    // ==================================================

    public void ReportHit(
        GameObject target,
        float damage,
        int criticalHits)
    {
        OnItemHit?.Invoke(target, damage, criticalHits);
    }

    public void ReportKill(GameObject target)
    {
        if (target == null)
            return;

        totalEnemiesKilled++;

        UpdateCauldronBonus();

        OnItemKill?.Invoke(target);
    }

    private void UpdateCauldronBonus()
    {
        if (playerStats == null || !HasItemByName("Cauldron"))
            return;

        int cauldronStacks = GetStacksByName("Cauldron");

        int killsRequired = Mathf.Max(1, cauldronKillsPerHealth);

        // Calculate the total health bonus earned so far.
        int milestonesReached = totalEnemiesKilled / killsRequired;

        int totalEarnedHealth = milestonesReached * cauldronStacks;

        // Only apply newly earned health.
        int healthToAdd = totalEarnedHealth - cauldronBonusHealthGranted;

        if (healthToAdd <= 0)
            return;

        float currentMaxHealth =
            GetStatValue(PlayerStatToModify.MaxHealth, "Cauldron");

        SetStatValue(
            PlayerStatToModify.MaxHealth,
            currentMaxHealth + healthToAdd);

        cauldronBonusHealthGranted = totalEarnedHealth;

        Debug.Log(
            $"Cauldron: +{healthToAdd} Max HP! " +
            $"Total kills: {totalEnemiesKilled}, " +
            $"Total Cauldron bonus: +{cauldronBonusHealthGranted} HP.",
            this);
    }

    // ==================================================
    // CLEANUP
    // ==================================================

    private void OnDestroy()
    {
        if (collectButton != null)
            collectButton.onClick.RemoveListener(CollectCurrentItem);

        if (discardButton != null)
            discardButton.onClick.RemoveListener(DiscardCurrentItem);

        if (banishButton != null)
            banishButton.onClick.RemoveListener(BanishCurrentItem);

        // A destroyed inventory cannot finish its close tween, so clean up immediately.
        menuSequence?.Kill();
        isOpen = false;
        isClosing = false;
        RestoreGameState();
        DestroyPreview();
    }
}
