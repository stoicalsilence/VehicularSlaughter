#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>One-click setup; no project scripts need to be replaced.</summary>
[InitializeOnLoad]
internal static class HordeDeathDissolveEditor
{
    private const string Root = "Assets/HordeDeathDissolve/";
    private const string ShaderPath = Root + "Shaders/HordeDeathDissolveBuiltIn.shader";
    private const string MaterialPath = Root + "Resources/HordeDeathDissolveBase.mat";

    static HordeDeathDissolveEditor()
    {
        EditorApplication.delayCall += EnsureMaterial;
    }

    private static void EnsureMaterial()
    {
        if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/'))) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null) return;
        Material material = new Material(shader) { name = "HordeDeathDissolveBase" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();
        Debug.Log("HordeDeathDissolve: Recreated missing Resources material.");
    }

    [MenuItem("Tools/Horde Death Dissolve/Install On All HordeEnemy Prefabs")]
    private static void InstallOnAllPrefabs()
    {
        EnsureMaterial();
        if (!EditorUtility.DisplayDialog("Install Horde Death Dissolve",
            "Add HordeDeathDissolve to every prefab whose ROOT has a HordeEnemy component, including flying enemies? " +
            "This will save those prefab assets; it will not alter HordeEnemy, HordeManager, HordeFlyingAttack or HordeBakedAnimator source code. " +
            "Existing HordeDeathDissolve components and their settings will be preserved.",
            "Install", "Cancel")) return;

        int added = 0;
        int alreadyInstalled = 0;
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null || asset.GetComponent<HordeEnemy>() == null) continue;

            GameObject loaded = null;
            try
            {
                loaded = PrefabUtility.LoadPrefabContents(path);
                if (loaded.GetComponent<HordeDeathDissolve>() != null)
                {
                    alreadyInstalled++;
                    continue;
                }
                loaded.AddComponent<HordeDeathDissolve>();
                PrefabUtility.SaveAsPrefabAsset(loaded, path);
                added++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("HordeDeathDissolve: Could not update " + path + ": " + e.Message);
            }
            finally
            {
                if (loaded != null) PrefabUtility.UnloadPrefabContents(loaded);
            }
        }
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Horde Death Dissolve",
            "Installed on " + added + " enemy prefabs. " + alreadyInstalled +
            " already had the component.\n\nFlying enemy prefabs are supported too. Existing component settings were preserved. Press Play and kill an enemy to test it.", "OK");
    }

    [MenuItem("Tools/Horde Death Dissolve/Check Installation")]
    private static void CheckInstallation()
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (shader != null && material != null && material.shader == shader)
            EditorUtility.DisplayDialog("Horde Death Dissolve", "Shader and Resources material are present. Add the component to enemy prefabs, or use the Install command.", "OK");
        else
            EditorUtility.DisplayDialog("Horde Death Dissolve", "Shader or material is missing. Import the complete package under Assets/HordeDeathDissolve.", "OK");
    }
}
#endif
