using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

/// <summary>
/// Drag & drop scene reference that converts to a scene name at runtime.
/// </summary>
[System.Serializable]
public class SceneRef
{
    // This is what we actually use at runtime.
    [SerializeField] private string sceneName;

#if UNITY_EDITOR
    // This lets you drag a Scene asset in the inspector.
    [SerializeField] private SceneAsset sceneAsset;
#endif

    public string Name => (sceneName ?? "").Trim();

#if UNITY_EDITOR
    // Called in editor when values change (inspector).
    public void SyncNameFromAsset()
    {
        if (sceneAsset != null)
        {
            sceneName = sceneAsset.name; // scene name must match build settings entry
        }
    }
#endif
}

public class ExperimentController : MonoBehaviour
{
    // --------- SINGLETON ---------
    public static ExperimentController Instance { get; private set; }

    // --------- ASSIGN IN INSPECTOR (IN START SCENE) ---------
    [Header("UI (Start Scene)")]
    public TMP_InputField participantIdInput;  // TextMeshPro InputField
    public TMP_Dropdown blockDropdown;         // TextMeshPro Dropdown (0 = Day, 1 = Night)

    [Header("Scenes (drag & drop Scene assets)")]
    [Tooltip("Scene to load for each practice trial. If only 1, it repeats 4 times.")]
    public List<SceneRef> practiceScenes;

    [Tooltip("Scene to load for each main trial. If only 1, it repeats 16 times.")]
    public List<SceneRef> mainScenes;

    // --------- RUNTIME INFO (ACCESSIBLE FROM ANY SCENE) ---------
    [Header("Runtime (read-only)")]
    public string participantID;
    public BlockType currentBlock;
    public int currentTrialIndex = -1;     // 0-based over full list (practice + main)
    public bool experimentRunning = false;

    // Scene order (what to load)
    private readonly List<string> trialSceneOrder = new List<string>();

    // Condition order (what each trial's condition is)
    private readonly List<TrialCondition> trialConditions = new List<TrialCondition>();

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

#if UNITY_EDITOR
    // Keeps sceneName in sync when you drag SceneAssets in inspector
    private void OnValidate()
    {
        if (practiceScenes != null)
            foreach (var s in practiceScenes)
                if (s != null) s.SyncNameFromAsset();

        if (mainScenes != null)
            foreach (var s in mainScenes)
                if (s != null) s.SyncNameFromAsset();
    }
#endif

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
        if (string.IsNullOrWhiteSpace(participantID))
            participantID = "P000";

        // 2) Determine block from dropdown (0 = Day, 1 = Night)
        int selected = blockDropdown != null ? blockDropdown.value : 0;
        currentBlock = (selected == 0) ? BlockType.Day : BlockType.Night;

        // 3) Build randomized scene order and condition schedule
        BuildTrialSceneOrder();
        BuildTrialConditionSchedule();

        // HARD VALIDATION: if scene list is broken, don't start.
        if (!ValidateTrialSceneOrder())
        {
            experimentRunning = false;
            Debug.LogError("Experiment did NOT start because scene list validation failed. Fix the errors above.");
            return;
        }

        experimentRunning = true;

        Debug.Log($"[ExperimentController] START participant={participantID}, block={currentBlock}, trials={trialSceneOrder.Count}");
        Debug.Log("[ExperimentController] Trial scene order:");
        for (int i = 0; i < trialSceneOrder.Count; i++)
            Debug.Log($"  {i}: {trialSceneOrder[i]}");

        // 4) Start with first trial
        LoadNextTrial();
    }

    // =========================================================
    //                  TRIAL ORDER & RANDOMIZATION
    // =========================================================

    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        const int totalPractice = 4;
        const int totalMain = 16;

        // Practice
        if (practiceScenes == null || practiceScenes.Count == 0)
        {
            Debug.LogError("No practice scenes assigned in ExperimentController.");
        }
        else if (practiceScenes.Count == 1)
        {
            string n = practiceScenes[0]?.Name ?? "";
            for (int i = 0; i < totalPractice; i++)
                trialSceneOrder.Add(n);
        }
        else
        {
            for (int i = 0; i < totalPractice; i++)
            {
                string n = practiceScenes[i % practiceScenes.Count]?.Name ?? "";
                trialSceneOrder.Add(n);
            }
        }

        // Main
        if (mainScenes == null || mainScenes.Count == 0)
        {
            Debug.LogError("No main scenes assigned in ExperimentController.");
        }
        else if (mainScenes.Count == 1)
        {
            string n = mainScenes[0]?.Name ?? "";
            for (int i = 0; i < totalMain; i++)
                trialSceneOrder.Add(n);
        }
        else
        {
            for (int i = 0; i < totalMain; i++)
            {
                string n = mainScenes[i % mainScenes.Count]?.Name ?? "";
                trialSceneOrder.Add(n);
            }
        }

        currentTrialIndex = -1;
    }

    private void BuildTrialConditionSchedule()
    {
        trialConditions.Clear();

        // ---------- PRACTICE (4 trials) ----------
        List<TrialCondition> practiceList = new List<TrialCondition>();

        var baseCombos = new List<(Expectancy ex, SignalColor col)>
        {
            (Expectancy.Expected,   SignalColor.Red),
            (Expectancy.Expected,   SignalColor.Amber),
            (Expectancy.Unexpected, SignalColor.Red),
            (Expectancy.Unexpected, SignalColor.Amber)
        };

        foreach (var combo in baseCombos)
        {
            MergeSide side = (Random.value < 0.5f) ? MergeSide.Left : MergeSide.Right;

            practiceList.Add(new TrialCondition
            {
                isPractice = true,
                expectancy = combo.ex,
                signalColor = combo.col,
                mergeSide = side
            });
        }

        ShuffleList(practiceList);

        // ---------- MAIN (16 trials) ----------
        List<TrialCondition> mainList = new List<TrialCondition>();

        foreach (var combo in baseCombos)
        {
            for (int i = 0; i < 2; i++)
                mainList.Add(new TrialCondition { isPractice = false, expectancy = combo.ex, signalColor = combo.col, mergeSide = MergeSide.Left });

            for (int i = 0; i < 2; i++)
                mainList.Add(new TrialCondition { isPractice = false, expectancy = combo.ex, signalColor = combo.col, mergeSide = MergeSide.Right });
        }

        ShuffleList(mainList);

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
    //                      VALIDATION
    // =========================================================

    private bool ValidateTrialSceneOrder()
    {
        if (trialSceneOrder.Count == 0)
        {
            Debug.LogError("trialSceneOrder is empty. Assign practice/main scenes in the inspector.");
            return false;
        }

        bool ok = true;
        for (int i = 0; i < trialSceneOrder.Count; i++)
        {
            string scene = (trialSceneOrder[i] ?? "").Trim();

            if (string.IsNullOrWhiteSpace(scene))
            {
                Debug.LogError($"Trial {i} has an EMPTY scene name. This usually happens when you used strings and didn't type them, or SceneRef wasn't assigned.");
                ok = false;
                continue;
            }

            if (!IsSceneInBuild(scene))
            {
                Debug.LogError(
                    $"Scene '{scene}' (trial {i}) is NOT in Build Settings.\n" +
                    $"Fix: File → Build Settings → 'Scenes In Build' → Add/Open the scene and click 'Add Open Scenes'."
                );
                ok = false;
            }
        }

        return ok;
    }

    private bool IsSceneInBuild(string sceneName)
    {
        // Check by name against scenes in build.
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName)
                return true;
        }
        return false;
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

        // Finished all trials in this block?
        if (currentTrialIndex >= trialSceneOrder.Count)
        {
            Debug.Log($"All trials finished for participant {participantID} in block {currentBlock}");

            experimentRunning = false;
            currentTrialIndex = -1;

            Instance = null;
            Destroy(gameObject);

            SceneManager.LoadScene("SubBlock");
            return;
        }

        string nextScene = (trialSceneOrder[currentTrialIndex] ?? "").Trim();

        if (string.IsNullOrWhiteSpace(nextScene))
        {
            Debug.LogError($"Next scene is empty at trial {currentTrialIndex}. Check your assigned scenes in the inspector.");
            return;
        }

        if (!IsSceneInBuild(nextScene))
        {
            Debug.LogError($"Cannot load '{nextScene}' because it's not in Build Settings. See earlier errors.");
            return;
        }

        Debug.Log($"Loading trial {currentTrialIndex}: {nextScene}");
        SceneManager.LoadScene(nextScene);
    }

    // Helpers for other scripts
    public bool IsPracticeTrial()
    {
        return currentTrialIndex >= 0 && currentTrialIndex < 4;
    }

    public int TotalTrialsInBlock()
    {
        return 20;
    }
}
