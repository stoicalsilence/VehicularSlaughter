using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-local ghost pool. Original HordeEnemy instances remain pooled as before.</summary>
[DisallowMultipleComponent]
public sealed class HordeDeathDissolvePool : MonoBehaviour
{
    // One frame can contain several visible MeshRenderers. Cap active death meshes
    // globally to avoid unbounded cost during exceptional horde-wide kills.
    private const int MaxActiveDeathMeshes = 48;

    private static HordeDeathDissolvePool instance;
    private readonly Stack<HordeDeathGhost> inactive = new Stack<HordeDeathGhost>();
    private Material dissolveMaterial;
    private int activeCount;

    internal static HordeDeathDissolvePool GetOrCreate()
    {
        if (instance != null) return instance;
        GameObject root = new GameObject("Horde Death Dissolve (pooled visuals)");
        // Keep this pool local to the game scene. Never DontDestroyOnLoad.
        if (HordeManager.Instance != null)
            root.transform.SetParent(HordeManager.Instance.transform, false);
        return root.AddComponent<HordeDeathDissolvePool>();
    }

    private void Awake()
    {
        instance = this;
        dissolveMaterial = Resources.Load<Material>("HordeDeathDissolveBase");
        if (dissolveMaterial == null)
        {
            // Editor safety fallback. For player builds keep the supplied material
            // in the Resources directory so the shader is included in the build.
            Shader shader = Shader.Find("Horde/DeathDissolveBuiltIn");
            if (shader != null)
            {
                dissolveMaterial = new Material(shader);
                dissolveMaterial.name = "Horde Death Dissolve Runtime Material";
            }
            else
                Debug.LogError("HordeDeathDissolve: Missing Resources/HordeDeathDissolveBase material or dissolve shader.", this);
        }
    }

    internal bool TrySpawn(Renderer source, Material[] baseMaterials,
        bool isEMP, Color empTint, float originY, float seed, Color edgeColor,
        float duration, float noiseScale, float edgeWidth, float brightness,
        float riseBias, float heightScale, float fallSpeed)
    {
        if (dissolveMaterial == null || activeCount >= MaxActiveDeathMeshes) return false;

        HordeDeathGhost ghost = null;
        while (inactive.Count > 0 && ghost == null)
            ghost = inactive.Pop();
        if (ghost == null)
        {
            GameObject go = new GameObject("Death Ghost");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            ghost = go.AddComponent<HordeDeathGhost>();
        }

        if (!ghost.Begin(source, baseMaterials, dissolveMaterial, isEMP,
            empTint, originY, seed, edgeColor, duration, noiseScale,
            edgeWidth, brightness, riseBias, heightScale, fallSpeed, this))
        {
            ghost.gameObject.SetActive(false);
            inactive.Push(ghost);
            return false;
        }
        activeCount++;
        return true;
    }

    internal void Release(HordeDeathGhost ghost)
    {
        activeCount = Mathf.Max(0, activeCount - 1);
        if (ghost == null) return;
        ghost.gameObject.SetActive(false);
        ghost.transform.SetParent(transform, false);
        inactive.Push(ghost);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (dissolveMaterial != null && dissolveMaterial.name == "Horde Death Dissolve Runtime Material")
            Destroy(dissolveMaterial);
    }
}
