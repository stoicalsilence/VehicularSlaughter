
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ForbiddenTomeInteractor : MonoBehaviour
{
    [Header("Player References")]
    [SerializeField]
    private PlayerVehicleInteractor vehicleInteractor;

    [SerializeField]
    private PlayerStats playerStats;

    [Header("UI")]
    [Tooltip("Dedicated TextMeshProUGUI on the player canvas.")]
    [SerializeField]
    private TextMeshProUGUI interactionText;

    [Header("Input")]
    [SerializeField]
    private KeyCode interactKey = KeyCode.F;

    private ForbiddenTome currentTome;

    private void Awake()
    {
        if (vehicleInteractor == null)
        {
            vehicleInteractor =
                GetComponent<PlayerVehicleInteractor>();
        }

        if (playerStats == null)
        {
            playerStats = GetComponent<PlayerStats>();
        }

        if (playerStats == null)
        {
            Debug.LogError(
                "ForbiddenTomeInteractor: Missing PlayerStats.",
                this
            );
        }

        if (interactionText == null)
        {
            Debug.LogError(
                "ForbiddenTomeInteractor: Assign a TMP interaction prompt.",
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

        Vector3 interactionPosition =
            GetInteractionPosition();

        currentTome = FindClosestTome(
            interactionPosition
        );

        if (currentTome == null || playerStats == null)
        {
            HidePrompt();
            return;
        }

        if (currentTome.CanActivate &&
            Input.GetKeyDown(interactKey))
        {
            currentTome.TryActivate(playerStats);
        }

        ShowPrompt(currentTome);
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
    // CLOSEST TOME
    // ==========================================

    private ForbiddenTome FindClosestTome(
        Vector3 position)
    {
        ForbiddenTome closest = null;

        float closestDistanceSquared =
            float.PositiveInfinity;

        var tomes = ForbiddenTome.ActiveTomes;

        for (int i = 0; i < tomes.Count; i++)
        {
            ForbiddenTome tome = tomes[i];

            if (tome == null || !tome.isActiveAndEnabled)
                continue;

            Vector3 offset =
                tome.InteractionPosition - position;

            float distanceSquared =
                offset.sqrMagnitude;

            float rangeSquared =
                tome.InteractionDistance *
                tome.InteractionDistance;

            if (distanceSquared > rangeSquared)
                continue;

            if (distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            closest = tome;
        }

        return closest;
    }

    // ==========================================
    // UI
    // ==========================================

    private void ShowPrompt(ForbiddenTome tome)
    {
        if (interactionText == null || tome == null)
            return;

        float currentXP = playerStats.xpGain;
        float currentDifficulty = playerStats.difficulty;

        float futureXP =
            currentXP + tome.XPGainIncrease;

        float futureDifficulty =
            currentDifficulty + tome.DifficultyIncrease;

        string targetText;

        if (tome.CanActivate)
        {
            targetText =
                "<b>Forbidden Tome</b>\n" +
                $"[{interactKey}] Read\n" +
                "<color=#7DFF91>XP Gain:</color> " +
                $"{currentXP * 100f:0.#}% -> " +
                $"<color=#7DFF91>{futureXP * 100f:0.#}%</color>\n" +
                "<color=#FF7777>Difficulty:</color> " +
                $"{currentDifficulty * 100f:0.#}% -> " +
                $"<color=#FF7777>{futureDifficulty * 100f:0.#}%</color>\n";
        }
        else
        {
            targetText =
                "<b>Forbidden Tome</b>\n" +
                "<color=#FF7777>The forbidden knowledge " +
                "has been consumed.</color>";
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
        currentTome = null;
        HidePrompt();
    }
}
