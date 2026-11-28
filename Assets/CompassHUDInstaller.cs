#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class CompassHUDInstaller : EditorWindow
{
    private Canvas canvas;
    private PlayerVehicleInteractor interactor;
    private Camera fallback;
    private TMP_FontAsset font;
    private bool distanceLabels;
    private bool isolateCanvas=true;
    [MenuItem("Tools/HUD Library/Build Compass HUD")]
    private static void Open() => GetWindow<CompassHUDInstaller>("Compass HUD");
    private void OnGUI()
    {
        EditorGUILayout.LabelField("Minimal compass tape",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Creates the top-center compass. Add HUDCompassMarker to any world object or prefab to display it.",MessageType.Info);
        canvas=(Canvas)EditorGUILayout.ObjectField("Gameplay Canvas",canvas,typeof(Canvas),true);
        interactor=(PlayerVehicleInteractor)EditorGUILayout.ObjectField("Vehicle Interactor",interactor,typeof(PlayerVehicleInteractor),true);
        fallback=(Camera)EditorGUILayout.ObjectField("Fallback Camera",fallback,typeof(Camera),true);
        font=(TMP_FontAsset)EditorGUILayout.ObjectField("HUD Font",font,typeof(TMP_FontAsset),false);
        distanceLabels=EditorGUILayout.Toggle("Show Distances",distanceLabels);
        isolateCanvas=EditorGUILayout.Toggle("Isolate Moving UI",isolateCanvas);
        if(interactor==null && fallback==null) EditorGUILayout.HelpBox("Assign the Vehicle Interactor to follow the player and car cameras. Without it, an assigned or MainCamera-tagged camera is used.",MessageType.Warning);
        using(new EditorGUI.DisabledScope(canvas==null||EditorApplication.isPlayingOrWillChangePlaymode))
            if(GUILayout.Button("Build Compass HUD",GUILayout.Height(32))) Build();
    }
    private void Build()
    {
        if(canvas.GetComponentInChildren<CompassMeter>(true)!=null)
        { EditorUtility.DisplayDialog("Compass HUD","A CompassMeter already exists under this Canvas. Remove it before rebuilding.","OK"); return; }
        Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Build Compass HUD");
        RectTransform root=Create(canvas.transform,"CompassHUD");
        root.anchorMin=root.anchorMax=new Vector2(.5f,1f); root.pivot=new Vector2(.5f,1f);
        root.sizeDelta=new Vector2(660f,94f); root.anchoredPosition=new Vector2(0,-12f);
        if(isolateCanvas) Undo.AddComponent<Canvas>(root.gameObject);
        RectTransform tapeRect=Create(root,"Tape"); StretchWidth(tapeRect,54f,15f);
        Undo.AddComponent<CanvasRenderer>(tapeRect.gameObject);
        var tape=Undo.AddComponent<HUDCompassTape>(tapeRect.gameObject);
        tape.raycastTarget=false; tape.color=Color.white; tape.maskable=false;
        RectTransform labels=Create(root,"Directions"); StretchWidth(labels,30f,-1f);
        RectTransform markers=Create(root,"Markers"); StretchWidth(markers,48f,-27f);
        var meter=Undo.AddComponent<CompassMeter>(root.gameObject);
        meter.vehicleInteractor=interactor; meter.fallbackCamera=fallback; meter.font=font;
        meter.tape=tape; meter.labelsRoot=labels; meter.markersRoot=markers; meter.showDistance=distanceLabels;
        EditorUtility.SetDirty(meter); EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Undo.CollapseUndoOperations(undo); Selection.activeGameObject=root.gameObject;
    }
    private static RectTransform Create(Transform parent,string name)
    {
        var go=new GameObject(name,typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go,"Create "+name);
        Undo.SetTransformParent(go.transform,parent,"Parent "+name); go.layer=parent.gameObject.layer;
        var r=(RectTransform)go.transform; r.localScale=Vector3.one; r.localRotation=Quaternion.identity;
        r.anchoredPosition3D=Vector3.zero; return r;
    }
    private static void StretchWidth(RectTransform r,float height,float y)
    {
        r.anchorMin=new Vector2(0f,.5f); r.anchorMax=new Vector2(1f,.5f); r.pivot=new Vector2(.5f,.5f);
        r.sizeDelta=new Vector2(0f,height); r.anchoredPosition=new Vector2(0f,y);
    }
}
#endif
