using UnityEngine;

[DisallowMultipleComponent]
public sealed class StatTradePickup : MonoBehaviour
{
    private StatTradeSystem statTradeSys;
    bool hasOpened;

    private void Start()
    {
        statTradeSys = FindObjectOfType<StatTradeSystem>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 6 || collision.gameObject.layer == 8)
        {
            if (hasOpened) return;

            hasOpened = true;

            statTradeSys.TryOpen(this);
        }
    }

    public void CompleteChoice()
    {
        Destroy(gameObject);
    }
}
