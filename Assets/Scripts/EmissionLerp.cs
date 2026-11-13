
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EmissionLerp : MonoBehaviour
{
    [Header("Material")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private int materialIndex = 0;

    [Header("Emission")]
    [SerializeField] private Color emissionColor = Color.white;
    [SerializeField] private float minIntensity = 0f;
    [SerializeField] private float maxIntensity = 5f;

    [Header("Pulse")]
    [Tooltip("Time in seconds to go from Min to Max.")]
    [SerializeField] private float pulseDuration = 2f;

    [Header("Proximity Transparency")]
    [SerializeField] private bool enableProximityTransparency = false;
    private Transform player;
    [SerializeField] private float fadeDistance = 3f;
    [SerializeField] private float fadeSpeed = 5f;

    private Material material;
    private Color originalColor;
    private float currentAlpha = 1f;

    private void Awake()
    {
        material = targetRenderer.materials[materialIndex];

        originalColor = material.color;

        material.EnableKeyword("_EMISSION");

        player = FindObjectOfType<PlayerGun>().gameObject.transform;
    }

    private void Update()
    {
        // Proximity transparency
        if (enableProximityTransparency && player != null)
        {
            float distance = Vector3.Distance(
                player.position,
                transform.position
            );

            float targetAlpha = distance <= fadeDistance ? 0f : 1f;

            currentAlpha = Mathf.MoveTowards(
                currentAlpha,
                targetAlpha,
                fadeSpeed * Time.deltaTime
            );

            Color color = originalColor;
            color.a = currentAlpha;
            material.color = color;
        }
        else
        {
            currentAlpha = 1f;

            Color color = originalColor;
            color.a = 1f;
            material.color = color;
        }

        // Emission
        if (material.color.a <= 0.01f)
        {
            material.SetColor("_EmissionColor", Color.black);
            return;
        }

        float t = Mathf.PingPong(Time.time / pulseDuration, 1f);

        float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        material.SetColor("_EmissionColor", emissionColor * intensity);
    }
}
