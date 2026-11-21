
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public sealed class WishingWellInteractor : MonoBehaviour
{
    [Header("Reward")]
    [Min(0f)]
    public float LuckPerLevel = 3f;

    [Header("Player References")]
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;
    [SerializeField] private PlayerLevelSystem levelSystem;
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private PlayerInventory inventory;

    [Header("UI")]
    [Tooltip("TextMeshProUGUI on the player's canvas.")]
    [SerializeField] private TextMeshProUGUI interactionText;

    [Header("Input")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    private WishingWell currentWell;

    private void Awake()
    {
        if (vehicleInteractor == null)
            vehicleInteractor = GetComponent<PlayerVehicleInteractor>();

        if (levelSystem == null)
            levelSystem = GetComponent<PlayerLevelSystem>();

        if (playerStats == null)
            playerStats = GetComponent<PlayerStats>();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();

        if (inventory == null)
            inventory = GetComponentInParent<PlayerInventory>();

        if (levelSystem == null || playerStats == null)
        {
            Debug.LogError(
                "WishingWellInteractor: Missing level system or player stats.",
                this
            );
        }

        if (inventory == null)
        {
            Debug.LogWarning(
                "WishingWellInteractor: Assign PlayerInventory to show drop chances.",
                this
            );
        }

        if (interactionText == null)
        {
            Debug.LogError(
                "WishingWellInteractor: Assign the interaction prompt.",
                this
            );
        }

        HidePrompt();
    }

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            HidePrompt();
            return;
        }

        Vector3 interactionPosition = GetInteractionPosition();

        currentWell = FindClosestWell(interactionPosition);

        if (currentWell == null)
        {
            HidePrompt();
            return;
        }

        bool canSpend =
            levelSystem != null &&
            playerStats != null &&
            levelSystem.BankedLevels > 0;

        if (canSpend && Input.GetKeyDown(interactKey))
        {
            TryInteract(currentWell, interactionPosition);

            canSpend = levelSystem.BankedLevels > 0;
        }

        ShowPrompt(canSpend);
    }

    // ==========================================
    // PLAYER / VEHICLE POSITION
    // ==========================================

    private Vector3 GetInteractionPosition()
    {
        if (vehicleInteractor != null &&
            vehicleInteractor.IsInVehicle &&
            vehicleInteractor.currentCarController != null)
        {
            return vehicleInteractor
                .currentCarController
                .transform.position;
        }

        return transform.position;
    }

    // ==========================================
    // CLOSEST WELL
    // ==========================================

    private WishingWell FindClosestWell(Vector3 position)
    {
        WishingWell closest = null;
        float closestDistanceSquared = float.PositiveInfinity;

        var wells = WishingWell.ActiveWells;

        for (int i = 0; i < wells.Count; i++)
        {
            WishingWell well = wells[i];

            if (well == null || !well.isActiveAndEnabled)
                continue;

            Vector3 offset = well.InteractionPosition - position;
            float distanceSquared = offset.sqrMagnitude;

            float rangeSquared =
                well.InteractionDistance * well.InteractionDistance;

            if (distanceSquared > rangeSquared)
                continue;

            if (distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            closest = well;
        }

        return closest;
    }

    // ==========================================
    // INTERACTION
    // ==========================================

    private void TryInteract(
        WishingWell well,
        Vector3 sourcePosition)
    {
        if (well == null ||
            levelSystem == null ||
            playerStats == null)
        {
            return;
        }

        if (levelSystem.BankedLevels <= 0)
            return;

        if (!levelSystem.TrySpendLevels(1))
            return;

        playerStats.itemLuck += LuckPerLevel;

        well.PlayTransfer(sourcePosition);
    }

    // ==========================================
    // UI
    // ==========================================




    private void ShowPrompt(bool canSpend)
    {
        if (interactionText == null)
            return;

        string luckAmount = LuckPerLevel.ToString("0.##");

        string actionText = canSpend
            ? $"[{interactKey}] Spend 1 Level Up -> Gain <color=#7DFF91>+{luckAmount} Luck</color>"
            : $"1 Level -> <color=#7DFF91>+{luckAmount} Luck</color>";

        string targetText =
            "<b>Wishin' Well</b>\n" +
            actionText;

        if (inventory != null && playerStats != null)
        {
            float currentLuck = playerStats.itemLuck;
            float futureLuck = currentLuck + LuckPerLevel;

            if (inventory.TryGetRarityDropChance(
                    ItemRarity.Epic, currentLuck, out float epicBefore) &&
                inventory.TryGetRarityDropChance(
                    ItemRarity.Epic, futureLuck, out float epicAfter))
            {
                targetText +=
                    "\n<color=#BFA0FF>Epic Drops:</color> " +
                    $"{epicBefore:0.0}% -> " +
                    $"<color=#7DFF91>{epicAfter:0.0}%</color>";
            }

            if (inventory.TryGetRarityDropChance(
                    ItemRarity.Legendary, currentLuck, out float legendaryBefore) &&
                inventory.TryGetRarityDropChance(
                    ItemRarity.Legendary, futureLuck, out float legendaryAfter))
            {
                targetText +=
                    "\n<color=#FFCA55>Legendary Drops:</color> " +
                    $"{legendaryBefore:0.0}% -> " +
                    $"<color=#7DFF91>{legendaryAfter:0.0}%</color>";
            }
        }

        if (!canSpend)
        {
            targetText +=
                "\n<color=#FF7777>No banked levels</color>";
        }

        if (interactionText.text != targetText)
            interactionText.text = targetText;

        if (!interactionText.gameObject.activeSelf)
            interactionText.gameObject.SetActive(true);
    }




    private void HidePrompt()
    {
        if (interactionText == null)
            return;

        if (interactionText.gameObject.activeSelf)
            interactionText.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        currentWell = null;
        HidePrompt();
    }
}
