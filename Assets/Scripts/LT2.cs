using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LT2 : MonoBehaviour
{

    public float speed = 10f;
    public float rotation = 10f;
    void Update()
    {
        Movement();
    }

    void Movement()
    {
        float forwardMovement = Input.GetAxis("Vertical") * speed * Time.deltaTime;
        float turnMovement = Input.GetAxis("Horizontal") * rotation * Time.deltaTime;


        if(Input.GetKey("w"))
        {
        transform.Translate(Vector3.forward * forwardMovement);
        }
        if(Input.GetKey("a"))
        {
        transform.Rotate(Vector3.up * turnMovement);
        }
    }
}