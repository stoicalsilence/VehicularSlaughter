using UnityEngine;

/// <summary>
/// Simple frame animation for a horde enemy.
/// Each animation frame is a child Renderer. All child GameObjects remain
/// active; only Renderer.enabled is changed when the frame advances.
/// </summary>
public sealed class HordeBakedAnimator : MonoBehaviour
{
    public enum AnimationState
    {
        Idle,
        Move
    }

    [Header("Frame Renderers")]
    [Tooltip("Renderer for each idle frame, in playback order. A single renderer gives a static idle pose.")]
    [SerializeField] private Renderer[] idleFrames;

    [Tooltip("Renderer for each moving frame, in playback order.")]
    [SerializeField] private Renderer[] moveFrames;

    [Header("Playback")]
    [SerializeField, Min(0f)] private float idleFramesPerSecond = 5f;
    [SerializeField, Min(0f)] private float moveFramesPerSecond = 12f;

    private Renderer[] currentFrames;
    private AnimationState currentState;
    private int currentFrameIndex = -1;
    private float frameTimer;
    private bool hasState;

    private void Awake()
    {
        DisableAllFrames();
    }

    private void OnEnable()
    {
        // Pooled enemies must never become visible on a stale animation frame.
        DisableAllFrames();
        ClearPlaybackState();
    }

    private void OnDisable()
    {
        DisableAllFrames();
        ClearPlaybackState();
    }

    /// <summary>
    /// Selects an animation. Calling Play repeatedly with the same state
    /// does not restart the animation.
    /// </summary>
    public void Play(AnimationState state)
    {
        if (hasState && currentState == state)
            return;

        DisableCurrentFrame();

        currentState = state;
        currentFrames = state == AnimationState.Move ? moveFrames : idleFrames;
        currentFrameIndex = -1;
        frameTimer = 0f;
        hasState = true;

        if (currentFrames != null && currentFrames.Length > 0)
            SetFrame(0);
    }

    /// <summary>
    /// Advances the selected animation using elapsed simulation time.
    /// </summary>
    public void Tick(float elapsed)
    {
        if (!hasState || currentFrames == null || currentFrames.Length <= 1)
            return;

        float framesPerSecond = currentState == AnimationState.Move
            ? moveFramesPerSecond
            : idleFramesPerSecond;

        if (framesPerSecond <= 0f || elapsed <= 0f)
            return;

        float frameDuration = 1f / framesPerSecond;
        frameTimer += elapsed;

        int framesToAdvance = Mathf.FloorToInt(frameTimer / frameDuration);
        if (framesToAdvance <= 0)
            return;

        frameTimer -= framesToAdvance * frameDuration;

        int nextFrame = (currentFrameIndex + framesToAdvance) % currentFrames.Length;
        SetFrame(nextFrame);
    }

    /// <summary>
    /// Hides all animation frames and resets playback for object pooling.
    /// </summary>
    public void ResetAnimation()
    {
        DisableAllFrames();
        ClearPlaybackState();
    }

    private void SetFrame(int index)
    {
        if (currentFrames == null || currentFrames.Length == 0)
            return;

        if (currentFrameIndex >= 0 && currentFrameIndex < currentFrames.Length)
        {
            Renderer previous = currentFrames[currentFrameIndex];
            if (previous != null)
                previous.enabled = false;
        }

        currentFrameIndex = index;

        Renderer next = currentFrames[currentFrameIndex];
        if (next != null)
            next.enabled = true;
    }

    private void DisableCurrentFrame()
    {
        if (currentFrames == null || currentFrameIndex < 0 || currentFrameIndex >= currentFrames.Length)
            return;

        Renderer renderer = currentFrames[currentFrameIndex];
        if (renderer != null)
            renderer.enabled = false;
    }

    private void DisableAllFrames()
    {
        SetFramesEnabled(idleFrames, false);
        SetFramesEnabled(moveFrames, false);
    }

    private static void SetFramesEnabled(Renderer[] frames, bool enabled)
    {
        if (frames == null)
            return;

        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] != null)
                frames[i].enabled = enabled;
        }
    }

    private void ClearPlaybackState()
    {
        currentFrames = null;
        currentFrameIndex = -1;
        frameTimer = 0f;
        hasState = false;
    }
}
