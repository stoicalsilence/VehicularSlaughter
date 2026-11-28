using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

/// <summary>Upgrade an existing tactical reward HUD in place. No gameplay or layout replacement.</summary>
public sealed class ProgressionFXInstaller : EditorWindow
{
    private PlayerLevelRewardSystem rewards;
    private PlayerLevelSystem levels;
    private RectTransform xpPanel;
    private TMP_FontAsset font;
    [MenuItem("Tools/HUD Library/Add Progression FX")]
    private static void Open()
    {
        var w=GetWindow<ProgressionFXInstaller>("Progression FX");
        if(Selection.activeGameObject!=null) w.xpPanel=Selection.activeGameObject.GetComponent<RectTransform>();
        if(w.font==null) w.font=TMP_Settings.defaultFontAsset;
        w.minSize=new Vector2(420,250);
    }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Adds rays and shine to the existing reward HUD, and a level-up toast above the selected XP panel. No rebuild. Use Edit Mode.",MessageType.Info);
        rewards=(PlayerLevelRewardSystem)EditorGUILayout.ObjectField("Reward System",rewards,typeof(PlayerLevelRewardSystem),true);
        levels=(PlayerLevelSystem)EditorGUILayout.ObjectField("Level System",levels,typeof(PlayerLevelSystem),true);
        if(levels==null && rewards!=null) levels=rewards.LevelSystem;
        xpPanel=(RectTransform)EditorGUILayout.ObjectField("XP Panel",xpPanel,typeof(RectTransform),true);
        font=(TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font",font,typeof(TMP_FontAsset),false);
        using(new EditorGUI.DisabledScope(Application.isPlaying || rewards==null))
            if(GUILayout.Button("Add reward celebration effects")) Build(true,false);
        using(new EditorGUI.DisabledScope(Application.isPlaying || levels==null || xpPanel==null || font==null))
            if(GUILayout.Button("Add gameplay level-up notification")) Build(false,true);
        using(new EditorGUI.DisabledScope(Application.isPlaying || rewards==null || levels==null || xpPanel==null || font==null))
            if(GUILayout.Button("Install both",GUILayout.Height(32))) Build(true,true);
    }
    private static bool SceneObject(Component c) => c!=null && !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid();
    private void Build(bool cards,bool toast)
    {
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Install Progression FX");
        try
        {
            if(cards) BuildReward();
            if(toast) BuildToast();
            Undo.CollapseUndoOperations(group);
            EditorUtility.DisplayDialog("Progression FX","Installed. Save the scene. Assign optional audio clips on PlayerLevelRewardSystem, then test in Play Mode.","OK");
        }
        catch(System.Exception ex)
        {
            Undo.RevertAllDownToGroup(group); Debug.LogException(ex);
            EditorUtility.DisplayDialog("Progression FX","Installation failed and changes were rolled back. See Console.","OK");
        }
    }
    private void BuildReward()
    {
        if(!SceneObject(rewards)) throw new System.InvalidOperationException("Assign a scene Reward System.");
        var so=new SerializedObject(rewards);
        var root=so.FindProperty("choicePanel").objectReferenceValue as GameObject;
        var slots=so.FindProperty("tacticalCards");
        if(root==null || !(root.transform is RectTransform) || root.scene!=rewards.gameObject.scene || root.GetComponentInParent<Canvas>()==null || slots==null || slots.arraySize<3)
            throw new System.InvalidOperationException("An existing tactical Reward HUD with three configured cards is required.");
        var cards = new HUDRewardCard[3];
        for(int i=0;i<3;i++)
        {
            var card=slots.GetArrayElementAtIndex(i).objectReferenceValue as HUDRewardCard;
            if(card==null || !card.transform.IsChildOf(root.transform)) throw new System.InvalidOperationException("Missing tactical card reference "+i);
            cards[i] = card;
        }
        var rayProp=so.FindProperty("celebrationRays");
        if(rayProp.objectReferenceValue==null)
        {
            var existing=root.transform.Find("CelebrationRays");
            RectTransform rt=existing!=null?existing as RectTransform:Make("CelebrationRays",root.transform);
            if(rt==null) throw new System.InvalidOperationException("CelebrationRays must be a RectTransform.");
            Undo.RecordObject(rt,"Position rays"); Place(rt,new Vector2(1400,1400),new Vector2(0,-35),new Vector2(.5f,.5f)); rt.SetAsFirstSibling();
            var rays=Graphic<HUDRadialRays>(rt); rayProp.objectReferenceValue=rays;
        }
        so.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(rewards);
        for(int i=0;i<3;i++)
        {
            var card=cards[i];
            var co=new SerializedObject(card); var prop=co.FindProperty("rarityShine");
            if(prop.objectReferenceValue!=null) continue;
            Transform existing=card.transform.Find("RarityShine");
            RectTransform rt=existing!=null?existing as RectTransform:Make("RarityShine",card.transform);
            if(rt==null) throw new System.InvalidOperationException("RarityShine must be a RectTransform.");
            Undo.RecordObject(rt,"Position shine"); Stretch(rt); rt.SetAsFirstSibling();
            var shine=Graphic<HUDRewardShine>(rt);
            var sh=new SerializedObject(shine); sh.FindProperty("panel").objectReferenceValue=card.GetComponent<HUDPanel>(); sh.ApplyModifiedProperties();
            prop.objectReferenceValue=shine; co.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(card);
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
    }
    private void BuildToast()
    {
        if(!SceneObject(levels)||!SceneObject(xpPanel)||xpPanel.GetComponentInParent<Canvas>()==null || levels.gameObject.scene!=xpPanel.gameObject.scene)
            throw new System.InvalidOperationException("Assign scene Level System and XP Panel in the same scene.");
        // Sibling of the XP panel? Use a child to track the panel's position and resolution automatically.
        var existing=xpPanel.Find("LevelUpNotification");
        if(existing!=null)
        {
            if(existing.GetComponent<HUDLevelUpNotification>()!=null) return;
            throw new System.InvalidOperationException("A different LevelUpNotification object already exists.");
        }
        var root=Make("LevelUpNotification",xpPanel); Place(root,new Vector2(440,90),new Vector2(0,82),new Vector2(.5f,1));
        var visual=Make("Visual",root); Stretch(visual);
        var cg=Undo.AddComponent<CanvasGroup>(visual.gameObject); cg.alpha=0; cg.blocksRaycasts=false; cg.interactable=false;
        var title=Label("Title",visual,"LEVEL UP  /  LEVEL 12",34,new Color32(215,249,255,255),new Vector2(440,44),new Vector2(0,16));
        var detail=Label("Detail",visual,"+1 UPGRADE BANKED",19,new Color32(112,231,237,255),new Vector2(440,30),new Vector2(0,-21));
        var meter=Undo.AddComponent<HUDLevelUpNotification>(root.gameObject);
        var so=new SerializedObject(meter);
        so.FindProperty("levelSystem").objectReferenceValue=levels;
        so.FindProperty("visualGroup").objectReferenceValue=cg; so.FindProperty("visual").objectReferenceValue=visual;
        so.FindProperty("titleText").objectReferenceValue=title; so.FindProperty("detailText").objectReferenceValue=detail;
        so.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(xpPanel.gameObject.scene);
    }
    private TextMeshProUGUI Label(string name,Transform parent,string value,float size,Color tint,Vector2 dims,Vector2 pos)
    {
        var rt=Make(name,parent); Place(rt,dims,pos,new Vector2(.5f,.5f));
        var t=Undo.AddComponent<TextMeshProUGUI>(rt.gameObject); t.font=font; t.text=value; t.fontSize=size;
        t.color=tint; t.alignment=TextAlignmentOptions.Center; t.enableAutoSizing=true; t.fontSizeMin=size*.65f; t.fontSizeMax=size; t.enableWordWrapping=false; t.raycastTarget=false; return t;
    }
    private static T Graphic<T>(RectTransform rt) where T:UnityEngine.UI.MaskableGraphic
    {
        if(rt.GetComponent<CanvasRenderer>()==null) Undo.AddComponent<CanvasRenderer>(rt.gameObject);
        T g=rt.GetComponent<T>(); if(g==null) g=Undo.AddComponent<T>(rt.gameObject);
        Undo.RecordObject(g,"Configure graphic"); g.color=Color.white; g.raycastTarget=false; g.maskable=false; return g;
    }
    private static RectTransform Make(string name,Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go,"Create "+name);
        Undo.SetTransformParent(go.transform,parent,"Parent "+name); go.layer=parent.gameObject.layer;
        var rt=(RectTransform)go.transform; rt.localRotation=Quaternion.identity; rt.localScale=Vector3.one; rt.localPosition=Vector3.zero; return rt;
    }
    private static void Place(RectTransform rt,Vector2 size,Vector2 pos,Vector2 anchor)
    { rt.anchorMin=rt.anchorMax=anchor; rt.pivot=new Vector2(.5f,.5f); rt.sizeDelta=size; rt.anchoredPosition=pos; }
    private static void Stretch(RectTransform rt)
    { rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one; rt.pivot=new Vector2(.5f,.5f); rt.offsetMin=rt.offsetMax=Vector2.zero; }
}
