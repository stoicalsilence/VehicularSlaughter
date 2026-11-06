
using System.Collections.Generic;
using UnityEngine;

public class XPBlobPool : MonoBehaviour
{
    [SerializeField] private XPBlob prefab;
    [SerializeField] private int prewarmCount = 64;
    [SerializeField] private int maxPoolSize = 512;

    private Stack<XPBlob> pool = new Stack<XPBlob>();

    private void Awake()
    {
        for (int i = 0; i < prewarmCount; i++)
        {
            XPBlob blob = CreateBlob();
            Return(blob);
        }
    }

    private XPBlob CreateBlob()
    {
        XPBlob blob = Instantiate(prefab, transform);
        blob.Initialize(this);
        blob.gameObject.SetActive(false);

        return blob;
    }

    public XPBlob Spawn(Vector3 position, float xpValue)
    {
        XPBlob blob = pool.Count > 0 ? pool.Pop() : CreateBlob();

        blob.SetInPool(false);
        blob.PrepareForSpawn(xpValue);

        blob.transform.position = position;
        blob.gameObject.SetActive(true);

        XPBlobCollector.NotifyBlobSpawned(blob);

        return blob;
    }

    public void Return(XPBlob blob)
    {
        if (blob == null || blob.IsInPool)
            return;

        blob.ResetForPool();
        blob.gameObject.SetActive(false);

        if (pool.Count < maxPoolSize)
        {
            blob.transform.SetParent(transform);
            blob.SetInPool(true);
            pool.Push(blob);
        }
        else
        {
            Destroy(blob.gameObject);
        }
    }
}
