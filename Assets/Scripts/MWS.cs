using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MWS : MonoBehaviour

{
    // Reference to tires
    [SerializeField] WheelCollider FL;
    [SerializeField] WheelCollider FR;
    [SerializeField] WheelCollider RL;
    [SerializeField] WheelCollider RR;

    // public float values
    public float acceleration = 500f;
    public float breakingForce = 250f;
    public float maxTurnAngle = 15f;

    // private float values
    private float currentAcceleration = 0f;
    private float currentBreakForce = 0f;
    private float currentTurnAngle = 0f;

    private void FixedUpdate()
    {
        currentAcceleration = acceleration * Input.GetAxis("Vertical");

        // Apply Braking force with Input.
        if (Input.GetKey(KeyCode.Space))
            currentBreakForce = breakingForce;
        else 
            currentBreakForce = 0f;

        // BELOW HAS TO BE PROGRAMMED TO RESPOND TO BRAKE AXIS FROM WHEEL

        // if (Input.GetAxis("Brake3"))
        //     currentBreakForce = breakingForce;
        // else 
        //     currentBreakForce = 0f;

        // if (Input.GetAxis("Brake4"))
        //     currentBreakForce = breakingForce;
        // else 
        //     currentBreakForce = 0f;


        // AWD torque
        FL.motorTorque = currentAcceleration;
        FR.motorTorque = currentAcceleration;
        RL.motorTorque = currentAcceleration;
        RR.motorTorque = currentAcceleration;
        // Braking
        FL.brakeTorque = currentBreakForce;
        FR.brakeTorque = currentBreakForce;
        RL.brakeTorque = currentBreakForce;
        RL.brakeTorque = currentBreakForce;

        // Turning FL and FR tire
        currentTurnAngle = maxTurnAngle * Input.GetAxis("Horizontal");
        FL.steerAngle = currentTurnAngle;
        FR.steerAngle = currentTurnAngle;
    }
}