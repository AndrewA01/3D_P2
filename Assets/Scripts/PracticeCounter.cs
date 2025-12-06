using UnityEngine;
using TMPro;

public class PracticeCounter : MonoBehaviour
{
    public TMP_Text practiceText;   // Drag your TMP text object here

    void Start()
    {
        if (ExperimentController.Instance == null)
        {
            Debug.LogWarning("ExperimentController not found.");
            practiceText.gameObject.SetActive(false);
            return;
        }

        int trialIndex = ExperimentController.Instance.currentTrialIndex;

        // Only show for trials 0–3
        if (trialIndex >= 0 && trialIndex <= 3)
        {
            int practiceNumber = trialIndex + 1; // Convert 0-based → 1-based
            practiceText.text = $"Practice Trial {practiceNumber}";
            practiceText.gameObject.SetActive(true);
        }
        else
        {
            practiceText.gameObject.SetActive(false);
        }
    }
}
