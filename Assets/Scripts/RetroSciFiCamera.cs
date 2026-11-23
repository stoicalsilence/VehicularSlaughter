using UnityEngine;

/// <summary>
/// Built-in Render Pipeline only. One full-screen blit; does not require the
/// Post Processing Stack, depth texture, fog, or URP.
/// Replace the old RetroSciFiCamera.cs with this file (do not keep both).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public sealed class RetroSciFiCamera : MonoBehaviour
{
    [Tooltip("A Material using RetroSciFi/BuiltIn Camera Look")]
    public Material effectMaterial;

    [Tooltip("Use to compare the unmodified camera image without removing the component.")]
    public bool bypassEffect;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (bypassEffect || effectMaterial == null ||
            effectMaterial.shader == null || !effectMaterial.shader.isSupported)
        {
            Graphics.Blit(source, destination);
            return;
        }

        Graphics.Blit(source, destination, effectMaterial);
    }
}
