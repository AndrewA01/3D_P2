using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SC2 : MonoBehaviour
{
    public float speed = 10f;
    public float rotation = 10f;

    void Start()
    {
        
    }

    void Update()
    {
        Movement();
    }

    void Movement()
    {
        float forwardMovement = Input.GetAxis("Vertical") * speed * Time.deltaTime;
        float turnMovement = Input.GetAxis("Horizontal") * rotation * Time.deltaTime;

        transform.Translate(Vector3.forward * forwardMovement);
        transform.Rotate(Vector3.up * turnMovement);
    }
}
