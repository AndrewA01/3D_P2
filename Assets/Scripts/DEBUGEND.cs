using UnityEngine;

/// <summary>
/// DEBUGEND: end trial helper — press Key (default P) to stop recording and load PTQ.
/// Hardened logging + public TriggerEnd() for testing.
/// </summary>
public class DEBUGEND : MonoBehaviour
{
    [Header("Keybind")]
    public KeyCode endTrialKey = KeyCode.P;

    [Header("Logging")]
    public string endReason = "Manual_P";

    // prevent double-triggering while transition in progress
    private bool triggered = false;

    // Public test hook (call from UI button to simulate key press)
    public void TriggerEnd()
    {
        if (!gameObject.activeInHierarchy)
            Debug.LogWarning("DEBUGEND.TriggerEnd() called but DEBUGEND GameObject is inactive.");

        Debug.Log("DEBUGEND: TriggerEnd() invoked (manual/API).");
        EndTrialNow();
    }

    public void EndTrialNow()
    {
        if (triggered)
        {
            Debug.Log("DEBUGEND: EndTrialNow() ignored because already triggered.");
            return;
        }

        Debug.Log("DEBUGEND: EndTrialNow() called -> attempting to stop recorder and load PTQ");

        // 1) Stop recording and stamp end time/reason (safe if null)
        if (PositionRecorder3.Current != null)
        {
            Debug.Log("DEBUGEND: Found PositionRecorder3.Current, calling StopRecordingForQuestion()");
            try
            {
                PositionRecorder3.Current.StopRecordingForQuestion(endReason);
            }
            catch (System.Exception e)
            {
                Debug.LogError("DEBUGEND: Exception calling StopRecordingForQuestion(): " + e);
            }
        }
        else
        {
            Debug.LogWarning("DEBUGEND: PositionRecorder3.Current is null (recorder not found in this trial?)");
        }

        // 2) Load Post-Trial Question scene via ExperimentController
        if (ExperimentController.Instance != null)
        {
            Debug.Log("DEBUGEND: Found ExperimentController, calling GoToPostTrialQuestion()");
            try
            {
                // mark triggered now — we attempted the transition
                triggered = true;
                ExperimentController.Instance.GoToPostTrialQuestion();
            }
            catch (System.Exception e)
            {
                Debug.LogError("DEBUGEND: Exception while calling GoToPostTrialQuestion(): " + e);
                // allow retry if call failed
                triggered = false;
            }
        }
        else
        {
            Debug.LogWarning("DEBUGEND: No ExperimentController found! Did you start from your subblock scene?");
            // keep triggered = false so user can retry after fixing controller presence
        }
    }

    private void Update()
    {
        // quick visible debug line to help narrow whether Update is running
        if (Input.GetKeyDown(endTrialKey))
        {
            Debug.Log("DEBUGEND: End key pressed");
            EndTrialNow();
        }
    }
}
