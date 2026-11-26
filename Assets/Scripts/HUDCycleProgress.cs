using UnityEngine;

/// <summary>
/// Animates a normalized progress bar through explicit completed cycles.
/// The data source, not a drop in fraction, determines when a cycle completes.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("UI/HUD Cycle Progress")]
public sealed class HUDCycleProgress : MonoBehaviour
{
    [Header("Reference")]
    [SerializeField] private HUDProgressBar bar;

    [Header("Animation")]
    [Tooltip("Normalized units per second for ordinary progress. Zero snaps.")]
    [SerializeField, Min(0f)] private float fillSpeed = 4f;
    [Tooltip("Normalized units per second when completing a cycle. Zero snaps.")]
    [SerializeField, Min(0f)] private float completionSpeed = 8f;
    [SerializeField, Min(0f)] private float fullHoldSeconds = 0.08f;
    [Tooltip("Bound the visual backlog when a large award completes many cycles. All real progress is retained by the data source.")]
    [SerializeField, Range(1, 8)] private int maxPendingCycles = 3;
    [SerializeField] private bool useUnscaledTime;

    [Header("Completion Flash")]
    [SerializeField] private bool flashOnCompletion = true;
    [SerializeField] private Color completionColor = new Color32(215, 249, 255, 255);

    private bool initialized;
    private float displayed;
    private float target;
    private int pendingCycles;
    private bool holdingFull;
    private float holdRemaining;
    private bool flashing;
    private Color savedFillColor;
    private HUDProgressBar flashBar;
    private HUDProgressBar writtenBar;
    private float lastWritten = -1f;

    public float DisplayedValue => displayed;
    public float TargetValue => target;
    public int PendingCycles => pendingCycles;
    public bool IsAnimating => pendingCycles > 0 || holdingFull || displayed != target;

    private void Awake()
    {
        if (bar == null)
            bar = GetComponent<HUDProgressBar>();
        if (bar == null)
            Debug.LogError("HUDCycleProgress: Assign a HUDProgressBar.", this);
        else if (initialized)
            WriteBar(true);
    }

    /// <summary>Synchronize without replaying old completions (initialization/reset).</summary>
    public void SnapToValue(float normalizedValue)
    {
        initialized = true;
        target = displayed = Normalize(normalizedValue);
        pendingCycles = 0;
        holdingFull = false;
        holdRemaining = 0f;
        RestoreFillColor();
        WriteBar(true);
    }

    /// <summary>
    /// Queue completed cycles explicitly, then settle at the latest authoritative fraction.
    /// A large backlog is visually compressed, never applied to gameplay.
    /// </summary>
    public void SetProgress(float normalizedValue, int completedCycles = 0)
    {
        float next = Normalize(normalizedValue);
        if (!initialized || !isActiveAndEnabled)
        {
            SnapToValue(next);
            return;
        }

        int cap = Mathf.Clamp(maxPendingCycles, 1, 8);
        int incoming = Mathf.Clamp(completedCycles, 0, cap);
        pendingCycles = Mathf.Min(cap, pendingCycles + incoming);
        // A lower fraction without a completed cycle is a correction/reset,
        // not a level-up. Do not invent a wrap or animate a false XP loss.
        if (pendingCycles == 0 && next < target)
        {
            SnapToValue(next);
            return;
        }
        target = next;
    }

    private void Update()
    {
        if (!initialized || !IsAnimating) return;
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f) return;

        if (holdingFull)
        {
            holdRemaining -= dt;
            if (holdRemaining > 0f) return;

            RestoreFillColor();
            holdingFull = false;
            pendingCycles = Mathf.Max(0, pendingCycles - 1);
            displayed = 0f;
            WriteBar();
            // Keep the reset visible for one frame, even at low frame rates.
            return;
        }

        if (pendingCycles > 0)
        {
            displayed = Advance(displayed, 1f, completionSpeed, dt);
            WriteBar();
            if (displayed >= 1f)
            {
                holdingFull = true;
                holdRemaining = Mathf.Max(0f, fullHoldSeconds);
                StartFlash();
                // Never consume the full hold in this frame: show completion.
            }
            return;
        }

        displayed = Advance(displayed, target, fillSpeed, dt);
        WriteBar();
    }

    private void OnDisable()
    {
        RestoreFillColor();
        if (initialized) SnapToValue(target);
    }

    private void WriteBar(bool force = false)
    {
        // A parent meter can synchronize before this child receives Awake.
        if (bar == null) bar = GetComponent<HUDProgressBar>();
        if (bar == null) return;
        if (!force && writtenBar == bar && displayed == lastWritten) return;
        // This component owns the animation; avoid a second smoothing pass.
        bar.SnapToValue(displayed);
        writtenBar = bar;
        lastWritten = displayed;
    }

    private void StartFlash()
    {
        if (!flashOnCompletion || bar == null) return;
        RestoreFillColor();
        flashBar = bar;
        savedFillColor = bar.fillColor;
        flashing = true;
        bar.fillColor = completionColor;
        bar.Refresh();
    }

    private void RestoreFillColor()
    {
        if (!flashing) return;
        if (flashBar != null)
        {
            flashBar.fillColor = savedFillColor;
            flashBar.Refresh();
        }
        flashBar = null;
        flashing = false;
    }

    private static float Normalize(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    }

    private static float Advance(float value, float destination, float speed, float dt)
    {
        return speed <= 0f ? destination : Mathf.MoveTowards(value, destination, speed * dt);
    }
}
