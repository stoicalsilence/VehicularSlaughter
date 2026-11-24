
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(HordeEnemy))]
public sealed class HordeEliteIndicator : MonoBehaviour
{
    [Header("Indicator")]
    [Tooltip("SpriteRenderer child positioned above the enemy's head.")]
    [SerializeField] private SpriteRenderer crownRenderer;

    [Header("Billboard")]
    [SerializeField] private bool faceCamera = true;

    private HordeEnemy enemy;
    private Transform crownTransform;
    private Transform cameraTransform;

    private void Awake()
    {
        enemy = GetComponent<HordeEnemy>();

        if (crownRenderer != null)
        {
            crownTransform = crownRenderer.transform;
            crownRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        // HordeManager sets the variant before activating
        // the pooled enemy, so this is safe.
        if (enemy == null)
            enemy = GetComponent<HordeEnemy>();

        if (crownRenderer == null)
            return;

        crownRenderer.enabled = enemy.IsElite;

        if (enemy.IsElite && faceCamera)
        {
            Camera cam = Camera.main;
            cameraTransform = cam != null ? cam.transform : null;
        }
    }

    private void LateUpdate()
    {
        if (!faceCamera ||
            crownRenderer == null ||
            !crownRenderer.enabled)
            return;

        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            cameraTransform = cam.transform;
        }

        // Keep the crown facing the camera.
        crownTransform.rotation = cameraTransform.rotation;
    }

    private void OnDisable()
    {
        if (crownRenderer != null)
            crownRenderer.enabled = false;
    }
}
