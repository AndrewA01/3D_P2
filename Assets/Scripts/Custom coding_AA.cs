using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CCAA : MonoBehaviour
{
    public float speed = 10.0f;
    public float rotationSpeed = 100.0f;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>(); 
    }
    void FixedUpdate()
    {
        if(Input.GetKey("a"))
        {
            rb.AddForce(transform.up * speed);
        }
    }
}

