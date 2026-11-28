using System.Collections.Generic;
using UnityEngine;

/// <summary>Register any active world object with all compass displays. No Update or physics queries.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("HUD/Compass Marker")]
public sealed class HUDCompassMarker : MonoBehaviour
{
    public Sprite icon;
    public Color tint = Color.white;
    [Tooltip("Optional point to track instead of this transform.")]
    public Transform trackingPoint;
    [Tooltip("Higher values win when icons overlap or the visible marker budget is full.")]
    public int priority;
    [Tooltip("Horizontal world distance. Zero means unlimited.")]
    [Min(0f)] public float maximumDistance;
    [Min(0f)] public float minimumDistance = 0.5f;
    [Tooltip("Optional: useful on the player's own car in a single-vehicle game.")]
    public bool hideWhileDriving;
    [Tooltip("Can be changed by gameplay without disabling the world object.")]
    public bool visibleOnCompass = true;

    internal static readonly HashSet<HUDCompassMarker> Active = new HashSet<HUDCompassMarker>();
    public Vector3 Position => trackingPoint != null ? trackingPoint.position : transform.position;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Active.Clear();
    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);
    public void SetVisible(bool value) => visibleOnCompass = value;
}
