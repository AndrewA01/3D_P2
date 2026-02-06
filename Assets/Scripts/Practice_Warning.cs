// =======================
// Practice_Warning.cs
// Attach to: Practice_Warning (Canvas)
// Assign in Inspector:
//   - countdownText: your Instructions (TextMeshProUGUI) OR Text (TMP)
//   - continueButton: your Continue button
// Behavior:
//   - ONLY runs when ExperimentController calls Show(...)
//   - Freezes time, waits for Continue click
//   - Then appends a 5s countdown to YOUR existing text
//   - Unfreezes, hides itself, calls back to controller
// =======================

using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Practice_Warning : MonoBehaviour
{
    [Header("UI References (you assign these)")]
    public TextMeshProUGUI countdownText;
    public Button continueButton;

    [Header("Countdown After Continue")]
    public int countdownSeconds = 5;

    private Action onFinished;
    private Coroutine routine;
    private string originalText;

    private Canvas myCanvas;

    private void Awake()
    {
        myCanvas = GetComponent<Canvas>();
        if (myCanvas != null)
        {
            // Ensure this overlay draws above other UI
            myCanvas.overrideSorting = true;
            myCanvas.sortingOrder = 100;
        }

        // Must NOT show at scene start
        gameObject.SetActive(false);

        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinuePressed);
    }

    public void Show(Action onFinishedCallback)
    {
        onFinished = onFinishedCallback;

        // Freeze gameplay (UI still works)
        Time.timeScale = 0f;

        // Cache whatever text YOU set in the Inspector
        if (countdownText != null)
            originalText = countdownText.text;

        // Ensure button is visible
        if (continueButton != null)
            continueButton.gameObject.SetActive(true);

        gameObject.SetActive(true);
    }

    private void OnContinuePressed()
    {
        if (continueButton != null)
            continueButton.gameObject.SetActive(false);

        if (routine != null)
            StopCoroutine(routine);

        routine = StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        for (int t = countdownSeconds; t > 0; t--)
        {
            if (countdownText != null)
            {
                countdownText.text = originalText + $"\n\nMain trials begin in {t}...";
            }
            yield return new WaitForSecondsRealtime(1f);
        }

        // Restore your original instructions text (keeps your UI clean)
        if (countdownText != null)
            countdownText.text = originalText;

        Time.timeScale = 1f;
        gameObject.SetActive(false);

        onFinished?.Invoke();
        onFinished = null;
    }
}
