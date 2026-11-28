using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class CorruptedCrateHUDInstaller : EditorWindow
{
    private StatTradeSystem system;
    private Canvas canvas;
    private TMP_FontAsset font;
    private AudioClip hoverSound, declineSound;
    private static readonly Color Purple = new Color32(180,125,232,255);
    private static readonly Color Muted = new Color32(181,164,197,255);
    private static readonly Color Green = new Color32(117,239,190,255);
    private static readonly Color Red = new Color32(255,131,131,255);
    [MenuItem("Tools/HUD Library/Build Corrupted Crate HUD")]
    private static void Open() { GetWindow<CorruptedCrateHUDInstaller>("Corrupted Crate HUD"); }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Corrupted Crate HUD",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Builds a two-card trade menu with a decline button. Preserves your existing trade pools and audio. The old panel stays inactive as a backup.",MessageType.Info);
        system=(StatTradeSystem)EditorGUILayout.ObjectField("Trade System",system,typeof(StatTradeSystem),true);
        canvas=(Canvas)EditorGUILayout.ObjectField("Gameplay Canvas",canvas,typeof(Canvas),true);
        font=(TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font",font,typeof(TMP_FontAsset),false);
        hoverSound=(AudioClip)EditorGUILayout.ObjectField("Optional Hover Sound",hoverSound,typeof(AudioClip),false);
        declineSound=(AudioClip)EditorGUILayout.ObjectField("Optional Decline Sound",declineSound,typeof(AudioClip),false);
        using(new EditorGUI.DisabledScope(system==null || canvas==null || font==null || EditorApplication.isPlayingOrWillChangePlaymode))
            if(GUILayout.Button("Build and connect Corrupted Crate HUD",GUILayout.Height(32)))Build();
    }
    private void Build()
    {
        var so=new SerializedObject(system);
        var old=so.FindProperty("choicePanel").objectReferenceValue as GameObject;
        if(EditorUtility.IsPersistent(system)||EditorUtility.IsPersistent(canvas)||system.gameObject.scene!=canvas.gameObject.scene)
        {EditorUtility.DisplayDialog("Corrupted Crate HUD","Select a trade system and Canvas in the same open scene, not prefab assets.","OK");return;}
        if(old!=null && (system.transform.IsChildOf(old.transform)||canvas.transform.IsChildOf(old.transform)))
        {EditorUtility.DisplayDialog("Corrupted Crate HUD","Move the trade system and Canvas outside the old choice panel first.","OK");return;}
        if(canvas.transform.Find("TacticalCorruptedCrateHUD")!=null || so.FindProperty("presentationRoot").objectReferenceValue!=null)
        {EditorUtility.DisplayDialog("Corrupted Crate HUD","A new presentation is already installed. Undo the previous build before rebuilding.","OK");return;}
        Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build Corrupted Crate HUD");
        try
        {
            var root=Node("TacticalCorruptedCrateHUD",canvas.transform);Stretch(root);root.SetAsLastSibling();
            var background=Graphic<Image>(root);background.color=new Color32(14,10,22,182);background.raycastTarget=true;
            Undo.AddComponent<CanvasGroup>(root.gameObject);
            var content=Node("Content",root);Place(content,1040,850,0,20);
            var rayRect=Node("CurseRays",content);Place(rayRect,1200,1000,0,0);
            var rays=Graphic<HUDRadialRays>(rayRect);
            var origin=Node("RevealOrigin",content);Place(origin,960,2,0,250);
            var reveal=Particles(content,"RevealParticles",32,.85f,100,95,5.5f);
            var sigil=Label(content,"WarningSigil","!",44,42,0,375,30,Purple,true);
            // Diamond behind the warning mark; no sprite or font icon required.
            var diamond=Node("WarningFrame",content);Place(diamond,40,40,0,375);diamond.localRotation=Quaternion.Euler(0,0,45);
            var df=Panel(diamond,new Color32(41,28,59,190),Purple,0);df.accents=HUDPanel.CornerAccents.None;diamond.SetSiblingIndex(sigil.transform.GetSiblingIndex());
            var heading=Label(content,"Heading","CORRUPTED CRATE",1000,70,0,310,50,Purple,true);
            Label(content,"Subtitle","Great power comes at a cost.",1000,40,0,253,26,Muted,false);
            var a=MakeCard(content,"OfferA",-248,-37);
            var b=MakeCard(content,"OfferB",248,-37);
            var decline=MakeDecline(content);
            Label(content,"DeclineHint","Leave without making a trade. The crate is spent.",900,30,0,-411,18,Muted,false);
            // Keep reveal particles above the static card frames and labels, but never block clicks.
            reveal.transform.SetAsLastSibling();
            Undo.RecordObject(system,"Connect Corrupted Crate HUD");so.Update();
            Assign(so,"choicePanel",root.gameObject);Assign(so,"presentationRoot",content);
            Assign(so,"curseRays",rays);Assign(so,"revealParticles",reveal);Assign(so,"revealOrigin",origin);
            Assign(so,"menuHeading",heading);Assign(so,"declineCard",decline);Assign(so,"declineButton",decline.button);
            Assign(so,"optionA.button",a.button);Assign(so,"optionA.card",a);Assign(so,"optionA.description",null);
            Assign(so,"optionB.button",b.button);Assign(so,"optionB.card",b);Assign(so,"optionB.description",null);
            if(hoverSound!=null)Assign(so,"hoverSound",hoverSound);
            if(declineSound!=null)Assign(so,"declineSound",declineSound);
            so.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(system);
            if(old!=null){Undo.RecordObject(old,"Deactivate old trade menu");old.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(old);}
            root.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(system.gameObject.scene);Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject=root.gameObject;
            EditorUtility.DisplayDialog("Corrupted Crate HUD","Built and connected. Save the scene and test by opening a corrupted crate. Declining consumes the crate without stat changes.","OK");
        }
        catch(Exception ex){Undo.RevertAllDownToGroup(undo);Debug.LogException(ex);EditorUtility.DisplayDialog("Corrupted Crate HUD","Installation rolled back: "+ex.Message,"OK");}
    }
    private static void Assign(SerializedObject so,string field,UnityEngine.Object value)
    {var p=so.FindProperty(field);if(p==null)throw new InvalidOperationException("Missing serialized field: "+field);p.objectReferenceValue=value;}
    private static RectTransform Node(string name,Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Create "+name);
        Undo.SetTransformParent(go.transform,parent,"Parent "+name);go.layer=parent.gameObject.layer;
        var r=(RectTransform)go.transform;r.localRotation=Quaternion.identity;r.localScale=Vector3.one;r.localPosition=Vector3.zero;return r;
    }
    private static void Place(RectTransform r,float w,float h,float x,float y)
    {r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);}
    private static void Stretch(RectTransform r)
    {r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;}
    private static T Graphic<T>(RectTransform r) where T:UnityEngine.UI.Graphic
    {
        if(r.GetComponent<CanvasRenderer>()==null)Undo.AddComponent<CanvasRenderer>(r.gameObject);
        var g=Undo.AddComponent<T>(r.gameObject);g.color=Color.white;g.raycastTarget=false;return g;
    }
    private static HUDPanel Panel(RectTransform r,Color fill,Color accent,float chamfer)
    {
        var p=Graphic<HUDPanel>(r);p.fillColor=fill;p.borderColor=accent;p.accentColor=accent;
        p.topLeft=p.topRight=p.bottomLeft=p.bottomRight=chamfer;p.borderThickness=1.3f;
        p.accents=HUDPanel.CornerAccents.TopLeft|HUDPanel.CornerAccents.BottomRight;p.accentLength=36;p.Refresh();return p;
    }
    private TextMeshProUGUI Label(RectTransform parent,string name,string text,float w,float h,float x,float y,float fs,Color color,bool bold)
    {
        var r=Node(name,parent);Place(r,w,h,x,y);var t=Graphic<TextMeshProUGUI>(r);
        t.font=font;t.fontSize=fs;t.color=color;t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;
        t.alignment=TextAlignmentOptions.Center;t.enableWordWrapping=true;t.richText=true;t.text=text;return t;
    }
    private HUDStatTradeCard MakeCard(RectTransform parent,string name,float x,float y)
    {
        var r=Node(name,parent);Place(r,464,540,x,y);
        var c=MakeButton(r,Purple,new Color32(33,28,46,247),22);
        c.benefitCategory=Label(r,"BenefitCategory","BENEFIT  /  PLAYER",418,30,0,231,20,Green,true);
        c.benefitAmount=Label(r,"BenefitAmount","+100%",418,78,0,168,62,Green,true);
        c.benefitName=Label(r,"BenefitName","XP Gain Multiplier",418,64,0,96,31,Color.white,true);
        c.benefitComparison=Label(r,"BenefitComparison","1 → 2",418,40,0,38,25,new Color32(205,216,224,255),false);
        var line=Node("TradeDivider",r);Place(line,394,1.5f,0,0);Graphic<Image>(line).color=new Color32(114,93,136,255);
        var center=Node("TradeDiamond",r);Place(center,12,12,0,0);center.localRotation=Quaternion.Euler(0,0,45);Graphic<Image>(center).color=Purple;
        c.costCategory=Label(r,"CostCategory","COST  /  VEHICLE",418,30,0,-39,20,Red,true);
        c.costAmount=Label(r,"CostAmount","-50%",418,78,0,-103,62,Red,true);
        c.costName=Label(r,"CostName","Turret Knockback",418,64,0,-173,31,Color.white,true);
        c.costComparison=Label(r,"CostComparison","5 → 2.5",418,40,0,-230,25,new Color32(205,216,224,255),false);
        // Wrap and autosize unusually long stat labels without affecting the other text.
        foreach(var t in new[]{c.benefitName,c.costName}){t.enableAutoSizing=true;t.fontSizeMin=20;t.fontSizeMax=31;}
        foreach(var t in new[]{c.benefitAmount,c.costAmount}){t.enableAutoSizing=true;t.fontSizeMin=30;t.fontSizeMax=62;}
        c.confirmationParticles=Particles(r,"ConfirmationParticles",44,.65f,155,105,6.5f);
        return c;
    }
    private HUDStatTradeCard MakeDecline(RectTransform parent)
    {
        var r=Node("Decline",parent);Place(r,320,58,0,-359);
        var c=MakeButton(r,new Color32(166,179,187,255),new Color32(30,30,40,247),10);
        Label(r,"Label","DECLINE",300,50,0,0,25,new Color32(220,227,233,255),true);return c;
    }
    private static HUDStatTradeCard MakeButton(RectTransform r,Color accent,Color fill,float chamfer)
    {
        var p=Panel(r,fill,accent,chamfer);p.raycastTarget=true;
        var g=Undo.AddComponent<CanvasGroup>(r.gameObject);
        var b=Undo.AddComponent<Button>(r.gameObject);b.targetGraphic=p;b.transition=Selectable.Transition.None;
        var c=Undo.AddComponent<HUDStatTradeCard>(r.gameObject);c.frame=p;c.button=b;c.group=g;c.accent=accent;c.baseFill=fill;return c;
    }
    private static HUDParticleBurst Particles(RectTransform parent,string name,int count,float life,float rise,float spread,float size)
    {
        var r=Node(name,parent);Stretch(r);var p=Graphic<HUDParticleBurst>(r);
        var so=new SerializedObject(p);
        so.FindProperty("particlesPerBurst").intValue=count;so.FindProperty("lifetime").floatValue=life;
        so.FindProperty("riseSpeed").floatValue=rise;so.FindProperty("horizontalSpeed").floatValue=spread;so.FindProperty("particleSize").floatValue=size;
        so.FindProperty("primaryColor").colorValue=Color.white;so.FindProperty("secondaryColor").colorValue=Color.white;so.FindProperty("highlightColor").colorValue=Color.white;
        so.FindProperty("fadeStart").floatValue=.28f;so.ApplyModifiedPropertiesWithoutUndo();return p;
    }
}
