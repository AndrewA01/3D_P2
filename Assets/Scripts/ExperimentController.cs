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
    // Runtime-safe scene name
    [SerializeField] private string sceneName;

#if UNITY_EDITOR
    // Drag-and-drop in Inspector (Editor-only)
    [SerializeField] private SceneAsset sceneAsset;
#endif

    public string Name => (sceneName ?? "").Trim();

#if UNITY_EDITOR
    public void SyncNameFromAsset()
    {
        if (sceneAsset != null)
            sceneName = sceneAsset.name; // must match Build Settings name
    }
#endif
}

public class ExperimentController : MonoBehaviour
{
    // Singleton
    public static ExperimentController Instance { get; private set; }

    [Header("UI (SubBlock Scene)")]
    public TMP_InputField participantIdInput;
    public TMP_Dropdown blockDropdown; // 0 = Day, 1 = Night

    [Header("Trial Scenes (drag scenes here)")]
    public List<SceneRef> practiceScenes = new();
    public List<SceneRef> mainScenes = new();

    [Header("Trial Counts")]
    public int practiceTrialCount = 4;
    public int mainTrialCount = 16;

    [Header("Shuffle")]
    public bool shufflePracticeOrder = true;
    public bool shuffleMainOrder = true;

    [Header("Post-Trial Question Scene (drag scene here)")]
    public SceneRef postTrialQuestionScene;

    [Header("Player Object Lookup (by name)")]
    [Tooltip("Exact GameObject name of the player-controlled car in EVERY trial scene. Example: 'Car 1'")]
    public string playerObjectName = "Car 1";

    [Tooltip("Disable/enable MonoBehaviours on the player car AND its children.")]
    public bool includeChildren = true;

    // ====== Runtime fields other scripts rely on ======
    [Header("Runtime (read-only)")]
    public string participantID;
    public BlockType currentBlock;
    public int currentTrialIndex = -1;
    public bool experimentRunning = false;

    // Internal schedules
    private readonly List<string> trialSceneOrder = new();
    private readonly List<TrialCondition> trialConditions = new();

    // Property other scripts rely on
    public TrialCondition CurrentCondition
    {
        get
        {
            if (currentTrialIndex >= 0 && currentTrialIndex < trialConditions.Count)
                return trialConditions[currentTrialIndex];

            // fallback
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
            foreach (var s in practiceScenes)
                s?.SyncNameFromAsset();

        if (mainScenes != null)
            foreach (var s in mainScenes)
                s?.SyncNameFromAsset();

        postTrialQuestionScene?.SyncNameFromAsset();
    }
#endif

    // Called by Start button in SubBlock scene
    public void OnStartButtonPressed()
    {
        participantID = string.IsNullOrWhiteSpace(participantIdInput?.text) ? "P000" : participantIdInput.text;
        currentBlock = (blockDropdown != null && blockDropdown.value == 1) ? BlockType.Night : BlockType.Day;

        BuildTrialSceneOrder();
        BuildTrialConditionSchedule();

        experimentRunning = true;
        LoadNextTrial();
    }

    // Called by your "end trial" trigger (P key, merge event, etc.)
    public void GoToPostTrialQuestion()
    {
        string ptq = postTrialQuestionScene != null ? postTrialQuestionScene.Name : "";

        if (string.IsNullOrWhiteSpace(ptq))
        {
            Debug.LogError("[ExperimentController] PostTrialQuestion scene not assigned. Drag it into 'Post-Trial Question Scene'.");
            return;
        }

        Debug.Log("[ExperimentController] Loading PTQ scene: " + ptq);
        SceneManager.LoadScene(ptq);
    }

    // Called by PTQ script after countdown finishes
    public void OnTrialFinished()
    {
        LoadNextTrial();
    }

    private void LoadNextTrial()
    {
        currentTrialIndex++;

        if (currentTrialIndex >= trialSceneOrder.Count)
        {
            Debug.Log($"[ExperimentController] Finished all trials for {participantID} ({currentBlock}). Returning to SubBlock.");

            experimentRunning = false;
            currentTrialIndex = -1;

            SceneManager.LoadScene("SubBlock");
            Destroy(gameObject);
            return;
        }

        string next = trialSceneOrder[currentTrialIndex];
        Debug.Log($"[ExperimentController] Trial {currentTrialIndex} loading scene: {next}");
        SceneManager.LoadScene(next);
    }

    // ===== Controls toggle for PTQ =====
    public void SetParticipantControlsEnabled(bool enabled)
    {
        GameObject player = FindPlayerObject();
        if (player == null)
        {
            Debug.LogWarning($"[ExperimentController] Could not find player object named '{playerObjectName}' in this scene.");
            return;
        }

        if (includeChildren)
        {
            var behaviours = player.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                if (b == this) continue;
                b.enabled = enabled;
            }
        }
        else
        {
            var behaviours = player.GetComponents<MonoBehaviour>();
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                if (b == this) continue;
                b.enabled = enabled;
            }
        }

        Debug.Log($"[ExperimentController] Player controls {(enabled ? "ENABLED" : "DISABLED")} for '{playerObjectName}'.");
    }

    private GameObject FindPlayerObject()
    {
        if (string.IsNullOrWhiteSpace(playerObjectName))
            return null;

        // Fast path
        GameObject exact = GameObject.Find(playerObjectName);
        if (exact != null) return exact;

        // Fallback: root search
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var r in roots)
        {
            if (r != null && r.name == playerObjectName)
                return r;
        }

        return null;
    }

    // ===== Trial scene order =====
    private void BuildTrialSceneOrder()
    {
        trialSceneOrder.Clear();

        // Practice section
        for (int i = 0; i < practiceTrialCount; i++)
        {
            if (practiceScenes == null || practiceScenes.Count == 0) break;
            trialSceneOrder.Add(practiceScenes[i % practiceScenes.Count].Name);
        }

        // Main section
        for (int i = 0; i < mainTrialCount; i++)
        {
            if (mainScenes == null || mainScenes.Count == 0) break;
            trialSceneOrder.Add(mainScenes[i % mainScenes.Count].Name);
        }

        // Shuffle within sections (optional)
        int practiceEnd = Mathf.Min(practiceTrialCount, trialSceneOrder.Count);

        if (shufflePracticeOrder && practiceEnd > 1)
            ShuffleRange(trialSceneOrder, 0, practiceEnd);

        if (shuffleMainOrder && trialSceneOrder.Count > practiceEnd + 1)
            ShuffleRange(trialSceneOrder, practiceEnd, trialSceneOrder.Count);

        currentTrialIndex = -1;
    }

    private void ShuffleRange(List<string> list, int startInclusive, int endExclusive)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            int j = Random.Range(i, endExclusive);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ===== Trial conditions =====
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

        // Practice: 4 trials (1 per combo)
        foreach (var c in combos)
        {
            trialConditions.Add(new TrialCondition
            {
                isPractice = true,
                expectancy = c.Item1,
                signalColor = c.Item2,
                mergeSide = Random.value < 0.5f ? MergeSide.Left : MergeSide.Right
            });
        }

        // Main: 16 trials (4 per combo: 2 left, 2 right)
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

        // NOTE: If you shuffle scenes independently, condition order stays as built above.
        // If you need conditions to follow the same shuffle pattern, tell me and I’ll align them.
    }
}
