using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public enum BlockType
{
    Day = 1,
    Night = 2
}

public enum Expectancy
{
    Expected,
    Unexpected
}

public enum SignalColor
{
    Red,
    Amber
}

public enum MergeSide
{
    Left,
    Right
}

[System.Serializable]
public struct TrialCondition
{
    public bool isPractice;
    public Expectancy expectancy;
    public SignalColor signalColor;
    public MergeSide mergeSide;
}

public class ExperimentController : MonoBehaviour
{
    // --------- SINGLETON ---------
    public static ExperimentController Instance { get; private set; }

    // --------- ASSIGN IN INSPECTOR (IN START SCENE) ---------
    [Header("UI (Start Scene)")]
    public TMP_InputField participantIdInput;
    public TMP_Dropdown blockDropdown; // 0 = Day, 1 = Night

    [Header("Scenes")]
    public List<string> practiceScenes;
    public List<string> mainScenes;

    // --------- RUNTIME INFO (ACCESSIBLE FROM ANY SCENE) ---------
    [Header("Runtime (read-only)")]
    public string participantID;
    public BlockType currentBlock;
    public int currentTrialIndex = -1;
    public bool experimentRunning = false;

    // Scene order
    private List<string> trialSceneOrder = new List<string>();

    // Condition order
    private List<TrialCondition> trialConditions = new List<TrialCondition>();

    /// <summary>
    /// Access the current trial's condition.
    /// </summary>
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

    private void Start()
    {
        UnityEngine.Random.InitState(System.DateTime.Now.Millisecond);
    }

    // =========================================================
    //                   UI ENTRY POINT
    // =========================================================
    public void OnStartButtonPressed()
    {
        participantID = participantIdInput != null ? participantIdInput.text : "P000";

        int selected = blockDropdown.value;
        currentBlock = (selected == 0) ? BlockType.Day : BlockType.Night;

        BuildTrialSceneOrder();
        BuildTrialConditionSchedule();

        experimentRunning = true;
        LoadNextTrial();
    }

    // =========================================================
    //              TRIAL ORDER & RANDOMIZATION
    // =========================================================
    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        int totalPractice = 4;
        int totalMain = 16;

        if (practiceScenes.Count == 0)
            Debug.LogError("No practice scenes assigned.");

        if (practiceScenes.Count == 1)
        {
            for (int i = 0; i < totalPractice; i++)
                trialSceneOrder.Add(practiceScenes[0]);
        }
        else
        {
            for (int i = 0; i < totalPractice; i++)
                trialSceneOrder.Add(practiceScenes[i % practiceScenes.Count]);
        }

        if (mainScenes.Count == 0)
            Debug.LogError("No main scenes assigned.");

        if (mainScenes.Count == 1)
        {
            for (int i = 0; i < totalMain; i++)
                trialSceneOrder.Add(mainScenes[0]);
        }
        else
        {
            for (int i = 0; i < totalMain; i++)
                trialSceneOrder.Add(mainScenes[i % mainScenes.Count]);
        }

        currentTrialIndex = -1;
    }

    private void BuildTrialConditionSchedule()
    {
        trialConditions.Clear();

        var baseCombos = new List<(Expectancy, SignalColor)>
        {
            (Expectancy.Expected, SignalColor.Red),
            (Expectancy.Expected, SignalColor.Amber),
            (Expectancy.Unexpected, SignalColor.Red),
            (Expectancy.Unexpected, SignalColor.Amber)
        };

        // ---------- PRACTICE ----------
        List<TrialCondition> practiceList = new List<TrialCondition>();

        foreach (var combo in baseCombos)
        {
            practiceList.Add(new TrialCondition
            {
                isPractice = true,
                expectancy = combo.Item1,
                signalColor = combo.Item2,
                mergeSide = (UnityEngine.Random.value < 0.5f) ? MergeSide.Left : MergeSide.Right
            });
        }

        ShuffleList(practiceList);

        // ---------- MAIN ----------
        List<TrialCondition> mainList = new List<TrialCondition>();

        foreach (var combo in baseCombos)
        {
            for (int i = 0; i < 2; i++)
                mainList.Add(new TrialCondition
                {
                    isPractice = false,
                    expectancy = combo.Item1,
                    signalColor = combo.Item2,
                    mergeSide = MergeSide.Left
                });

            for (int i = 0; i < 2; i++)
                mainList.Add(new TrialCondition
                {
                    isPractice = false,
                    expectancy = combo.Item1,
                    signalColor = combo.Item2,
                    mergeSide = MergeSide.Right
                });
        }

        ShuffleList(mainList);

        trialConditions.AddRange(practiceList);
        trialConditions.AddRange(mainList);

        if (trialConditions.Count != trialSceneOrder.Count)
            Debug.LogWarning("Condition count does not match scene count.");
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = UnityEngine.Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // =========================================================
    //                  TRIAL FLOW
    // =========================================================
    public void OnTrialFinished()
    {
        LoadNextTrial();
    }

    private void LoadNextTrial()
    {
        currentTrialIndex++;

        if (currentTrialIndex >= trialSceneOrder.Count)
        {
            Debug.Log($"All trials finished for {participantID}, block {currentBlock}");

            experimentRunning = false;
            currentTrialIndex = -1;

            Instance = null;
            Destroy(gameObject);
            SceneManager.LoadScene("SubBlock");
            return;
        }

        SceneManager.LoadScene(trialSceneOrder[currentTrialIndex]);
    }

    public bool IsPracticeTrial() => currentTrialIndex >= 0 && currentTrialIndex < 4;
    public int TotalTrialsInBlock() => 20;
}
