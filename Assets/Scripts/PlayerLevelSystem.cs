
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

    [Header("Player Stats")]
    [SerializeField]
    private PlayerStats playerStats;

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

    // Both the person and car use the same player's multiplier.
    public float XPGainMultiplier =>
        playerStats != null ? playerStats.xpGain : 1f;

    public event Action XPChanged;
    public event Action<int> BankedLevelsChanged;
    public event Action<int> LevelEarned;

    private void Awake()
    {
        if (playerStats == null)
            playerStats = GetComponent<PlayerStats>();

        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogWarning(
                "PlayerLevelSystem: No PlayerStats assigned. " +
                "XP gain multiplier will default to 1.",
                this
            );
        }
    }

    private void OnValidate()
    {
        baseXPToNextLevel = Mathf.Max(1f, baseXPToNextLevel);
        additionalXPPerLevelEarned =
            Mathf.Max(0f, additionalXPPerLevelEarned);
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
                LevelEarned?.Invoke(totalLevelsEarned - levelsGained + i + 1);
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
}
