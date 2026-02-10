// =======================
// ExperimentController.cs
// SAFE version for wheel input + scene transitions
// Fixes stuck throttle by resetting input + timescale before loads
// =======================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum BlockType { Day = 1, Night = 2 }
public enum Expectancy { Expected, Unexpected }
public enum SignalColor { Red, Amber }
public enum MergeSide { Left, Right }

[System.Serializable]
public struct TrialCondition
{
    public bool isPractice;
    public Expectancy expectancy;
    public SignalColor signalColor;
    public MergeSide mergeSide;
}

[System.Serializable]
public class SceneRef
{
    [SerializeField] private string sceneName;

#if UNITY_EDITOR
    [SerializeField] private SceneAsset sceneAsset;
#endif

    public string Name => (sceneName ?? "").Trim();

#if UNITY_EDITOR
    public void SyncNameFromAsset()
    {
        if (sceneAsset != null)
            sceneName = sceneAsset.name;
    }
#endif
}

public class ExperimentController : MonoBehaviour
{
    public static ExperimentController Instance { get; private set; }

    [Header("UI (SubBlock Scene)")]
    public TMP_InputField participantIdInput;
    public TMP_Dropdown blockDropdown;

    [Header("Trial Scenes")]
    public List<SceneRef> practiceScenes = new();
    public List<SceneRef> mainScenes = new();

    [Header("Trial Counts")]
    public int practiceTrialCount = 4;
    public int mainTrialCount = 16;

    [Header("Shuffle")]
    public bool shufflePracticeOrder = true;
    public bool shuffleMainOrder = true;

    [Header("Scene Names")]
    public string subBlockSceneName = "SubBlock";

    // ===== Runtime =====
    [Header("Runtime (read-only)")]
    public string participantID;
    public BlockType currentBlock;
    public int currentTrialIndex = -1;
    public bool experimentRunning = false;

    private readonly List<string> trialSceneOrder = new();
    private readonly List<TrialCondition> trialConditions = new();

    private bool waitingForMainStart = false;
    private readonly List<Selectable> disabledSelectables = new();

    public TrialCondition CurrentCondition
    {
        get
        {
            if (currentTrialIndex >= 0 && currentTrialIndex < trialConditions.Count)
                return trialConditions[currentTrialIndex];

            return new TrialCondition
            {
                isPractice = true,
                expectancy = Expectancy.Expected,
                signalColor = SignalColor.Red,
                mergeSide = MergeSide.Left
            };
        }
    }

    // ================= LIFECYCLE =================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (practiceScenes != null)
            foreach (var s in practiceScenes) s?.SyncNameFromAsset();

        if (mainScenes != null)
            foreach (var s in mainScenes) s?.SyncNameFromAsset();
    }
#endif

    // ================= SUBBLOCK START =================

    public void OnStartButtonPressed()
    {
        // DEFENSIVE: always resume time + clear inputs
        Time.timeScale = 1f;
        Input.ResetInputAxes();

        participantID = string.IsNullOrWhiteSpace(participantIdInput?.text)
            ? "P000"
            : participantIdInput.text;

        currentBlock = (blockDropdown != null && blockDropdown.value == 1)
            ? BlockType.Night
            : BlockType.Day;

        BuildTrialSceneOrder();
        BuildTrialConditionSchedule();

        experimentRunning = true;
        waitingForMainStart = false;
        currentTrialIndex = -1;

        // NEW: show warning immediately after Start on SubBlock before first practice trial loads
        Practice_Warning warning = FindObjectOfType<Practice_Warning>(true);
        if (warning == null)
        {
            // If not found, just proceed
            LoadNextTrial();
            return;
        }

        // Make sure only the warning UI is interactable while it's up
        DisableAllUISelectablesExcept(warning.transform);

        warning.countdownFormat = "Practice trials begin in {0}...";
        warning.Show(() =>
        {
            RestoreDisabledUISelectables();

            Time.timeScale = 1f;
            Input.ResetInputAxes();

            LoadNextTrial(); // starts practice trial 1
        });
    }

    // ================= TRIAL FLOW =================

    public void OnTrialFinished()
    {
        LoadNextTrial();
    }

    private void LoadNextTrial()
    {
        currentTrialIndex++;

        // EXPERIMENT FINISHED
        if (currentTrialIndex >= trialSceneOrder.Count)
        {
            Debug.Log($"[ExperimentController] Finished experiment for {participantID} ({currentBlock}).");

            experimentRunning = false;
            currentTrialIndex = -1;

            Time.timeScale = 1f;
            Input.ResetInputAxes();

            SceneManager.LoadScene(subBlockSceneName, LoadSceneMode.Single);
            Destroy(gameObject);
            return;
        }

        // PRACTICE → MAIN GATE
        if (currentTrialIndex == practiceTrialCount)
        {
            waitingForMainStart = true;
            Time.timeScale = 0f;

            Input.ResetInputAxes();
            SceneManager.LoadScene(subBlockSceneName, LoadSceneMode.Single);
            return;
        }

        LoadSceneSafe(trialSceneOrder[currentTrialIndex]);
    }

    private void LoadCurrentMainTrialScene()
    {
        if (currentTrialIndex < 0 || currentTrialIndex >= trialSceneOrder.Count)
            return;

        LoadSceneSafe(trialSceneOrder[currentTrialIndex]);
    }

    // ================= SCENE LOAD SAFETY =================

    private void LoadSceneSafe(string sceneName)
    {
        Debug.Log($"[ExperimentController] Loading trial {currentTrialIndex}: {sceneName}");

        // CRITICAL FIX
        Time.timeScale = 1f;
        Input.ResetInputAxes();

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!waitingForMainStart) return;
        if (!string.Equals(scene.name, subBlockSceneName, StringComparison.OrdinalIgnoreCase)) return;

        Practice_Warning warning = FindObjectOfType<Practice_Warning>(true);

        if (warning == null)
        {
            Debug.LogWarning("[ExperimentController] Practice_Warning not found. Continuing.");
            waitingForMainStart = false;
            Time.timeScale = 1f;
            Input.ResetInputAxes();
            LoadCurrentMainTrialScene();
            return;
        }

        DisableAllUISelectablesExcept(warning.transform);

        warning.countdownFormat = "Main trials begin in {0}...";
        warning.Show(() =>
        {
            waitingForMainStart = false;

            RestoreDisabledUISelectables();

            Time.timeScale = 1f;
            Input.ResetInputAxes();

            LoadCurrentMainTrialScene();
        });
    }

    // ================= UI HELPERS =================

    private void DisableAllUISelectablesExcept(Transform keepEnabledRoot)
    {
        disabledSelectables.Clear();

        Selectable[] all = FindObjectsOfType<Selectable>(true);
        foreach (Selectable s in all)
        {
            if (s == null) continue;
            if (keepEnabledRoot != null && s.transform.IsChildOf(keepEnabledRoot))
                continue;

            if (s.interactable)
            {
                s.interactable = false;
                disabledSelectables.Add(s);
            }
        }
    }

    private void RestoreDisabledUISelectables()
    {
        foreach (Selectable s in disabledSelectables)
            if (s != null)
                s.interactable = true;

        disabledSelectables.Clear();
    }

    // ================= TRIAL ORDER =================

    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        for (int i = 0; i < practiceTrialCount; i++)
            trialSceneOrder.Add(practiceScenes[i % practiceScenes.Count].Name);

        for (int i = 0; i < mainTrialCount; i++)
            trialSceneOrder.Add(mainScenes[i % mainScenes.Count].Name);

        int practiceEnd = Mathf.Min(practiceTrialCount, trialSceneOrder.Count);

        if (shufflePracticeOrder && practiceEnd > 1)
            ShuffleRange(trialSceneOrder, 0, practiceEnd);

        if (shuffleMainOrder && trialSceneOrder.Count > practiceEnd + 1)
            ShuffleRange(trialSceneOrder, practiceEnd, trialSceneOrder.Count);
    }

    private void ShuffleRange(List<string> list, int startInclusive, int endExclusive)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            int j = UnityEngine.Random.Range(i, endExclusive);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ================= CONDITIONS =================

    private void BuildTrialConditionSchedule()
    {
        trialConditions.Clear();

        var combos = new List<(Expectancy, SignalColor)>
        {
            (Expectancy.Expected, SignalColor.Red),
            (Expectancy.Expected, SignalColor.Amber),
            (Expectancy.Unexpected, SignalColor.Red),
            (Expectancy.Unexpected, SignalColor.Amber)
        };

        foreach (var c in combos)
        {
            trialConditions.Add(new TrialCondition
            {
                isPractice = true,
                expectancy = c.Item1,
                signalColor = c.Item2,
                mergeSide = UnityEngine.Random.value < 0.5f ? MergeSide.Left : MergeSide.Right
            });
        }

        foreach (var c in combos)
        {
            for (int i = 0; i < 4; i++)
            {
                trialConditions.Add(new TrialCondition
                {
                    isPractice = false,
                    expectancy = c.Item1,
                    signalColor = c.Item2,
                    mergeSide = (i < 2) ? MergeSide.Left : MergeSide.Right
                });
            }
        }
    }
}
