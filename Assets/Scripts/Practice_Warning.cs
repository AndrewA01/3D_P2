using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Practice_Warning : MonoBehaviour
{
    [Header("UI References (assign these)")]
    [Tooltip("The big instructions paragraph (optional; only used to cache/restore).")]
    public TextMeshProUGUI instructionsText;

    [Tooltip("The TMP that should display the countdown (THIS should be your 'New Text').")]
    public TextMeshProUGUI countdownLabel;

    public Button continueButton;

    [Header("Countdown After Continue")]
    public int countdownSeconds = 5;

    private Action onFinished;
    private Coroutine countdownRoutine;
    private string originalInstructions;

    private Canvas myCanvas;

    private void Awake()
    {
        myCanvas = GetComponent<Canvas>();
        if (myCanvas != null)
        {
            myCanvas.overrideSorting = true;
            myCanvas.sortingOrder = 100;
        }

        // Don't show at scene start
        gameObject.SetActive(false);

        // IMPORTANT: Don't nuke other listeners here; just add ours.
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinuePressed);
    }

    public void Show(Action onFinishedCallback)
    {
        onFinished = onFinishedCallback;

        Time.timeScale = 0f;

        if (instructionsText != null)
            originalInstructions = instructionsText.text;

        // Ensure countdown text is blank until Continue is pressed
        if (countdownLabel != null)
            countdownLabel.text = "";

        if (continueButton != null)
        {
            continueButton.gameObject.SetActive(true);
            continueButton.interactable = true;
        }

        gameObject.SetActive(true);
    }

    private void OnContinuePressed()
    {
        // Prevent double-press
        if (continueButton != null)
        {
            continueButton.interactable = false;
            continueButton.gameObject.SetActive(false);
        }

        if (countdownRoutine != null)
            StopCoroutine(countdownRoutine);

        countdownRoutine = StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        for (int t = countdownSeconds; t > 0; t--)
        {
            if (countdownLabel != null)
                countdownLabel.text = $"Main trials begin in {t}...";

            yield return new WaitForSecondsRealtime(1f);
        }

        if (countdownLabel != null)
            countdownLabel.text = "";

        if (instructionsText != null)
            instructionsText.text = originalInstructions;

        Time.timeScale = 1f;
        gameObject.SetActive(false);

        onFinished?.Invoke();
        onFinished = null;
    }
}
