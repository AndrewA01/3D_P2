using System;
using System.Collections.Generic;
using System.Linq;
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

    [Header("Scene Names")]
    [Tooltip("Name of the start/menu scene.")]
    public string subBlockSceneName = "SubBlock";

    [Header("Player Object Lookup (by name)")]
    [Tooltip("Exact GameObject name of the player-controlled car in EVERY trial scene. Example: 'Car 1' or 'Car1'")]
    public string playerObjectName = "Car 1";

    [Tooltip("Disable/enable MonoBehaviours on the player car AND its children.")]
    public bool includeChildren = true;

    [Header("Cleanup & Camera Rebind (fixes lingering car/camera issues)")]
    [Tooltip("If true, destroys any lingering player objects with the same name that survive across scene loads (e.g., in DontDestroyOnLoad).")]
    public bool destroyLingeringPlayersOnSceneLoad = true;

    [Tooltip("If true, tries to rebind camera follow target to the current scene's player after every trial scene load.")]
    public bool rebindCameraOnTrialLoad = true;

    [Tooltip("Optional: If your camera is a persistent rig with a known name, list it here (e.g., 'CameraRig'). Leave empty to just use Camera.main.")]
    public string persistentCameraRigName = "";

    // ===== Runtime fields other scripts rely on =====
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
        SceneManager.LoadScene(ptq, LoadSceneMode.Single);
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
            Debug.Log($"[ExperimentController] Finished all trials for {participantID} ({currentBlock}). Returning to {subBlockSceneName}.");

            experimentRunning = false;
            currentTrialIndex = -1;

            SceneManager.LoadScene(subBlockSceneName, LoadSceneMode.Single);
            Destroy(gameObject);
            return;
        }

        string next = trialSceneOrder[currentTrialIndex];
        Debug.Log($"[ExperimentController] Trial {currentTrialIndex} loading scene: {next}");
        SceneManager.LoadScene(next, LoadSceneMode.Single);
    }

    // ===== Controls toggle for PTQ =====
    public void SetParticipantControlsEnabled(bool enabled)
    {
        GameObject player = FindPlayerObjectInActiveScene();
        if (player == null)
        {
            Debug.LogWarning($"[ExperimentController] Could not find player object named '{playerObjectName}' in the ACTIVE scene.");
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

    // ===== Scene load hook: cleanup lingering player + rebind camera =====
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!experimentRunning) return;

        string ptqName = postTrialQuestionScene != null ? postTrialQuestionScene.Name : "";
        bool isPTQ = !string.IsNullOrWhiteSpace(ptqName) && scene.name == ptqName;
        bool isSubBlock = !string.IsNullOrWhiteSpace(subBlockSceneName) && scene.name == subBlockSceneName;

        // If we're in PTQ, usually no car should be active; but we still cleanup if something persisted.
        if (destroyLingeringPlayersOnSceneLoad)
        {
            CleanupLingeringPlayers(scene);
        }

        // Only rebind camera on actual trial scenes (not PTQ and not menu)
        if (rebindCameraOnTrialLoad && !isPTQ && !isSubBlock)
        {
            var player = FindPlayerObjectInActiveScene();
            if (player != null)
            {
                RebindCameraTo(player.transform);
            }
        }
    }

    /// <summary>
    /// Destroys extra player objects with the same name that survive across loads (common culprit is DontDestroyOnLoad).
    /// Keeps the one in the ACTIVE scene (if present).
    /// </summary>
    private void CleanupLingeringPlayers(Scene activeScene)
    {
        if (string.IsNullOrWhiteSpace(playerObjectName)) return;

        // Find all objects with that name across loaded + DontDestroyOnLoad
        var all = FindAllGameObjectsByName(playerObjectName);
        if (all.Count <= 1) return;

        // Prefer keeping the one in the active scene (the newly loaded trial scene)
        GameObject keep = all.FirstOrDefault(go => go != null && go.scene == activeScene);

        // If none are in active scene, keep the first (better than deleting all)
        if (keep == null)
            keep = all.FirstOrDefault(go => go != null);

        foreach (var go in all)
        {
            if (go == null) continue;
            if (go == keep) continue;

            Debug.LogWarning($"[ExperimentController] Destroying lingering player duplicate '{go.name}' from scene '{go.scene.name}'.");
            Destroy(go);
        }
    }

    private GameObject FindPlayerObjectInActiveScene()
    {
        if (string.IsNullOrWhiteSpace(playerObjectName))
            return null;

        // Fast path: finds in active scene, but also can find DontDestroyOnLoad.
        // We specifically want the one belonging to the ACTIVE scene.
        var candidates = FindAllGameObjectsByName(playerObjectName);
        if (candidates.Count == 0) return null;

        var active = SceneManager.GetActiveScene();
        var inActive = candidates.FirstOrDefault(go => go != null && go.scene == active);
        if (inActive != null) return inActive;

        // Fallback
        return candidates.FirstOrDefault(go => go != null);
    }

    /// <summary>
    /// Robustly finds objects by name across loaded scenes + DontDestroyOnLoad via Resources.FindObjectsOfTypeAll.
    /// </summary>
    private static List<GameObject> FindAllGameObjectsByName(string exactName)
    {
        var results = new List<GameObject>();
        if (string.IsNullOrWhiteSpace(exactName)) return results;

        var allGos = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in allGos)
        {
            if (go == null) continue;
            if (go.name != exactName) continue;

            // Skip editor-only / hidden assets
            if ((go.hideFlags & HideFlags.HideInHierarchy) != 0) continue;

            // Must be part of a valid scene (including DontDestroyOnLoad)
            if (!go.scene.IsValid()) continue;

            results.Add(go);
        }

        // De-dup just in case
        return results.Distinct().ToList();
    }

    private void RebindCameraTo(Transform target)
    {
        if (target == null) return;

        // Use persistent rig if named
        Camera cam = null;

        if (!string.IsNullOrWhiteSpace(persistentCameraRigName))
        {
            var rig = GameObject.Find(persistentCameraRigName);
            if (rig != null)
                cam = rig.GetComponentInChildren<Camera>(true);
        }

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
        {
            Debug.LogWarning("[ExperimentController] No camera found to rebind (Camera.main is null).");
            return;
        }

        // 1) Try Cinemachine via reflection (no hard dependency)
        TryRebindCinemachine(target);

        // 2) Try common follow scripts on the camera
        TrySetTargetOnBehaviours(cam.gameObject, target);

        Debug.Log($"[ExperimentController] Camera rebind attempted to '{target.name}'.");
    }

    private void TryRebindCinemachine(Transform target)
    {
        // Look for components named "CinemachineVirtualCamera" and set Follow/LookAt via reflection.
        var allBehaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        foreach (var b in allBehaviours)
        {
            if (b == null) continue;
            var t = b.GetType();
            if (t.FullName == null) continue;

            if (!t.FullName.Contains("CinemachineVirtualCamera")) continue;

            var followProp = t.GetProperty("Follow");
            var lookAtProp = t.GetProperty("LookAt");

            if (followProp != null && followProp.CanWrite)
                followProp.SetValue(b, target, null);

            if (lookAtProp != null && lookAtProp.CanWrite)
                lookAtProp.SetValue(b, target, null);
        }
    }

    private void TrySetTargetOnBehaviours(GameObject cameraGO, Transform target)
    {
        var behaviours = cameraGO.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var b in behaviours)
        {
            if (b == null) continue;
            var type = b.GetType();

            // Try common fields/properties: "target", "Target", "followTarget", "FollowTarget"
            SetIfExists(type, b, "target", target);
            SetIfExists(type, b, "Target", target);
            SetIfExists(type, b, "followTarget", target);
            SetIfExists(type, b, "FollowTarget", target);
        }
    }

    private void SetIfExists(Type type, object instance, string memberName, Transform target)
    {
        var field = type.GetField(memberName);
        if (field != null && field.FieldType == typeof(Transform))
        {
            field.SetValue(instance, target);
            return;
        }

        var prop = type.GetProperty(memberName);
        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(Transform))
        {
            prop.SetValue(instance, target, null);
        }
    }

    // ===== Trial scene order =====
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

        currentTrialIndex = -1;
    }

    private void ShuffleRange(List<string> list, int startInclusive, int endExclusive)
    {
        for (int i = startInclusive; i < endExclusive; i++)
        {
            int j = UnityEngine.Random.Range(i, endExclusive);
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
