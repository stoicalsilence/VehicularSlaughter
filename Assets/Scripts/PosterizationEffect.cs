using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Posterization")]
public class PosterizationEffect : MonoBehaviour
{
    [Header("Posterization Settings")]
    [Range(2, 64)] public int colorLevels = 6;
    [Range(0f, 1f)] public float intensity = 1f;

    private Material material;

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Shader shader = Shader.Find("Hidden/Posterization");
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(source, destination);
                return;
            }

            material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
        }

        material.SetFloat("_Levels", colorLevels);
        material.SetFloat("_Intensity", intensity);
        Graphics.Blit(source, destination, material);
    }

    private void OnDisable()
    {
        if (material != null)
        {
            if (Application.isPlaying)
                Destroy(material);
            else
                DestroyImmediate(material);

            material = null;
        }
    }
}
