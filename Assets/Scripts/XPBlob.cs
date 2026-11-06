
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class XPBlob : MonoBehaviour
{
    public static readonly List<XPBlob> ActiveBlobs =
        new List<XPBlob>();

    public bool IsBeingCollected { get; private set; }
    public bool IsInPool { get; private set; }

    public float XPValue { get; private set; }

    public XPBlobCollector Collector { get; private set; }

    private XPBlobPool ownerPool;
    private Tween moveTween;

    public void Initialize(XPBlobPool pool)
    {
        ownerPool = pool;
        IsInPool = false;
    }

    public void SetInPool(bool value)
    {
        IsInPool = value;
    }

    public void PrepareForSpawn(float xpValue)
    {
        XPValue = xpValue;
        IsBeingCollected = false;
        Collector = null;

        moveTween = null;
    }

    public bool TryClaim(XPBlobCollector collector)
    {
        if (IsBeingCollected)
            return false;

        IsBeingCollected = true;
        Collector = collector;

        return true;
    }

    public void SetMoveTween(Tween tween)
    {
        moveTween = tween;
    }

    public void ClearMoveTween()
    {
        moveTween = null;
    }

    public void CancelMoveTween()
    {
        if (moveTween != null && moveTween.IsActive())
            moveTween.Kill();

        moveTween = null;
    }

    public void ClearCollector(XPBlobCollector collector)
    {
        if (Collector != collector)
            return;

        Collector = null;
        IsBeingCollected = false;
    }

    public void ResetForPool()
    {
        CancelMoveTween();

        IsBeingCollected = false;
        Collector = null;
        XPValue = 0f;
    }

    public void ReturnToPool()
    {
        if (ownerPool != null)
            ownerPool.Return(this);
    }

    private void OnEnable()
    {
        if (!IsInPool && !ActiveBlobs.Contains(this))
            ActiveBlobs.Add(this);
    }

    private void OnDisable()
    {
        ActiveBlobs.Remove(this);

        if (Collector != null)
            Collector.NotifyBlobDisabled(this);

        Collector = null;
        IsBeingCollected = false;

        CancelMoveTween();
    }
}
