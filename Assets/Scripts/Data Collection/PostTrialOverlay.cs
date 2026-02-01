using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PostTrialOverlay : MonoBehaviour
{
    public static PostTrialOverlay Instance { get; private set; }

    [Header("UI (existing TrialRuntime canvas)")]
    public Canvas rootCanvas;
    public GameObject panelRoot;
    public Button[] answerButtons;
    public TextMeshProUGUI countdownText;

    [Header("Trial End")]
    [Tooltip("Press P to end the trial")]
    public bool allowPKey = true;

    [Tooltip("Leave at 0 to DISABLE timed ending")]
    public float trialDurationSeconds = 0f;

    [Header("After Response")]
    public float countdownSecondsAfterChoice = 5f;

    private bool trialRunning = true;
    private bool overlayShown = false;
    private bool countingDown = false;

    private float trialTimer = 0f;
    private float countdownTimer = 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (rootCanvas == null)
            rootCanvas = GetComponentInChildren<Canvas>(true);

        // Hook up answer buttons once
        foreach (var b in answerButtons)
        {
            if (b == null) continue;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(OnAnswerClicked);
        }

        HideOverlayImmediate();
    }

    private void Update()
    {
        // ----- TRIAL RUNNING -----
        if (trialRunning && !overlayShown)
        {
            // Manual end
            if (allowPKey && Input.GetKeyDown(KeyCode.P))
            {
                EndTrial();
                return;
            }

            // Timed end ONLY if value > 0
            if (trialDurationSeconds > 0f)
            {
                trialTimer += Time.deltaTime;
                if (trialTimer >= trialDurationSeconds)
                {
                    EndTrial();
                    return;
                }
            }
        }

        // ----- POST-RESPONSE COUNTDOWN -----
        if (countingDown)
        {
            countdownTimer -= Time.unscaledDeltaTime;

            if (countdownText != null)
                countdownText.text = $"Next trial in {Mathf.CeilToInt(countdownTimer)}";

            if (countdownTimer <= 0f)
            {
                countingDown = false;
                FinishTrial();
            }
        }
    }

    private void EndTrial()
    {
        if (overlayShown) return;

        trialRunning = false;
        overlayShown = true;

        // Freeze simulation
        Time.timeScale = 0f;

        // Show overlay
        if (panelRoot != null) panelRoot.SetActive(true);
        if (rootCanvas != null) rootCanvas.enabled = true;

        // Enable answer buttons
        foreach (var b in answerButtons)
            if (b != null) b.interactable = true;

        if (countdownText != null)
            countdownText.text = "";
    }

    private void OnAnswerClicked()
    {
        if (countingDown) return;

        // Lock buttons
        foreach (var b in answerButtons)
            if (b != null) b.interactable = false;

        // Start 5-second countdown
        countdownTimer = countdownSecondsAfterChoice;
        countingDown = true;
    }

    private void FinishTrial()
    {
        HideOverlayImmediate();

        // Unfreeze
        Time.timeScale = 1f;

        // Advance experiment
        ExperimentController.Instance?.OnTrialFinished();
    }

    private void HideOverlayImmediate()
    {
        overlayShown = false;
        countingDown = false;

        if (panelRoot != null) panelRoot.SetActive(false);
        if (rootCanvas != null) rootCanvas.enabled = false;

        if (countdownText != null)
            countdownText.text = "";
    }
}
