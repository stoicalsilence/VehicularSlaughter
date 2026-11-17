
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerLevelSystem : MonoBehaviour
{
    [Header("XP Requirements")]
    [SerializeField, Min(1f)]
    private float baseXPToNextLevel = 100f;

    [SerializeField, Min(0f)]
    private float additionalXPPerLevelEarned = 25f;

    [Header("Player References")]
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerInventory playerInventory;

    [Header("Item Effects")]
    [Tooltip("Additional XP multiplier per banked level and owned CEO Book.")]
    [SerializeField, Min(0f)]
    private float ceoBookXPBonusPerBankedLevel = 0.05f;

    private const string CEOBookItemName =
        "CEO Grindset Self-Help Book";

    private float currentXP;
    private int bankedLevels;
    private int totalLevelsEarned;

    public float CurrentXP => currentXP;

    public float XPRequiredForNextLevel =>
        Mathf.Max(
            1f,
            baseXPToNextLevel +
            additionalXPPerLevelEarned * totalLevelsEarned
        );

    public float XPProgressFraction =>
        Mathf.Clamp01(CurrentXP / XPRequiredForNextLevel);

    public int BankedLevels => bankedLevels;

    // Lifetime levels earned. Spending levels never decreases this.
    public int TotalLevelsEarned => totalLevelsEarned;

    // Permanent XP multiplier from player stats.
    public float BaseXPGainMultiplier =>
        playerStats != null
            ? Mathf.Max(1f, playerStats.xpGain)
            : 1f;

    // Dynamic XP bonus from the CEO Book.
    public float CEOBookXPBonus => GetCEOBookXPBonus();

    // Effective XP multiplier used by both player and vehicle.
    public float XPGainMultiplier =>
        BaseXPGainMultiplier + CEOBookXPBonus;

    public event Action XPChanged;
    public event Action<int> BankedLevelsChanged;
    public event Action<int> LevelEarned;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerInventory == null)
            playerInventory = GetComponentInParent<PlayerInventory>();

        if (playerStats == null)
        {
            Debug.LogWarning(
                "PlayerLevelSystem: No PlayerStats assigned. " +
                "Base XP gain multiplier will default to 1.",
                this
            );
        }

        if (playerInventory == null)
        {
            Debug.LogWarning(
                "PlayerLevelSystem: No PlayerInventory assigned. " +
                "Conditional XP item effects will be disabled.",
                this
            );
        }
    }

    private void OnValidate()
    {
        baseXPToNextLevel = Mathf.Max(1f, baseXPToNextLevel);

        additionalXPPerLevelEarned =
            Mathf.Max(0f, additionalXPPerLevelEarned);

        ceoBookXPBonusPerBankedLevel =
            Mathf.Max(0f, ceoBookXPBonusPerBankedLevel);
    }

    /// <summary>
    /// Adds XP to the progression system.
    /// The amount passed here should already include any XP multiplier.
    /// </summary>
    public void AddXP(float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            return;

        currentXP += amount;

        int levelsGained = 0;

        // Allows one large XP pickup to award multiple levels.
        while (currentXP >= XPRequiredForNextLevel)
        {
            currentXP -= XPRequiredForNextLevel;

            totalLevelsEarned++;
            bankedLevels++;
            levelsGained++;
        }

        if (levelsGained > 0)
        {
            BankedLevelsChanged?.Invoke(bankedLevels);

            for (int i = 0; i < levelsGained; i++)
            {
                LevelEarned?.Invoke(
                    totalLevelsEarned - levelsGained + i + 1
                );
            }
        }

        XPChanged?.Invoke();
    }

    /// <summary>
    /// Attempts to spend one banked level.
    /// Returns false if no levels are available.
    /// </summary>
    public bool TrySpendLevel()
    {
        return TrySpendLevels(1);
    }

    /// <summary>
    /// Attempts to spend the specified number of banked levels.
    /// This does not change lifetime levels earned.
    /// </summary>
    public bool TrySpendLevels(int amount)
    {
        if (amount <= 0 || bankedLevels < amount)
            return false;

        bankedLevels -= amount;

        BankedLevelsChanged?.Invoke(bankedLevels);

        return true;
    }

    // ==================================================
    // ITEM EFFECTS
    // ==================================================

    /// <summary>
    /// CEO Grindset Self-Help Book:
    /// Grants additional XP Gain for every banked level
    /// and every owned stack of the item.
    /// </summary>
    private float GetCEOBookXPBonus()
    {
        if (playerInventory == null || bankedLevels <= 0)
            return 0f;

        int stacks = playerInventory.GetStacksByName(
            CEOBookItemName
        );

        if (stacks <= 0)
            return 0f;

        return bankedLevels *
               stacks *
               Mathf.Max(0f, ceoBookXPBonusPerBankedLevel);
    }
}
