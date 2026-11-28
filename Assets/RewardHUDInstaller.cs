using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Editor-only hierarchy builder. Uses actual project script GUIDs.</summary>
public sealed class RewardHUDInstaller : EditorWindow
{
    private Canvas canvas;
    private PlayerLevelRewardSystem rewards;
    private TMP_FontAsset font;
    private static readonly Color White = new Color32(244,246,247,255);
    private static readonly Color Muted = new Color32(166,179,187,255);
    private static readonly Color Cyan = new Color32(112,231,237,255);
    private static readonly Color Green = new Color32(117,239,190,255);
    [MenuItem("Tools/HUD Library/Build Reward HUD")]
    private static void Open()
    {
        var w = GetWindow<RewardHUDInstaller>("Reward HUD Setup");
        if (Selection.activeGameObject != null)
        {
            w.canvas = Selection.activeGameObject.GetComponentInParent<Canvas>();
            w.rewards = Selection.activeGameObject.GetComponentInParent<PlayerLevelRewardSystem>();
        }
        if (w.font == null) w.font = TMP_Settings.defaultFontAsset;
        w.minSize = new Vector2(410, 250);
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Tactical Upgrade Menu", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select the gameplay Canvas and existing reward system. Builds a new panel and rewires only the UI fields. The old panel is retained and disabled. Not for Play Mode.", MessageType.Info);
        canvas = (Canvas)EditorGUILayout.ObjectField("Gameplay Canvas", canvas, typeof(Canvas), true);
        rewards = (PlayerLevelRewardSystem)EditorGUILayout.ObjectField("Reward System", rewards, typeof(PlayerLevelRewardSystem), true);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font", font, typeof(TMP_FontAsset), false);
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(Application.isPlaying || canvas == null || rewards == null || font == null))
            if (GUILayout.Button("Build and connect RewardHUD", GUILayout.Height(35))) Build();
    }
    private void Build()
    {
        if (EditorUtility.IsPersistent(canvas) || EditorUtility.IsPersistent(rewards) ||
            !canvas.gameObject.scene.IsValid() || canvas.gameObject.scene != rewards.gameObject.scene)
        { EditorUtility.DisplayDialog("Reward HUD", "Use scene objects in the same scene, not prefab assets.", "OK"); return; }
        if (canvas.transform.Find("TacticalRewardHUD") != null)
        { EditorUtility.DisplayDialog("Reward HUD", "TacticalRewardHUD already exists. Undo or remove it before rebuilding.", "OK"); return; }
        var so = new SerializedObject(rewards);
        string[] required = {"choicePanel", "rewardButtons", "rewardNameTexts", "rarityTexts", "descriptionTexts", "rewardFrames", "remainingChoicesText", "currentLevelText", "tacticalCards"};
        foreach (string key in required)
            if (so.FindProperty(key) == null) { EditorUtility.DisplayDialog("Reward HUD", "Missing field: " + key + ". Install the supplied updated PlayerLevelRewardSystem first.", "OK"); return; }
        var old = so.FindProperty("choicePanel").objectReferenceValue as GameObject;
        if (old != null && (rewards.transform.IsChildOf(old.transform) || canvas.transform.IsChildOf(old.transform)))
        { EditorUtility.DisplayDialog("Reward HUD", "Old choice panel contains the reward controller or Canvas. Move the controller out first.", "OK"); return; }
        if (!EditorUtility.DisplayDialog("Build Reward HUD", "Connect the new menu to the selected reward system and disable the old panel? Reward pools, rarity weights and gameplay references will not be modified.", "Build", "Cancel")) return;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Tactical Reward HUD");
        try
        {
            RectTransform root = Make("TacticalRewardHUD", canvas.transform);
            Stretch(root);
            root.SetAsLastSibling();
            var backdrop = Undo.AddComponent<Image>(root.gameObject);
            backdrop.color = new Color32(8,15,23,213); backdrop.raycastTarget = true;
            var cg = Undo.AddComponent<CanvasGroup>(root.gameObject);
            cg.interactable = false; cg.blocksRaycasts = false;
            // Fixed layout in Canvas reference units; active slots are centered by the layout group.
            RectTransform body = Make("Content", root); Place(body, 1260, 760, 0, 0);
            TextMeshProUGUI level = Text("CurrentLevel", body, "LEVEL 10", 65, Cyan, 1100, 85, 0, 282);
            TextMeshProUGUI remaining = Text("RemainingChoices", body, "9 UPGRADES REMAINING", 26, White, 1100, 45, 0, 217);
            Line("HeaderDivider", body, 650, 2, 0, 186, new Color32(82,113,130,220));
            RectTransform row = Make("Cards", body); Place(row, 1250, 426, 0, -50);
            var layout = Undo.AddComponent<HorizontalLayoutGroup>(row.gameObject);
            layout.spacing = 26; layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            var cards = new HUDRewardCard[3]; var buttons = new Button[3];
            for (int i = 0; i < 3; i++) cards[i] = BuildCard(row, i, out buttons[i]);
            Text("Footer", body, "SELECT ONE UPGRADE TO CONTINUE", 20, Muted, 1100, 40, 0, -314);
            // Wire only the UI fields; preserve configured reward pools and all other settings.
            Undo.RecordObject(rewards, "Connect reward HUD");
            so.Update();
            so.FindProperty("choicePanel").objectReferenceValue = root.gameObject;
            Array(so, "rewardButtons", buttons);
            Array(so, "tacticalCards", cards);
            Array(so, "rewardNameTexts", new Object[3]); Array(so, "rarityTexts", new Object[3]);
            Array(so, "descriptionTexts", new Object[3]); Array(so, "rewardFrames", new Object[3]);
            so.FindProperty("currentLevelText").objectReferenceValue = level;
            so.FindProperty("remainingChoicesText").objectReferenceValue = remaining;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rewards);
            if (old != null) { Undo.RecordObject(old, "Disable legacy reward panel"); old.SetActive(false); PrefabUtility.RecordPrefabInstancePropertyModifications(old); }
            // Leave all three slots available for runtime, and panel inactive in edit mode.
            root.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;
            EditorUtility.DisplayDialog("Reward HUD", "Built and connected. The old panel is retained. Save the scene and test by redeeming banked levels normally. To preview layout, activate TacticalRewardHUD in Edit Mode.", "OK");
        }
        catch (System.Exception ex)
        {
            Undo.RevertAllDownToGroup(group);
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Reward HUD", "Build failed; changes were rolled back. Check Console.", "OK");
        }
    }
    private HUDRewardCard BuildCard(RectTransform parent, int i, out Button button)
    {
        RectTransform card = Make("Reward" + (i+1), parent); Place(card, 380, 426, 0, 0);
        var panel = Undo.AddComponent<HUDPanel>(card.gameObject);
        panel.topLeft = 24; panel.topRight = 14; panel.bottomLeft = 14; panel.bottomRight = 24;
        panel.fillColor = new Color32(24,36,47,247); panel.borderColor = Muted;
        panel.accentColor = Cyan; panel.borderThickness = 1.5f;
        panel.accents = HUDPanel.CornerAccents.TopLeft | HUDPanel.CornerAccents.BottomRight;
        panel.accentLength = 42; panel.raycastTarget = true; panel.color = Color.white;
        button = Undo.AddComponent<Button>(card.gameObject);
        button.targetGraphic = panel; button.transition = Selectable.Transition.None;
        var revealGroup = Undo.AddComponent<CanvasGroup>(card.gameObject);
        RectTransform content = Make("CardContent", card); Stretch(content);
        var rarity = Text("Rarity", content, "COMMON", 24, Muted, 340, 40, 0, 173);
        Line("RarityDivider", content, 288, 2, 0, 146, new Color32(91,112,126,180));
        var category = Text("Category", content, "PLAYER", 20, Cyan, 330, 32, 0, 114);
        var stat = Text("Stat", content, i == 0 ? "Airstrike Damage" : i == 1 ? "Fire Rate" : "Vehicle Maximum Health", 36, White, 330, 92, 0, 53);
        stat.enableAutoSizing = true; stat.fontSizeMin = 22; stat.fontSizeMax = 36;
        stat.enableWordWrapping = true; stat.overflowMode = TextOverflowModes.Ellipsis;
        var amount = Text("Improvement", content, "+10.79%", 54, Green, 340, 68, 0, -43);
        amount.enableAutoSizing = true; amount.fontSizeMin = 24; amount.fontSizeMax = 54;
        var comparison = Text("Comparison", content, "100  ->  <color=#75EFBE>110.79</color>", 25, White, 340, 45, 0, -104);
        comparison.enableAutoSizing = true; comparison.fontSizeMin = 16; comparison.fontSizeMax = 25;
        var rare = Burst("RarityParticles", card, 16, .60f, 105, 4.5f);
        var confirm = Burst("ConfirmParticles", card, 36, .55f, 145, 6f);
        var view = Undo.AddComponent<HUDRewardCard>(card.gameObject);
        var so = new SerializedObject(view);
        Ref(so, "panel", panel); Ref(so,"categoryText",category); Ref(so,"rarityText",rarity);
        Ref(so,"statText",stat); Ref(so,"amountText",amount); Ref(so,"comparisonText",comparison);
        Ref(so,"content",content); Ref(so,"revealGroup",revealGroup);
        Ref(so,"revealParticles",rare); Ref(so,"confirmParticles",confirm); so.ApplyModifiedProperties();
        return view;
    }
    private static HUDParticleBurst Burst(string name, Transform parent, int n, float life, float speed, float size)
    {
        RectTransform rt = Make(name, parent); Stretch(rt);
        // Explicit dependency, including for the case where an older script omitted RequireComponent.
        Undo.AddComponent<CanvasRenderer>(rt.gameObject);
        var fx = Undo.AddComponent<HUDParticleBurst>(rt.gameObject);
        fx.raycastTarget = false; fx.maskable = false; fx.color = Color.white;
        var so = new SerializedObject(fx);
        so.FindProperty("particlesPerBurst").intValue = n;
        so.FindProperty("lifetime").floatValue = life;
        so.FindProperty("riseSpeed").floatValue = speed;
        so.FindProperty("horizontalSpeed").floatValue = 65;
        so.FindProperty("particleSize").floatValue = size;
        so.FindProperty("fadeStart").floatValue = .25f;
        foreach (string key in new[]{"primaryColor","secondaryColor","highlightColor"}) so.FindProperty(key).colorValue = Color.white;
        so.ApplyModifiedProperties(); return fx;
    }
    private TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color, float w, float h, float x, float y)
    {
        RectTransform rt = Make(name, parent); Place(rt,w,h,x,y);
        var t = Undo.AddComponent<TextMeshProUGUI>(rt.gameObject);
        t.font = font; t.text = value; t.fontSize = size; t.color = color;
        t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.richText = true; t.enableWordWrapping = false; return t;
    }
    private static void Line(string name, Transform parent, float w, float h, float x, float y, Color color)
    {
        var rt = Make(name, parent); Place(rt,w,h,x,y);
        var im = Undo.AddComponent<Image>(rt.gameObject); im.color = color; im.raycastTarget = false;
    }
    private static RectTransform Make(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        Undo.SetTransformParent(go.transform, parent, "Parent " + name);
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
        rt.localPosition = Vector3.zero; return rt;
    }
    private static void Place(RectTransform rt, float w, float h, float x, float y)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f,.5f);
        rt.sizeDelta = new Vector2(w,h); rt.anchoredPosition = new Vector2(x,y);
    }
    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(.5f,.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
    private static void Ref(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
    private static void Array(SerializedObject so, string name, Object[] values)
    {
        var p = so.FindProperty(name); p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
