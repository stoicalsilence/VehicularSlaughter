using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

public sealed class ItemDiscoveryHUDInstaller : EditorWindow
{
    private PlayerInventory inventory;
    private Canvas canvas;
    private TMP_FontAsset font;
    private bool includeDiscard;
    private bool autoFrame=true;
    private AudioClip hoverSound;
    [MenuItem("Tools/HUD Library/Build Item Discovery HUD")]
    private static void Open()
    {
        var w=GetWindow<ItemDiscoveryHUDInstaller>("Item Discovery HUD");
        w.minSize=new Vector2(430,300);
        if(Selection.activeGameObject!=null)w.inventory=Selection.activeGameObject.GetComponentInParent<PlayerInventory>();
        w.SuggestReferences();
    }
    private void SuggestReferences()
    {
        if(inventory!=null)
        {
            var so=new SerializedObject(inventory);
            var old=so.FindProperty("choicePanel")?.objectReferenceValue as GameObject;
            if(canvas==null && old!=null)canvas=old.GetComponentInParent<Canvas>(true);
            var name=so.FindProperty("itemNameText")?.objectReferenceValue as TMP_Text;
            if(font==null && name!=null)font=name.font;
            var discard=so.FindProperty("discardButton")?.objectReferenceValue as Button;
            includeDiscard=discard!=null && VisibleWithin(discard.transform,old!=null?old.transform:null);
        }
        if(font==null)font=TMP_Settings.defaultFontAsset;
    }
    private static bool VisibleWithin(Transform t,Transform root)
    {for(;t!=null&&t!=root;t=t.parent)if(!t.gameObject.activeSelf)return false;return t==root&&root!=null;}
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Builds a compact modal and rewires only the inventory's choice UI. The old panel stays in the scene, inactive. Keeps the existing preview camera, root and item prefabs.",MessageType.Info);
        EditorGUI.BeginChangeCheck();
        inventory=(PlayerInventory)EditorGUILayout.ObjectField("Player Inventory",inventory,typeof(PlayerInventory),true);
        if(EditorGUI.EndChangeCheck()){canvas=null;font=null;SuggestReferences();}
        canvas=(Canvas)EditorGUILayout.ObjectField("Gameplay Canvas",canvas,typeof(Canvas),true);
        font=(TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font",font,typeof(TMP_FontAsset),false);
        autoFrame=EditorGUILayout.Toggle("Automatic Preview Framing",autoFrame);
        includeDiscard=EditorGUILayout.Toggle("Include Discard Button",includeDiscard);
        hoverSound=(AudioClip)EditorGUILayout.ObjectField("Optional Hover Sound",hoverSound,typeof(AudioClip),false);
        using(new EditorGUI.DisabledScope(Application.isPlaying||inventory==null||canvas==null||font==null))
            if(GUILayout.Button("Build and connect Item Discovery HUD",GUILayout.Height(36)))Build();
    }
    private static void Assert(bool condition,string error){if(!condition)throw new InvalidOperationException(error);}
    private void Build()
    {
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Build Item Discovery HUD");
        try
        {
            Assert(!EditorUtility.IsPersistent(inventory)&&!EditorUtility.IsPersistent(canvas)&&inventory.gameObject.scene==canvas.gameObject.scene,"Choose scene objects in the same scene.");
            var so=new SerializedObject(inventory);
            var old=so.FindProperty("choicePanel").objectReferenceValue as GameObject;
            Assert(so.FindProperty("discoveryHUD")!=null,"Replace PlayerInventory.cs with the supplied version first.");
            Assert(so.FindProperty("discoveryHUD").objectReferenceValue==null,"Inventory already has a discovery HUD. Use Undo or remove its reference before installing another.");
            Assert(canvas.transform.Find("TacticalItemDiscoveryHUD")==null,"TacticalItemDiscoveryHUD already exists under this Canvas.");
            Assert(old==null||(!inventory.transform.IsChildOf(old.transform)&&!canvas.transform.IsChildOf(old.transform)),"Inventory and Canvas must live outside the old panel.");
            var camera=so.FindProperty("previewCamera").objectReferenceValue as Camera;
            var previewRoot=so.FindProperty("previewRoot").objectReferenceValue as Transform;
            Assert(old==null||((camera==null||!camera.transform.IsChildOf(old.transform))&&(previewRoot==null||!previewRoot.IsChildOf(old.transform))),"The existing preview camera/root is inside the old UI. Move the preview rig outside that panel, preserving world transforms, then rerun the installer.");
            var root=Node("TacticalItemDiscoveryHUD",canvas.transform); Stretch(root);
            var overlay=Graphic<Image>(root); overlay.color=new Color(.025f,.045f,.06f,.62f);overlay.raycastTarget=true;
            var groupComponent=Undo.AddComponent<CanvasGroup>(root.gameObject); groupComponent.interactable=false;groupComponent.blocksRaycasts=false;
            var hud=Undo.AddComponent<HUDItemDiscovery>(root.gameObject);hud.automaticFraming=autoFrame;hud.hoverSound=hoverSound;
            var card=Node("ItemCard",root);Place(card,660,820,0,0);hud.card=card;
            hud.frame=Panel(card,new Color(.085f,.13f,.17f,.96f),new Color32(112,201,244,255),22);
            var shine=Node("RarityShine",card);Stretch(shine);hud.shine=Graphic<HUDRewardShine>(shine);
            SetObject(hud.shine,"panel",hud.frame);
            var halo=Node("DisplayHalo",card);Place(halo,520,326,0,167);hud.halo=Graphic<HUDItemHalo>(halo);
            var rays=Node("CelebrationRays",card);Place(rays,620,620,0,135);hud.rays=Graphic<HUDRadialRays>(rays);
            var mount=Node("PreviewMount",card);Place(mount,448,270,0,175);hud.previewMount=mount;
            var raw=Node("ItemPreview",mount);Stretch(raw);hud.previewImage=Graphic<RawImage>(raw);hud.previewImage.color=Color.white;
            if(camera!=null)hud.previewImage.texture=camera.targetTexture;
            var fitter=Undo.AddComponent<AspectRatioFitter>(raw.gameObject);fitter.aspectMode=AspectRatioFitter.AspectMode.FitInParent;
            if(camera!=null && camera.targetTexture!=null && camera.targetTexture.height>0)fitter.aspectRatio=(float)camera.targetTexture.width/camera.targetTexture.height;
            var emission=Node("BurstSource",card);Place(emission,220,16,0,180);hud.burstSource=emission;
            hud.revealParticles=Particles(card,"RevealParticles",8,.43f,80,65,4);
            hud.rareParticles=Particles(card,"RareParticles",18,.52f,105,90,4.5f);
            hud.epicParticles=Particles(card,"EpicParticles",28,.68f,120,110,5);
            hud.legendaryParticles=Particles(card,"LegendaryParticles",52,.9f,170,150,6.5f);
            hud.confirmParticles=Particles(card,"CollectParticles",36,.60f,150,130,6);
            hud.banishParticles=Particles(card,"BanishParticles",18,.42f,80,110,4.5f);
            hud.heading=Label(card,"Heading","ITEM DISCOVERED",580,38,0,366,24,new Color32(218,231,237,255),true);
            var rarity=Node("RarityBadge",card);Place(rarity,224,40,0,-13);var badge=Panel(rarity,new Color(.13f,.20f,.25f,.9f),new Color32(103,125,138,255),8);hud.rarityFrame=badge;badge.accents=HUDPanel.CornerAccents.None;badge.Refresh();
            hud.rarityLabel=Label(rarity,"Rarity","RARE",210,36,0,0,24,Color.white,true);
            var name=Label(card,"ItemName","Gas Can",590,64,0,-67,44,Color.white,true); name.enableAutoSizing=true;name.fontSizeMin=22;name.fontSizeMax=44;name.enableWordWrapping=true;
            hud.stackLabel=Label(card,"Stacks","NEW ITEM",580,34,0,-119,20,new Color32(117,239,190,255),true);
            Line(card,"DescriptionDivider",574,-146);
            // Scrollable text: short and long authored descriptions are preserved verbatim.
            var scroll=Node("DescriptionScroll",card);Place(scroll,570,126,0,-216);hud.descriptionScroll=Undo.AddComponent<ScrollRect>(scroll.gameObject);hud.descriptionScroll.horizontal=false;hud.descriptionScroll.vertical=true;hud.descriptionScroll.movementType=ScrollRect.MovementType.Clamped;hud.descriptionScroll.scrollSensitivity=30;
            var viewport=Node("Viewport",scroll);Stretch(viewport);var hit=Graphic<Image>(viewport);hit.color=new Color(0,0,0,0);hit.raycastTarget=true;Undo.AddComponent<RectMask2D>(viewport.gameObject);
            var desc=Node("Description",viewport);desc.anchorMin=new Vector2(0,1);desc.anchorMax=new Vector2(1,1);desc.pivot=new Vector2(.5f,1);desc.sizeDelta=new Vector2(-12,140);desc.anchoredPosition=Vector2.zero;
            var descText=Graphic<TextMeshProUGUI>(desc);descText.font=font;descText.fontSize=26;descText.color=new Color32(220,232,239,255);descText.alignment=TextAlignmentOptions.Top;descText.enableWordWrapping=true;descText.richText=true;descText.text="Item description";
            var size=Undo.AddComponent<ContentSizeFitter>(desc.gameObject);size.verticalFit=ContentSizeFitter.FitMode.PreferredSize;hud.descriptionScroll.viewport=viewport;hud.descriptionScroll.content=desc;hud.description=descText;
            hud.scrollHint=Label(card,"ScrollHint","SCROLL TO READ MORE",570,18,0,-290,14,new Color32(166,179,187,255),false);hud.scrollHint.gameObject.SetActive(false);
            Line(card,"ActionDivider",574,-302);
            var list=new List<HUDItemAction>();float width=includeDiscard?184:272;float step=includeDiscard?198:290;
            var collect=ActionButton(card,hud,HUDItemDecision.Collect,"COLLECT",includeDiscard?-step:-step*.5f,width,new Color32(117,239,190,255));list.Add(collect);
            var banish=ActionButton(card,hud,HUDItemDecision.Banish,"BANISH",includeDiscard?0:step*.5f,width,new Color32(214,179,143,255));list.Add(banish);
            hud.banishCount=Label((RectTransform)banish.transform,"BanishCount","5",40,40,width*.5f-26,0,22,new Color32(214,179,143,255),true);
            banish.countLabel=hud.banishCount;
            HUDItemAction discard=null;if(includeDiscard){discard=ActionButton(card,hud,HUDItemDecision.Discard,"DISCARD",step,width,new Color32(166,179,187,255));list.Add(discard);}
            hud.actions=list.ToArray();hud.hint=Label(card,"Hint","Choose what to do with this item.",590,38,0,-379,17,new Color32(166,179,187,255),false);hud.hint.enableWordWrapping=true;
            // Replace only UI references. The existing camera, root, audio and all inventory data are untouched.
            Undo.RecordObject(inventory,"Connect item discovery UI");so.Update();
            Set(so,"choicePanel",root.gameObject);Set(so,"discoveryHUD",hud);Set(so,"itemNameText",name);Set(so,"rarityText",hud.rarityLabel);Set(so,"descriptionText",descText);Set(so,"banishCountText",hud.banishCount);Set(so,"collectButton",collect.button);Set(so,"banishButton",banish.button);Set(so,"discardButton",discard!=null?discard.button:null);Set(so,"previewImage",hud.previewImage);
            so.ApplyModifiedProperties();PrefabUtility.RecordPrefabInstancePropertyModifications(inventory);
            if(old!=null){Undo.RecordObject(old,"Deactivate original item menu");old.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(old);}
            root.gameObject.SetActive(false);EditorUtility.SetDirty(hud);EditorSceneManager.MarkSceneDirty(inventory.gameObject.scene);Undo.CollapseUndoOperations(group);Selection.activeGameObject=root.gameObject;
            EditorUtility.DisplayDialog("Item Discovery HUD","Built and connected. Save the scene and open a crate in Play Mode. The old panel is retained inactive. The existing preview rig and item data are unchanged.","OK");
        }
        catch(Exception e){Undo.RevertAllDownToGroup(group);Debug.LogException(e);EditorUtility.DisplayDialog("Item Discovery HUD","Installation rolled back: "+e.Message,"OK");}
    }
    private static void Set(SerializedObject so,string field,UnityEngine.Object value){var p=so.FindProperty(field);Assert(p!=null,"Missing serialized field: "+field);p.objectReferenceValue=value;}
    private static void SetObject(UnityEngine.Object target,string field,UnityEngine.Object value){var so=new SerializedObject(target);Set(so,field,value);so.ApplyModifiedPropertiesWithoutUndo();}
    private static RectTransform Node(string name,Transform parent)
    {
        var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Create "+name);Undo.SetTransformParent(go.transform,parent,"Parent "+name);go.layer=parent.gameObject.layer;
        var rt=(RectTransform)go.transform;rt.localRotation=Quaternion.identity;rt.localScale=Vector3.one;rt.localPosition=Vector3.zero;return rt;
    }
    private static void Place(RectTransform r,float w,float h,float x,float y){r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=new Vector2(w,h);r.anchoredPosition=new Vector2(x,y);}
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;}
    private static T Graphic<T>(RectTransform r) where T:UnityEngine.UI.Graphic
    {if(r.GetComponent<CanvasRenderer>()==null)Undo.AddComponent<CanvasRenderer>(r.gameObject);var g=Undo.AddComponent<T>(r.gameObject);g.color=Color.white;g.raycastTarget=false;return g;}
    private static HUDPanel Panel(RectTransform r,Color fill,Color accent,float chamfer)
    {var p=Graphic<HUDPanel>(r);p.fillColor=fill;p.borderColor=accent;p.accentColor=accent;p.topLeft=p.topRight=p.bottomLeft=p.bottomRight=chamfer;p.borderThickness=1.5f;p.accents=HUDPanel.CornerAccents.TopLeft|HUDPanel.CornerAccents.BottomRight;p.accentLength=36;p.Refresh();return p;}
    private TextMeshProUGUI Label(RectTransform parent,string name,string value,float w,float h,float x,float y,float fs,Color color,bool bold)
    {var r=Node(name,parent);Place(r,w,h,x,y);var t=Graphic<TextMeshProUGUI>(r);t.font=font;t.fontSize=fs;t.color=color;t.fontStyle=bold?FontStyles.Bold:FontStyles.Normal;t.alignment=TextAlignmentOptions.Center;t.enableWordWrapping=false;t.richText=true;t.text=value;return t;}
    private static void Line(RectTransform parent,string name,float w,float y){var r=Node(name,parent);Place(r,w,1.5f,0,y);Graphic<Image>(r).color=new Color32(83,106,119,255);}
    private HUDItemAction ActionButton(RectTransform parent,HUDItemDiscovery hud,HUDItemDecision decision,string title,float x,float w,Color tint)
    {
        var r=Node(title,parent);Place(r,w,52,x,-340);var p=Panel(r,new Color(.12f,.18f,.22f,1),tint,10);p.raycastTarget=true;
        var b=Undo.AddComponent<Button>(r.gameObject);b.targetGraphic=p;b.transition=Selectable.Transition.None;
        var a=Undo.AddComponent<HUDItemAction>(r.gameObject);a.owner=hud;a.button=b;a.decision=decision;a.frame=p;a.accent=tint;
        a.label=Label(r,"Label",title,w-(decision==HUDItemDecision.Banish?54:10),46,decision==HUDItemDecision.Banish?-15:0,0,23,tint,true);return a;
    }
    private static HUDParticleBurst Particles(RectTransform parent,string name,int count,float life,float rise,float spread,float size)
    {
        var r=Node(name,parent);Stretch(r);var p=Graphic<HUDParticleBurst>(r);var so=new SerializedObject(p);
        so.FindProperty("particlesPerBurst").intValue=count;so.FindProperty("lifetime").floatValue=life;so.FindProperty("riseSpeed").floatValue=rise;so.FindProperty("horizontalSpeed").floatValue=spread;so.FindProperty("particleSize").floatValue=size;
        so.FindProperty("primaryColor").colorValue=Color.white;so.FindProperty("secondaryColor").colorValue=Color.white;so.FindProperty("highlightColor").colorValue=Color.white;so.FindProperty("fadeStart").floatValue=.28f;so.ApplyModifiedPropertiesWithoutUndo();return p;
    }
}
