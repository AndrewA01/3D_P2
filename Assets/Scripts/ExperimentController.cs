// =======================
// ExperimentController.cs
// Drop-in replacement for your current file.
// Changes vs your earlier version:
//   - After 4 practice trials, loads SubBlock and shows Practice_Warning overlay
//   - Participant must press Continue, then 5s countdown runs, then main trials begin
//   - While Practice_Warning is up, ALL other UI Selectables are disabled (so Start/Dropdown/ID can't be clicked)
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
    public TMP_Dropdown blockDropdown; // 0 = Day, 1 = Night

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

    // gate between practice and main
    private bool waitingForMainStart = false;

    // UI disable/restore while warning is visible
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

    // ===== SubBlock Start =====
    public void OnStartButtonPressed()
    {
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

        LoadNextTrial();
    }

    // ===== Called by PostTrialOverlay =====
    public void OnTrialFinished()
    {
        LoadNextTrial();
    }

    // ===== Scene Flow =====
    private void LoadNextTrial()
    {
        currentTrialIndex++;

        if (currentTrialIndex >= trialSceneOrder.Count)
        {
            Debug.Log($"[ExperimentController] Finished experiment for {participantID} ({currentBlock}).");

            experimentRunning = false;
            currentTrialIndex = -1;

            SceneManager.LoadScene(subBlockSceneName, LoadSceneMode.Single);
            Destroy(gameObject);
            return;
        }

        // After the last practice trial completes, currentTrialIndex becomes practiceTrialCount (e.g., 4).
        // Go to SubBlock and show Practice_Warning overlay that requires Continue + countdown.
        if (currentTrialIndex == practiceTrialCount)
        {
            waitingForMainStart = true;
            Time.timeScale = 0f;
            SceneManager.LoadScene(subBlockSceneName, LoadSceneMode.Single);
            return;
        }

        string next = trialSceneOrder[currentTrialIndex];
        Debug.Log($"[ExperimentController] Loading trial {currentTrialIndex}: {next}");
        SceneManager.LoadScene(next, LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!waitingForMainStart) return;
        if (!string.Equals(scene.name, subBlockSceneName, StringComparison.OrdinalIgnoreCase)) return;

        Practice_Warning warning = FindObjectOfType<Practice_Warning>(true);

        if (warning == null)
        {
            Debug.LogWarning("[ExperimentController] Practice_Warning not found. Continuing to main trials.");
            waitingForMainStart = false;
            Time.timeScale = 1f;
            LoadCurrentMainTrialScene();
            return;
        }

        // Disable all other UI controls so participant can't click Start/Dropdown/ID etc.
        DisableAllUISelectablesExcept(warning.transform);

        warning.Show(() =>
        {
            waitingForMainStart = false;

            RestoreDisabledUISelectables();

            // Defensive: warning also sets this back to 1
            Time.timeScale = 1f;

            LoadCurrentMainTrialScene();
        });
    }

    private void LoadCurrentMainTrialScene()
    {
        if (currentTrialIndex < 0 || currentTrialIndex >= trialSceneOrder.Count)
            return;

        string next = trialSceneOrder[currentTrialIndex];
        Debug.Log($"[ExperimentController] Starting main trials. Loading trial {currentTrialIndex}: {next}");
        SceneManager.LoadScene(next, LoadSceneMode.Single);
    }

    // ===== UI helper: disable every Selectable except those under the Practice_Warning overlay =====
    private void DisableAllUISelectablesExcept(Transform keepEnabledRoot)
    {
        disabledSelectables.Clear();

        Selectable[] all = FindObjectsOfType<Selectable>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Selectable s = all[i];
            if (s == null) continue;

            // Keep anything that is part of the warning overlay
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
        for (int i = 0; i < disabledSelectables.Count; i++)
        {
            if (disabledSelectables[i] != null)
                disabledSelectables[i].interactable = true;
        }
        disabledSelectables.Clear();
    }

    // ===== Trial Order =====
    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        for (int i = 0; i < practiceTrialCount; i++)
        {
            if (practiceScenes == null || practiceScenes.Count == 0) break;
            trialSceneOrder.Add(practiceScenes[i % practiceScenes.Count].Name);
        }

        for (int i = 0; i < mainTrialCount; i++)
        {
            if (mainScenes == null || mainScenes.Count == 0) break;
            trialSceneOrder.Add(mainScenes[i % mainScenes.Count].Name);
        }

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

    // ===== Trial Conditions =====
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

        // 4 practice trials
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

        // 16 main trials (each combo 4 times; left twice, right twice)
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
