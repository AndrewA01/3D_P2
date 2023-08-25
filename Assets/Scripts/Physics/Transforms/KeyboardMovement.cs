using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KeyboardMovement : MonoBehaviour
{
    public float speed = 25f;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>(); 
    }
    void FixedUpdate()
    {
        if(Input.GetKey("w"))
        {
            rb.AddForce(transform.forward * speed);
        }

        if(Input.GetKey("a"))
        {
            rb.AddForce(-transform.right * speed);
        }

        if(Input.GetKey("s"))
        {
            rb.AddForce(-transform.forward * speed);
        }

        if(Input.GetKey("d"))
        {
            rb.AddForce(transform.right * speed);
        }

        // transform. up is green axis (y), forward is blue axis (z), right is red axis (x)

        if(Input.GetKey(KeyCode.LeftShift))
        {
            rb.AddForce(-transform.up * speed);
        }

        if(Input.GetKey(KeyCode.Space))
        {
            rb.AddForce(transform.up * speed);
        }

    }
}