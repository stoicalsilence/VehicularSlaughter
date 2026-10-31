using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ScrollTexture : MonoBehaviour
{
    public Vector2 scrollSpeed = new Vector2(0.1f, 0f);
    private Material materialInstance;
    private Vector2 offset;

    void Awake()
    {
        materialInstance = GetComponent<Renderer>().material;
    }

    void Update()
    {
        offset += scrollSpeed * Time.deltaTime;
        materialInstance.mainTextureOffset = offset;
    }
}
