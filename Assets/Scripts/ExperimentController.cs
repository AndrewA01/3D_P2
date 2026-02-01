using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

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

        string next = trialSceneOrder[currentTrialIndex];
        Debug.Log($"[ExperimentController] Loading trial {currentTrialIndex}: {next}");
        SceneManager.LoadScene(next, LoadSceneMode.Single);
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
