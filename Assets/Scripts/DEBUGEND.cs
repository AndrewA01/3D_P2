using UnityEngine;

public class DEBUGEND : MonoBehaviour
{
    [Header("Keybind")]
    public KeyCode endTrialKey = KeyCode.P;

    [Header("Logging")]
    public string endReason = "Manual_P";

    private bool triggered = false;

    public void EndTrialNow()
    {
        if (triggered) return;
        triggered = true;

        Debug.Log("DEBUGEND: EndTrialNow() called -> stopping recorder and loading PTQ");

        // 1) Stop recording and stamp end time/reason (safe if null)
        if (PositionRecorder3.Current != null)
        {
            Debug.Log("DEBUGEND: Found PositionRecorder3.Current, calling StopRecordingForQuestion()");
            PositionRecorder3.Current.StopRecordingForQuestion(endReason);
        }
        else
        {
            Debug.LogWarning("DEBUGEND: PositionRecorder3.Current is null (recorder not found in this trial?)");
        }

        // 2) Load Post-Trial Question scene
        if (ExperimentController.Instance != null)
        {
            Debug.Log("DEBUGEND: Found ExperimentController, calling GoToPostTrialQuestion()");
            ExperimentController.Instance.GoToPostTrialQuestion();
        }
        else
        {
            Debug.LogWarning("DEBUGEND: No ExperimentController found! Did you start from your subblock scene?");
            triggered = false; // allow retry if controller was missing
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(endTrialKey))
        {
            Debug.Log("DEBUGEND: End key pressed");
            EndTrialNow();
        }
    }
}
