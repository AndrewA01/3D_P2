using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PressA : MonoBehaviour
{
    public float speed = 100f;
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
            rb.AddForce(transform.forward * speed);
        }
    }
}

