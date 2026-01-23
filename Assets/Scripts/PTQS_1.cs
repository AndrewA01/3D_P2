using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PTQS_1 : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text questionText;
    public TMP_Text countdownText;
    public Button buttonA;
    public Button buttonB;

    [Header("Timing")]
    public int countdownSeconds = 5;

    private float shownTime;
    private bool responded = false;

    private void Start()
    {
        Debug.Log("[PTQS_1] PTQ scene started");

        shownTime = Time.time;

        if (countdownText != null)
            countdownText.text = "";

        // Disable controls while in PTQ
        if (ExperimentController.Instance != null)
            ExperimentController.Instance.SetParticipantControlsEnabled(false);

        if (buttonA != null)
            buttonA.onClick.AddListener(() => OnResponse(buttonA));

        if (buttonB != null)
            buttonB.onClick.AddListener(() => OnResponse(buttonB));
    }

    private void OnResponse(Button btn)
    {
        if (responded) return;
        responded = true;

        if (buttonA != null) buttonA.interactable = false;
        if (buttonB != null) buttonB.interactable = false;

        string question = questionText != null ? questionText.text : "";
        string response = GetButtonLabel(btn);
        float rt = Time.time - shownTime;

        if (PositionRecorder3.Current != null)
        {
            PositionRecorder3.Current.SetSurveyResult(question, response, rt);
            PositionRecorder3.Current.SaveNowAndCleanup();
        }
        else
        {
            Debug.LogWarning("[PTQS_1] PositionRecorder3.Current is null");
        }

        StartCoroutine(CountdownThenNext());
    }

    private IEnumerator CountdownThenNext()
    {
        int remaining = Mathf.Max(0, countdownSeconds);

        while (remaining > 0)
        {
            if (countdownText != null)
                countdownText.text = remaining.ToString();

            yield return new WaitForSeconds(1f);
            remaining--;
        }

        if (countdownText != null)
            countdownText.text = "0";

        // Re-enable controls before next trial
        if (ExperimentController.Instance != null)
            ExperimentController.Instance.SetParticipantControlsEnabled(true);

        // Next trial
        if (ExperimentController.Instance != null)
            ExperimentController.Instance.OnTrialFinished();
        else
            Debug.LogError("[PTQS_1] ExperimentController.Instance is null");
    }

    private string GetButtonLabel(Button btn)
    {
        if (btn == null) return "";
        TMP_Text t = btn.GetComponentInChildren<TMP_Text>(true);
        return t != null ? t.text : btn.name;
    }
}
