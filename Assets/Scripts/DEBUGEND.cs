using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DEBUGEND : MonoBehaviour
{
    public void EndTrialNow()
    {
        Debug.Log("DEBUGEND: EndTrialNow() called");

        if (ExperimentController.Instance != null)
        {
            Debug.Log("DEBUGEND: Found ExperimentController, calling OnTrialFinished()");
            ExperimentController.Instance.OnTrialFinished();
        }
        else
        {
            Debug.LogWarning("DEBUGEND: No ExperimentController found! Did you start from your subblock scene?");
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("DEBUGEND: P key pressed");
            EndTrialNow();
        }
    }
}
