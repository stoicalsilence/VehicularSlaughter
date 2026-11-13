
using UnityEngine;

public class ProximityTransparency : MonoBehaviour
{
    [Header("References")]
    private Transform player;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private int materialIndex = 0;

    [Header("Distance")]
    [SerializeField] private float fadeDistance = 3f;
    [SerializeField] private float fadeSpeed = 5f;

    [Header("Transparency")]
    [Range(0f, 1f)]
    [SerializeField] private float invisibleAlpha = 0f;

    [Range(0f, 1f)]
    [SerializeField] private float visibleAlpha = 1f;

    private Material material;
    private Color originalColor;
    private float currentAlpha = 1f;

    private void Awake()
    {
        player = FindObjectOfType<CharacterController>().gameObject.transform;
        material = targetRenderer.materials[materialIndex];

        originalColor = material.color;

        // Enable transparency on Standard shader materials.
        material.SetFloat("_Mode", 3);
        material.SetInt("_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        material.renderQueue = 3000;
    }

    private void Update()
    {
        if (player == null || material == null)
            return;

        float distance = Vector3.Distance(
            player.position,
            transform.position
        );

        float targetAlpha = distance <= fadeDistance
            ? invisibleAlpha
            : visibleAlpha;

        currentAlpha = Mathf.MoveTowards(
            currentAlpha,
            targetAlpha,
            fadeSpeed * Time.deltaTime
        );

        Color color = originalColor;
        color.a = currentAlpha;

        material.color = color;
    }
}
