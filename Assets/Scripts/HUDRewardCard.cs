using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using DG.Tweening;

/// <summary>Presentation only; PlayerLevelRewardSystem remains the sole reward authority.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button), typeof(HUDPanel))]
public sealed class HUDRewardCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private HUDPanel panel;
    [SerializeField] private TMP_Text categoryText, rarityText, statText, amountText, comparisonText, actionText;
    [SerializeField] private RectTransform content;
    [SerializeField] private CanvasGroup revealGroup;
    [SerializeField] private HUDParticleBurst revealParticles, confirmParticles;
    [SerializeField, Range(0f, 1f)] private float focusTint = .14f;
    [SerializeField] private Color baseBackground = new Color32(24, 36, 47, 247);
    [Header("Rarity Shine (Optional)")]
    [SerializeField] private HUDRewardShine rarityShine;
    [SerializeField, Range(0f, 1f)] private float epicShineOpacity = .10f;
    [SerializeField, Range(0f, 1f)] private float legendaryShineOpacity = .28f;
    [SerializeField, Min(.01f)] private float shineDuration = .72f;
    private System.Action focusSound;
    public void SetFocusSound(System.Action callback) { focusSound = callback; }
    public void PlayRarityShine()
    {
        if (!bound || rarityShine == null) return;
        if (rarity == ItemRarity.Legendary) rarityShine.Play(accent, legendaryShineOpacity, shineDuration);
        else if (rarity == ItemRarity.Epic) rarityShine.Play(accent, epicShineOpacity, shineDuration * .8f);
    }
    public void ClearRarityShine() { if (rarityShine != null) rarityShine.Clear(); }
    private Button button;
    private Color accent = Color.white;
    private bool hovered, focused, confirmed, bound;
    private ItemRarity rarity;
    private Vector3 restScale = Vector3.one;
    private bool capturedScale;
    private Vector3 cardRestScale = Vector3.one;
    private bool capturedCardScale;
    private float confirmationFlash;

    public bool IsConfirmed => bound && confirmed;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (panel == null) panel = GetComponent<HUDPanel>();
        // Keep serialized references for existing scenes; no per-card CTA is needed.
        if (actionText != null) actionText.enabled = false;
        if (content != null)
        {
            Transform divider = content.Find("FooterDivider");
            if (divider != null) divider.gameObject.SetActive(false);
        }
        CaptureScale();
    }
    private void CaptureScale()
    {
        if (!capturedScale && content != null) { restScale = content.localScale; capturedScale = true; }
        if (!capturedCardScale) { cardRestScale = transform.localScale; capturedCardScale = true; }
    }
    private void ResetConfirmationVisual()
    {
        confirmationFlash = 0f;
        if (capturedCardScale) transform.localScale = cardRestScale;
    }
    public void Bind(string category, string stat, ItemRarity tier, string amount, string comparison,
        Color rarityColor, Color categoryColor, Color titleColor, Color improvementColor)
    {
        CaptureScale();
        ResetConfirmationVisual();
        ClearRarityShine();
        rarity = tier; accent = rarityColor; hovered = focused = confirmed = false; bound = true;
        if (revealParticles != null) revealParticles.Clear();
        if (confirmParticles != null) confirmParticles.Clear();
        Set(categoryText, category, categoryColor);
        Set(rarityText, tier.ToString().ToUpperInvariant(), accent);
        Set(statText, stat, titleColor);
        Set(amountText, amount, improvementColor);
        Set(comparisonText, comparison, titleColor);
        FinishReveal();
    }
    public void AddReveal(Sequence sequence, float offset, float duration)
    {
        if (!bound || duration <= 0f) return;
        if (content != null)
        {
            content.localScale = restScale * .91f;
            sequence.Insert(offset, content.DOScale(restScale, duration).SetEase(Ease.OutCubic));
        }
        if (revealGroup != null)
        {
            revealGroup.alpha = 0f;
            sequence.Insert(offset, revealGroup.DOFade(1f, duration).SetEase(Ease.OutQuad));
        }
    }
    public void AddExit(Sequence sequence, float offset, float duration)
    {
        if (!bound || duration <= 0f || revealGroup == null) return;
        sequence.Insert(offset, revealGroup.DOFade(0f, duration).SetEase(Ease.InQuad));
    }
    public void FinishReveal()
    {
        if (content != null) content.localScale = restScale;
        if (revealGroup != null) revealGroup.alpha = 1f;
        Draw();
    }
    public void PlayRareReveal()
    {
        if (bound && (rarity == ItemRarity.Epic || rarity == ItemRarity.Legendary)) Emit(revealParticles, accent);
    }
    public void Confirm()
    {
        if (!bound) return;
        confirmed = true;
        ClearRarityShine();
        Draw();
        Emit(confirmParticles, accent);
    }
    /// <summary>Append a frame punch and flash inside the owner's existing unscaled hold.
    /// No independent tween, delay, or layout size changes.</summary>
    public void AddConfirmation(Sequence sequence, float hold, float punchStrength, float flashStrength)
    {
        if (!IsConfirmed || sequence == null || hold <= 0f) return;
        CaptureScale();
        float duration = Mathf.Min(hold, 0.26f);
        float punch = Mathf.Clamp(punchStrength, 0f, 0.15f);
        if (punch > 0f)
        {
            // Animate the full frame, not the content alone. Preserve the authored scale.
            Vector3 compressed = Vector3.Scale(cardRestScale, new Vector3(1f + punch * .25f, 1f - punch, 1f));
            Vector3 expanded = Vector3.Scale(cardRestScale, new Vector3(1f + punch, 1f + punch, 1f));
            transform.localScale = cardRestScale;
            sequence.Insert(0f, transform.DOScale(compressed, duration * .18f).SetEase(Ease.OutQuad));
            sequence.Insert(duration * .18f, transform.DOScale(expanded, duration * .32f).SetEase(Ease.OutCubic));
            sequence.Insert(duration * .50f, transform.DOScale(cardRestScale, duration * .50f).SetEase(Ease.OutCubic));
        }
        confirmationFlash = Mathf.Clamp01(flashStrength);
        if (confirmationFlash > 0f)
        {
            Draw();
            sequence.Insert(0f, DOTween.To(() => confirmationFlash, value =>
            {
                confirmationFlash = value;
                Draw();
            }, 0f, duration).SetEase(Ease.OutQuad));
        }
    }

    public void AddUnchosenDim(Sequence sequence, float hold, float opacity)
    {
        if (!bound || confirmed || revealGroup == null || sequence == null || hold <= 0f) return;
        float target = Mathf.Clamp01(opacity);
        if (target >= 1f) return;
        sequence.Insert(0f, revealGroup.DOFade(target, Mathf.Min(hold, .12f)).SetEase(Ease.OutQuad));
    }

    private void Emit(HUDParticleBurst burst, Color tint)
    {
        if (burst == null || !burst.isActiveAndEnabled || burst.GetComponent<CanvasRenderer>() == null) return;
        burst.color = tint;
        burst.Emit((RectTransform)transform, true);
    }
    private void Draw()
    {
        if (!bound) return;
        bool focus = !confirmed && (hovered || focused) && button != null && button.IsInteractable();
        Color c = confirmed ? (Color) new Color32(117, 239, 190, 255) : accent;
        if (panel != null)
        {
            panel.fillColor = Color.Lerp(baseBackground, c, confirmed ? .24f : focus ? focusTint : .045f);
            panel.fillColor.a = baseBackground.a;
            float flash = confirmed ? confirmationFlash : 0f;
            if (flash > 0f)
            {
                panel.fillColor = Color.Lerp(panel.fillColor, Color.white, flash * .12f);
                panel.fillColor.a = baseBackground.a;
            }
            Color rim = Color.Lerp(c, Color.white, flash);
            panel.borderColor = focus || confirmed ? rim : Color.Lerp(new Color32(91, 112, 126, 255), c, .65f);
            panel.accentColor = rim;
            panel.borderThickness = focus || confirmed ? 2.5f + flash * 2f : 1.5f;
            panel.Refresh();
        }
        // Confirmation is communicated by the green frame and the particle burst.
    }
    private static void Set(TMP_Text t, string value, Color c) { if (t != null) { t.text = value; t.color = c; } }
    public void RefreshInteractionState() { Draw(); }

    public void SetPointerHover(bool value, bool playAudio = true)
    {
        if (!bound || confirmed) return;
        if (value && button != null && button.IsInteractable())
        {
            bool newlyHovered = !hovered;
            hovered = true;
            focused = false;
            ClearSiblingHighlights();
            EventSystem es = EventSystem.current;
            if (es != null && !es.alreadySelecting && es.currentSelectedGameObject != null)
            {
                HUDRewardCard selected = es.currentSelectedGameObject.GetComponentInParent<HUDRewardCard>();
                if (selected != null && selected.transform.parent == transform.parent)
                    es.SetSelectedGameObject(null);
            }
            if (newlyHovered && playAudio) focusSound?.Invoke();
        }
        else hovered = false;
        Draw();
    }

    private void ClearSiblingHighlights()
    {
        Transform parent = transform.parent;
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++)
        {
            HUDRewardCard other = parent.GetChild(i).GetComponent<HUDRewardCard>();
            if (other == null || other == this || (!other.hovered && !other.focused)) continue;
            other.hovered = other.focused = false;
            other.Draw();
        }
    }

    public void OnPointerEnter(PointerEventData e) { SetPointerHover(true); }
    public void OnPointerExit(PointerEventData e) { SetPointerHover(false); }
    public void OnSelect(BaseEventData e)
    {
        // Button pointer-down also invokes OnSelect. It is not keyboard focus.
        if (!bound || confirmed || e is PointerEventData || button == null || !button.IsInteractable()) return;
        bool newlyFocused = !focused && !hovered;
        hovered = false;
        focused = true;
        ClearSiblingHighlights();
        Draw();
        if (newlyFocused) focusSound?.Invoke();
    }
    public void OnDeselect(BaseEventData e) { focused = false; Draw(); }
    private void OnDisable()
    {
        hovered = focused = confirmed = bound = false;
        focusSound = null;
        ClearRarityShine();
        ResetConfirmationVisual();
        if (revealParticles != null) revealParticles.Clear();
        if (confirmParticles != null) confirmParticles.Clear();
    }
}
