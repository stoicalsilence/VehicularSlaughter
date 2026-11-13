using UnityEngine;

[DisallowMultipleComponent]
public sealed class ItemCrate : MonoBehaviour
{
    private PlayerInventory playerInventory;
    bool hasOpened;

    private void Start()
    {
        playerInventory = FindObjectOfType<PlayerInventory>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 6 || collision.gameObject.layer == 8)
        {
            if (hasOpened) return;

            hasOpened = true;

            PlayerInventory inventory = playerInventory;

            inventory.TryOpenCrate(this);
        }
    }

    public void CompleteChoice()
    {
        Destroy(gameObject);
    }
}
