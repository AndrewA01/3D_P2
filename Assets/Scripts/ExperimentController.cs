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
    public TMP_InputField participantIdInput;  // TextMeshPro InputField
    public TMP_Dropdown blockDropdown;         // TextMeshPro Dropdown (0 = Day, 1 = Night)

    [Header("Scenes")]
    [Tooltip("Scene to load for each practice trial (can be the same or different).")]
    public List<string> practiceScenes;    // ideally size 4, but can also be 1 repeated

    [Tooltip("Scene to load for each main trial (can be same or multiple scenes).")]
    public List<string> mainScenes;        // ideally size 16, but can also be 1 repeated


    // --------- RUNTIME INFO (ACCESSIBLE FROM ANY SCENE) ---------
    [Header("Runtime (read-only)")]
    public string participantID;
    public BlockType currentBlock;
    public int currentTrialIndex = -1;     // 0-based over full list (practice + main)
    public bool experimentRunning = false;

    // Scene order (what to load)
    private List<string> trialSceneOrder = new List<string>();

    // Condition order (what each trial's condition is)
    private List<TrialCondition> trialConditions = new List<TrialCondition>();

    /// <summary>
    /// Convenience accessor for the current trial's condition.
    /// </summary>
    public TrialCondition CurrentCondition
    {
        get
        {
            if (currentTrialIndex >= 0 && currentTrialIndex < trialConditions.Count)
                return trialConditions[currentTrialIndex];

            // Fallback dummy condition
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
        // Simple Singleton / Persistent controller
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
        Random.InitState(System.DateTime.Now.Millisecond);
    }

    // =========================================================
    //                   PUBLIC API (CALLED BY UI)
    // =========================================================

    /// <summary>
    /// Called by the Start button in the start scene.
    /// </summary>
    public void OnStartButtonPressed()
    {
        // 1) Read participant ID
        participantID = participantIdInput != null ? participantIdInput.text : "P000";

        // 2) Determine block from dropdown (0 = Day, 1 = Night)
        int selected = blockDropdown.value;

if (selected == 0)
    currentBlock = BlockType.Day;   // A
else
    currentBlock = BlockType.Night; // B


        // 3) Build randomized scene order and condition schedule
        BuildTrialSceneOrder();
        BuildTrialConditionSchedule();

        experimentRunning = true;

        // 4) Start with first trial
        LoadNextTrial();
    }

    // =========================================================
    //                  TRIAL ORDER & RANDOMIZATION
    // =========================================================

    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        int totalPractice = 4;  // we logically have 4 practice trials
        int totalMain = 16;     // 16 main trials

        // If user only gave 1 practice scene, just repeat it 4 times.
        if (practiceScenes.Count == 0)
        {
            Debug.LogError("No practice scenes assigned in ExperimentController.");
        }

        if (practiceScenes.Count == 1)
        {
            for (int i = 0; i < totalPractice; i++)
                trialSceneOrder.Add(practiceScenes[0]);
        }
        else
        {
            // If they provided multiple scenes, just sample cyclically
            for (int i = 0; i < totalPractice; i++)
            {
                string sceneName = practiceScenes[i % practiceScenes.Count];
                trialSceneOrder.Add(sceneName);
            }
        }

        if (mainScenes.Count == 0)
        {
            Debug.LogError("No main scenes assigned in ExperimentController.");
        }

        if (mainScenes.Count == 1)
        {
            for (int i = 0; i < totalMain; i++)
                trialSceneOrder.Add(mainScenes[0]);
        }
        else
        {
            // Similarly, sample cyclically or however you want
            for (int i = 0; i < totalMain; i++)
            {
                string sceneName = mainScenes[i % mainScenes.Count];
                trialSceneOrder.Add(sceneName);
            }
        }

        // Optional: you can shuffle practice & main scenes separately if they
        // are truly interchangeable. If each scene is very specific, you might
        // want to *not* shuffle here and let conditions & scenes be independent.
        //
        // For now, we leave scenes as-is and randomize conditions instead.
        currentTrialIndex = -1;
    }

    private void BuildTrialConditionSchedule()
    {
        trialConditions.Clear();

        // ---------- PRACTICE (4 trials) ----------
        // 1 of each combination: (Expected/Red, Expected/Amber, Unexpected/Red, Unexpected/Amber)
        List<TrialCondition> practiceList = new List<TrialCondition>();

        var baseCombos = new List<(Expectancy ex, SignalColor col)>
        {
            (Expectancy.Expected,   SignalColor.Red),
            (Expectancy.Expected,   SignalColor.Amber),
            (Expectancy.Unexpected, SignalColor.Red),
            (Expectancy.Unexpected, SignalColor.Amber)
        };

        // One of each combo; random merge side
        foreach (var combo in baseCombos)
        {
            MergeSide side = (Random.value < 0.5f) ? MergeSide.Left : MergeSide.Right;

            TrialCondition tc = new TrialCondition
            {
                isPractice = true,
                expectancy = combo.ex,
                signalColor = combo.col,
                mergeSide = side
            };

            practiceList.Add(tc);
        }

        // Shuffle practice trials
        ShuffleList(practiceList);


        // ---------- MAIN (16 trials) ----------
        // For each of the 4 combos, 4 trials: 2 Left, 2 Right
        List<TrialCondition> mainList = new List<TrialCondition>();

        foreach (var combo in baseCombos)
        {
            // 2 left
            for (int i = 0; i < 2; i++)
            {
                mainList.Add(new TrialCondition
                {
                    isPractice = false,
                    expectancy = combo.ex,
                    signalColor = combo.col,
                    mergeSide = MergeSide.Left
                });
            }

            // 2 right
            for (int i = 0; i < 2; i++)
            {
                mainList.Add(new TrialCondition
                {
                    isPractice = false,
                    expectancy = combo.ex,
                    signalColor = combo.col,
                    mergeSide = MergeSide.Right
                });
            }
        }

        // Shuffle main trials
        ShuffleList(mainList);

        // Combine practice + main
        trialConditions.AddRange(practiceList);
        trialConditions.AddRange(mainList);

        if (trialConditions.Count != trialSceneOrder.Count)
        {
            Debug.LogWarning($"Condition count ({trialConditions.Count}) != scene count ({trialSceneOrder.Count}). They should both be 20.");
        }
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // =========================================================
    //                  TRIAL FLOW CONTROL
    // =========================================================

    public void OnTrialFinished()
    {
        LoadNextTrial();
    }

    private void LoadNextTrial()
{
    currentTrialIndex++;

    // ✅ Finished all trials in this block?
    if (currentTrialIndex >= trialSceneOrder.Count)
    {
        Debug.Log($"All trials finished for participant {participantID} in block {currentBlock}");

        experimentRunning = false;
        currentTrialIndex = -1;  // reset for safety

        // ✅ Destroy this persistent controller so SubBlock can spawn a fresh one
        Instance = null;
        Destroy(gameObject);

        // ✅ Load the SubBlock scene again (your experiment setup scene)
        UnityEngine.SceneManagement.SceneManager.LoadScene("SubBlock");

        return;
    }

    // Otherwise, go to the next trial scene
    string nextScene = trialSceneOrder[currentTrialIndex];
    Debug.Log($"Loading trial {currentTrialIndex}: {nextScene}");
    UnityEngine.SceneManagement.SceneManager.LoadScene(nextScene);
}


    // Helpers for other scripts
    public bool IsPracticeTrial()
    {
        return currentTrialIndex >= 0 && currentTrialIndex < 4;  // by design
    }

    public int TotalTrialsInBlock()
    {
        return 20;
    }
}
