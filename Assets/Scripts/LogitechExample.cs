using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LogitechExample : MonoBehaviour

{
    LogitechGSDK.LogiControllerPropertiesData properties;
    
    public float xAxes,GasInput,BreakInput,ClutchInput;

    public int CurrentGear;

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

            xAxes = rec.lX / 32768f;

            // Gas 
            if(rec.lY > 0)
            {
                GasInput = 0;
            }else if(rec.lY < 0)

            {
                GasInput = rec.lY / -32768f;
            }
            // Break 
            if(rec.lRz > 0)
            {
                BreakInput = 0;
            }else if (rec.lRz < 0)
            {
                BreakInput = rec.lRz / -32768f;
            }
            // Clutch 
            if (rec.rglSlider[0] > 0)
            {
                ClutchInput = 0;
            }else if (rec.rglSlider[0] < 0)
            {
                ClutchInput = rec.rglSlider[0] / -32768f;
            }

        }
        else
        {
            print("No steering wheel?");
        }
    }
}

