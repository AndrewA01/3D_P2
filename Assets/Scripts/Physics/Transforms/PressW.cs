using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PressW : MonoBehaviour
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
        if(Input.GetKey("w"))
        {
            rb.AddForce(transform.up * speed);
        }
    }
}

