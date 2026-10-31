using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotator : MonoBehaviour
{
    public Vector3 axis = Vector3.up; // same as green gizmo (Y)
    public float speed = 90f; // degrees per second

    void Update()
    {
        transform.rotation *= Quaternion.AngleAxis(speed * Time.deltaTime, axis);
    }
}
