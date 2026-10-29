using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RunOverExplodeable : MonoBehaviour
{
    public AudioClip explodeSound;
    public GameObject explodeParticles;
    public float requiredCarSpeed;

    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.layer == 6)
        {
            CarController car = other.GetComponent<CarController>();

            if(car.carSpeed > requiredCarSpeed)
            {
                AudioManager.Play3D(explodeSound, transform.position);
                GameObject particles = Instantiate(explodeParticles, transform.position, Quaternion.identity);
                Destroy(particles, 5);
                Destroy(this.transform.parent.gameObject);
            }
        }
    }
}
