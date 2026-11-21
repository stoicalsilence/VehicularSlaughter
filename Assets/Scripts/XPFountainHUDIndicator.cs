
using UnityEngine;

public sealed class XPFountainHUDIndicator : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform arrow;
    [SerializeField] private RectTransform canvasRect;

    [Header("Player")]
    [SerializeField] private PlayerVehicleInteractor vehicleInteractor;

    [Header("Settings")]
    [SerializeField, Min(0f)]
    private float edgePadding = 60f;

    [SerializeField]
    private float fountainHeightOffset = 1.5f;

    private void Awake()
    {
        if (canvasRect == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();

            if (canvas != null)
                canvasRect = canvas.rootCanvas
                    .GetComponent<RectTransform>();
        }

        if (vehicleInteractor == null)
            vehicleInteractor =
                FindFirstObjectByType<PlayerVehicleInteractor>();

        if (arrow != null)
            arrow.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (arrow == null || canvasRect == null)
            return;

        HordeManager manager = HordeManager.Instance;

        GameObject fountain = manager != null
            ? manager.ActiveFountain
            : null;

        // Automatically use the correct gameplay camera.
        Camera activeCamera = vehicleInteractor != null
            ? vehicleInteractor.ActiveViewCamera
            : null;

        bool shouldShow =
            fountain != null &&
            fountain.activeInHierarchy &&
            activeCamera != null &&
            activeCamera.isActiveAndEnabled;

        if (arrow.gameObject.activeSelf != shouldShow)
            arrow.gameObject.SetActive(shouldShow);

        if (!shouldShow)
            return;

        UpdateArrow(
            fountain.transform.position,
            activeCamera
        );
    }

    private void UpdateArrow(
        Vector3 targetPosition,
        Camera activeCamera)
    {
        targetPosition += Vector3.up * fountainHeightOffset;

        Vector3 viewportPosition =
            activeCamera.WorldToViewportPoint(targetPosition);

        Vector2 direction = new Vector2(
            viewportPosition.x - 0.5f,
            viewportPosition.y - 0.5f
        );

        // Handle fountains located behind the camera.
        if (viewportPosition.z < 0f)
            direction = -direction;

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.up;

        direction.Normalize();

        float halfWidth = Mathf.Max(
            0f,
            canvasRect.rect.width * 0.5f - edgePadding
        );

        float halfHeight = Mathf.Max(
            0f,
            canvasRect.rect.height * 0.5f - edgePadding
        );

        float xScale = Mathf.Abs(direction.x) > 0.0001f
            ? halfWidth / Mathf.Abs(direction.x)
            : float.PositiveInfinity;

        float yScale = Mathf.Abs(direction.y) > 0.0001f
            ? halfHeight / Mathf.Abs(direction.y)
            : float.PositiveInfinity;

        float distanceToEdge = Mathf.Min(xScale, yScale);

        arrow.anchoredPosition =
            direction * distanceToEdge;

        // Arrow sprite points UP by default.
        float angle = Mathf.Atan2(
            direction.y,
            direction.x
        ) * Mathf.Rad2Deg - 90f;

        arrow.localRotation =
            Quaternion.Euler(0f, 0f, angle);
    }
}
