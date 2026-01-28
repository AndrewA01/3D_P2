using UnityEngine;

public class HeadlightsBlockToggle : MonoBehaviour
{
    [Tooltip("If true, headlights turn on only during Night (Block B).")]
    public bool enableOnlyForNightBlock = true;

    private void Start()
    {
        // Safety check
        if (ExperimentController.Instance == null)
        {
            Debug.LogWarning("[HeadlightsBlockToggle] ExperimentController not found. Headlights will remain OFF.");
            gameObject.SetActive(false);
            return;
        }

        bool isNight = ExperimentController.Instance.currentBlock == BlockType.Night;

        // Core logic
        if (enableOnlyForNightBlock)
        {
            gameObject.SetActive(isNight);
        }
        else
        {
            gameObject.SetActive(true);
        }

        Debug.Log(
            $"[HeadlightsBlockToggle] Block={ExperimentController.Instance.currentBlock} → " +
            $"Headlights {(gameObject.activeSelf ? "ON" : "OFF")}"
        );
    }
}
