using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerOffMapReturn : MonoBehaviour
{
    public Transform repositionPoint;
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CharacterController cc = other.GetComponent<CharacterController>();

            if (cc != null)
            {
                cc.enabled = false;
                other.transform.position = repositionPoint.position;
                cc.enabled = true;
            }
        }
    }
}
