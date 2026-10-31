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

    private Material material;

    private void Awake()
    {
        material = targetRenderer.materials[materialIndex];
        material.EnableKeyword("_EMISSION");
    }

    private void Update()
    {
        // PingPong goes from 0 -> 1 -> 0 repeatedly.
        float t = Mathf.PingPong(Time.time / pulseDuration, 1f);

        float intensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        material.SetColor("_EmissionColor", emissionColor * intensity);
    }
}
