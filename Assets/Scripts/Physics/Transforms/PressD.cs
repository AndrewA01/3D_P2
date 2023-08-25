using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PressD : MonoBehaviour
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
        if(Input.GetKey("d"))
        {
            rb.AddForce(transform.right * speed);
        }
    }
}
