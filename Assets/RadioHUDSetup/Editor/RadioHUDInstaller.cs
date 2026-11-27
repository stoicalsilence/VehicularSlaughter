#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Editor-only setup for the RadioMeter v1.0 package. No runtime code is replaced.
public sealed class RadioHUDInstaller : EditorWindow
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private PlayerRadio radio;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Sprite handset, repositionIcon, respawnIcon, airstrikeIcon;
    private Vector2 scroll;
    private static readonly Color Ready = new Color32(112,231,237,255);
    private static readonly Color White = new Color32(244,246,247,255);
    private static readonly Color Muted = new Color32(132,148,160,255);
    private static readonly Color Border = new Color32(103,120,132,255);
    private static readonly Color PanelFill = new Color32(25,38,45,220);

    [MenuItem("Tools/HUD Library/Build Radio HUD")]
    private static void Open()
    {
        var w = GetWindow<RadioHUDInstaller>("Radio HUD Setup");
        w.minSize = new Vector2(380, 450);
        w.FindDefaults();
        w.Show();
    }

    private void FindDefaults()
    {
        if (canvas == null && Selection.activeGameObject != null)
            canvas = Selection.activeGameObject.GetComponentInParent<Canvas>();
        if (canvas == null) canvas = UniqueSceneObject<Canvas>();
        if (radio == null) radio = UniqueSceneObject<PlayerRadio>();
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (handset == null) handset = LoadIcon("Radio_Handset");
        if (repositionIcon == null) repositionIcon = LoadIcon("Radio_Reposition");
        if (respawnIcon == null) respawnIcon = LoadIcon("Radio_Respawn");
        if (airstrikeIcon == null) airstrikeIcon = LoadIcon("Radio_Airstrike");
    }

    private static T UniqueSceneObject<T>() where T : Component
    {
        var found = new List<T>();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found.Count == 1 ? found[0] : null;
    }

    private Sprite LoadIcon(string name)
    {
        string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
        if (string.IsNullOrEmpty(scriptPath)) return null;
        string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(scriptPath), "../Icons/" + name + ".png"));
        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!path.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        path = path.Substring(project.Length + 1).Replace('\\', '/');
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
            !importer.alphaIsTransparency || importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed ||
            settings.spriteMeshType != SpriteMeshType.FullRect || importer.filterMode != FilterMode.Bilinear)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Build the complete RadioHUD", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Uses existing RadioMeter v1.0, patched PlayerRadio, HUDPanel, HUDArc and HUDProgressBar. Select your gameplay Canvas and player radio. No gameplay or legacy HUD objects are modified.", MessageType.Info);
        canvas = (Canvas)EditorGUILayout.ObjectField("Gameplay Canvas", canvas, typeof(Canvas), true);
        radio = (PlayerRadio)EditorGUILayout.ObjectField("Player Radio", radio, typeof(PlayerRadio), true);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font", font, typeof(TMP_FontAsset), false);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sprites (white artwork)", EditorStyles.boldLabel);
        handset = (Sprite)EditorGUILayout.ObjectField("Radio", handset, typeof(Sprite), false);
        repositionIcon = (Sprite)EditorGUILayout.ObjectField("Reposition", repositionIcon, typeof(Sprite), false);
        respawnIcon = (Sprite)EditorGUILayout.ObjectField("Respawn", respawnIcon, typeof(Sprite), false);
        airstrikeIcon = (Sprite)EditorGUILayout.ObjectField("Airstrike", airstrikeIcon, typeof(Sprite), false);
        if (GUILayout.Button("Find missing references / bundled icons")) FindDefaults();
        EditorGUILayout.Space();
        bool valid = canvas != null && radio != null && font != null && handset != null && repositionIcon != null && respawnIcon != null && airstrikeIcon != null &&
            canvas.gameObject.scene.IsValid() && radio.gameObject.scene.IsValid() && canvas.gameObject.scene.isLoaded && radio.gameObject.scene.isLoaded &&
            canvas.gameObject.scene == radio.gameObject.scene && !EditorUtility.IsPersistent(canvas) && !EditorUtility.IsPersistent(radio) && !EditorApplication.isPlayingOrWillChangePlaymode;
        if (!valid) EditorGUILayout.HelpBox("Assign all fields using objects from the same scene and imported sprites. Import TMP Essential Resources if no font is available. Build in Edit Mode.", MessageType.Warning);
        if (canvas != null && canvas.transform.Find("RadioHUD") != null)
        {
            valid = false;
            EditorGUILayout.HelpBox("This Canvas already has a RadioHUD child. Rename your old object before building if you want to keep a backup. Nothing will be overwritten.", MessageType.Warning);
        }
        using (new EditorGUI.DisabledScope(!valid))
            if (GUILayout.Button("Create RadioHUD hierarchy", GUILayout.Height(38))) Build();
        EditorGUILayout.HelpBox("After building: disable the old radio-only UI objects and clear the five legacy UI references on PlayerRadio. Select the generated root to inspect all assignments. Ctrl+Z undoes creation.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void Build()
    {
        // Preflight serialized field names on the actual newly created component;
        // failures roll back the entire operation rather than leaving half a HUD.
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Radio HUD");
        try
        {
            var root = Node("RadioHUD", canvas.transform, Vector2.zero, Vector2.zero);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var meter = Add<RadioMeter>(root.gameObject);
            var so = new SerializedObject(meter);
            so.Update();
            Ref(so, "radio", radio);

            var status = Node("RadioStatus", root, new Vector2(264,68), new Vector2(16,164), Vector2.zero, Vector2.zero);
            var statusPanel = Panel(status, Ready, PanelFill, new Color32(137,153,159,255));
            var ring = Add<HUDArc>(Node("CooldownRing", status, new Vector2(50,50), new Vector2(-94,0)).gameObject);
            ring.startAngle = 90; ring.sweepAngle = 360; ring.clockwise = true;
            ring.resolution = 48; ring.padding = 1; ring.arcThickness = 3;
            ring.showTicks = false; ring.showNeedle = false; ring.trackColor = new Color32(83,97,108,255);
            ring.fillColor = Ready; ring.raycastTarget = false; ring.color = Color.white;
            SetFloat(ring, "value", 1f); ring.Refresh();
            var radioImage = Picture("RadioIcon", status, new Vector2(29,29), new Vector2(-94,0), handset, Ready);
            Text("Title", status, 104,24,-9,12,"RADIO",19,White,TextAlignmentOptions.Left);
            var statusText = Text("Status", status,125,18,1,-13,"READY",12,Ready,TextAlignmentOptions.Left);
            var key = Text("KeyOrTime",status,65,32,90,0,"TAB",22,Ready,TextAlignmentOptions.Right);
            Ref(so,"statusPanel",statusPanel); Ref(so,"cooldownRing",ring); Ref(so,"radioIcon",radioImage);
            Ref(so,"statusText",statusText); Ref(so,"keyOrTimeText",key); Ref(so,"readyPulseTarget",radioImage.rectTransform);

            var selector = Node("ChargeSelector",root,new Vector2(660,166),new Vector2(0,310),new Vector2(.5f,0),new Vector2(.5f,0));
            Text("Header",selector,660,20,0,73,"TACTICAL SUPPORT",16,White);
            Card(so,selector,"reposition","Reposition","REPOSITION","1/3 CHARGE",-224,repositionIcon);
            Card(so,selector,"respawn","Respawn","RESPAWN","2/3 CHARGE",0,respawnIcon);
            Card(so,selector,"airstrike","Airstrike","AIRSTRIKE","100%",224,airstrikeIcon);
            var bar = Add<HUDProgressBar>(Node("ChargeBar",selector,new Vector2(600,10),new Vector2(-30,-57)).gameObject);
            bar.leftSlant = bar.rightSlant = 5; bar.borderThickness = 0; bar.showDamageTrail = false;
            bar.trackColor = new Color32(67,82,92,255); bar.fillColor = Ready;
            bar.color = Color.white; bar.raycastTarget = false;
            SetFloat(bar,"previewValue",0); SetFloat(bar,"previewTrail",0); bar.Refresh();
            Marker(selector,"Threshold1",-129.17f);
            Marker(selector,"Threshold2",69.17f);
            var pct = Text("Percent",selector,55,20,302,-57,"0%",15,White,TextAlignmentOptions.Right);
            var hint = Text("Hint",selector,660,20,0,-75,"HOLD TAB TO CHARGE  ·  RELEASE TO CANCEL",14,White);
            Ref(so,"selectorRoot",selector.gameObject); Ref(so,"chargeBar",bar); Ref(so,"chargePercentText",pct); Ref(so,"selectorHint",hint);

            var placement = Node("PlacementPrompt",root,new Vector2(540,80),new Vector2(0,310),new Vector2(.5f,0),new Vector2(.5f,0));
            var placementPanel = Panel(placement,Ready,PanelFill,new Color32(137,153,159,255));
            var title = Text("Title",placement,500,25,0,22,"REPOSITION CAR",22,White);
            var target = Text("Status",placement,500,20,0,0,"TARGET VALID",14,Ready);
            var controls = Text("Controls",placement,510,20,0,-23,"LMB CONFIRM  ·  RMB / C CANCEL",13,White);
            Ref(so,"placementRoot",placement.gameObject); Ref(so,"placementPanel",placementPanel);
            Ref(so,"placementTitle",title); Ref(so,"placementStatus",target); Ref(so,"placementControls",controls);
            so.ApplyModifiedProperties();
            Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { selector.gameObject, placement.gameObject }, "Hide transient radio UI");
            selector.gameObject.SetActive(false); placement.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;
            EditorGUIUtility.PingObject(root.gameObject);
            Debug.Log("RadioHUD created. All meter references are wired. Review placement and disable the old radio-only UI.", root);
        }
        catch (Exception ex)
        {
            Undo.RevertAllDownToGroup(group);
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Radio HUD setup failed", "No partial HUD was retained. Check Console and confirm the original RadioMeter/library versions are installed.\n\n" + ex.Message, "OK");
        }
    }

    private static RectTransform Node(string name, Transform parent, Vector2 size, Vector2 pos, Vector2? anchor = null, Vector2? pivot = null)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)obj.transform;
        Undo.RegisterCreatedObjectUndo(obj, "Create " + name);
        Undo.SetTransformParent(rt,parent,"Parent " + name);
        Undo.RegisterCompleteObjectUndo(rt,"Configure " + name);
        rt.localRotation = Quaternion.identity;
        rt.localPosition = Vector3.zero;
        rt.anchorMin = rt.anchorMax = anchor ?? new Vector2(.5f,.5f);
        rt.pivot = pivot ?? new Vector2(.5f,.5f);
        rt.sizeDelta = size; rt.anchoredPosition = pos; rt.localScale = Vector3.one;
        return rt;
    }
    private static T Add<T>(GameObject obj) where T : Component
    {
        var c = Undo.AddComponent<T>(obj);
        Undo.RegisterCompleteObjectUndo(c, "Configure " + typeof(T).Name);
        return c;
    }
    private static HUDPanel Panel(RectTransform rt, Color accent, Color fill, Color border)
    {
        var p = Add<HUDPanel>(rt.gameObject);
        p.topLeft = 12; p.topRight = 10; p.bottomRight = 12; p.bottomLeft = 10;
        p.borderThickness = 1.5f; p.fillColor = fill; p.borderColor = border; p.accentColor = accent;
        p.accents = HUDPanel.CornerAccents.TopLeft | HUDPanel.CornerAccents.BottomLeft;
        p.accentLength = 24; p.color = Color.white; p.raycastTarget = false; p.Refresh();
        return p;
    }
    private static void Marker(Transform parent,string name,float x)
    {
        var rt = Picture(name,parent,new Vector2(1.5f,16),new Vector2(x,-57),null,White).rectTransform;
        Undo.RegisterCompleteObjectUndo(rt,"Angle threshold marker");
        rt.localRotation = Quaternion.Euler(0,0,-15);
    }
    private static Image Picture(string name, Transform parent, Vector2 size, Vector2 pos, Sprite sprite, Color color)
    {
        var img = Add<Image>(Node(name,parent,size,pos).gameObject);
        img.sprite = sprite; img.color = color; img.preserveAspect = sprite != null; img.raycastTarget = false;
        return img;
    }
    private TextMeshProUGUI Text(string name, Transform parent,float w,float h,float x,float y,string value,float size,Color color,TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var t = Add<TextMeshProUGUI>(Node(name,parent,new Vector2(w,h),new Vector2(x,y)).gameObject);
        t.font = font; t.text = value; t.fontSize = size; t.fontStyle = FontStyles.Bold;
        t.color = color; t.alignment = align; t.enableWordWrapping = false; t.raycastTarget = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
    private void Card(SerializedObject so, Transform parent,string field,string name,string label,string threshold,float x,Sprite sprite)
    {
        var rt = Node(name,parent,new Vector2(212,104),new Vector2(x,7));
        var p = Panel(rt,Border,new Color32(30,42,51,225),Border);
        var icon = Picture("Icon",rt,new Vector2(34,34),new Vector2(0,20),sprite,Muted);
        var title = Text("Title",rt,190,20,0,-13,label,17,Muted);
        var state = Text("State",rt,190,18,0,-35,threshold,11,Muted);
        Ref(so,field+".panel",p); Ref(so,field+".icon",icon); Ref(so,field+".title",title);
        Ref(so,field+".stateText",state); Ref(so,field+".pulseTarget",icon.rectTransform);
    }
    private static void Ref(SerializedObject so,string field,UnityEngine.Object value)
    {
        var p = so.FindProperty(field);
        if (p == null || p.propertyType != SerializedPropertyType.ObjectReference)
            throw new InvalidOperationException("Missing serialized reference: " + field);
        p.objectReferenceValue = value;
    }
    private static void SetFloat(UnityEngine.Object target,string field,float value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null || p.propertyType != SerializedPropertyType.Float)
            throw new InvalidOperationException("Missing serialized value: " + field);
        p.floatValue = value; so.ApplyModifiedProperties();
    }
}
#endif
