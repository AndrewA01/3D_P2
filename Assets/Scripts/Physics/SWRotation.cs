using UnityEngine;
using System.Collections;

// A very simplistic car driving on the x-z plane.

public class SWRotation : MonoBehaviour
{
    public float speed = 10.0f;
    public float rotationSpeed = 100.0f;

    void Update()
    {
        // Get the horizontal axis.
        // By default they are mapped to the arrow keys.
        // The value is in the range -1 to 1
    
        float rotation = Input.GetAxis("Horizontal") * rotationSpeed;

        // Make it move 10 meters per second instead of 10 meters per frame...

        rotation *= Time.deltaTime;

        // Rotate around our y-axis
        transform.Rotate(0, rotation, 0);
    }
}