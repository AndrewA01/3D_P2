using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class G920V1 : MonoBehaviour

{
    LogitechGSDK.LogiControllerPropertiesData properties;
    
    public float xAxes,GasInput,BreakInput;

    [SerializeField] WheelCollider FL;
    [SerializeField] WheelCollider FR;
    [SerializeField] WheelCollider RL;
    [SerializeField] WheelCollider RR;

    public float acceleration = 600f;
    public float breakingForce = 250f;
    public float maxTurnAngle = 15f;

    private float currentAcceleration = 0f;
    private float currentBreakForce = 0f;
    private float currentTurnAngle = 0f;

    private void Start()
    {
        print(LogitechGSDK.LogiSteeringInitialize(false));
    }

    private void Update()
    {
        if (LogitechGSDK.LogiUpdate() && LogitechGSDK.LogiIsConnected(0))
        {
            LogitechGSDK.DIJOYSTATE2ENGINES rec;
            rec = LogitechGSDK.LogiGetStateUnity(0);

            // STEERING WHEEL
            xAxes = rec.lX / 32768f;

            // Gas 
            if(rec.lY > 0)
            {
                GasInput = 0;
            }

            // Adjust this midpoint?
            else if(rec.lY < 0)
            {
                GasInput = rec.lY / -32768f;
            }

            // Break 
            if(rec.lRz > 0)
            {
                BreakInput = 0;
            }

            // Adjust this midpoint?
            else if (rec.lRz < 0)
            {
                BreakInput = rec.lRz / -32768f;
            }

        }
        else
        {
            print("No steering wheel?");
        }
    }
        private void FixedUpdate()
    {
        currentAcceleration = acceleration * GasInput;
        currentBreakForce = breakingForce * BreakInput;

        FL.motorTorque = currentAcceleration;
        FR.motorTorque = currentAcceleration;
        RL.motorTorque = currentAcceleration;
        RR.motorTorque = currentAcceleration;

        FL.brakeTorque = currentBreakForce;
        FR.brakeTorque = currentBreakForce;
        RL.brakeTorque = currentBreakForce;
        RL.brakeTorque = currentBreakForce;

        // STEERING
        currentTurnAngle = maxTurnAngle * xAxes;
        FL.steerAngle = currentTurnAngle;
        FR.steerAngle = currentTurnAngle;
    }
}