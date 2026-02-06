using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class DataRecorderV2 : MonoBehaviour
{
    public static DataRecorderV2 Instance;

    [Header("Target Lookup")]
    public string playerObjectName = "Car 1";

    [Header("Sampling")]
    [Tooltip("Seconds between rows (0.1 = 10 Hz).")]
    [SerializeField] private float sampleIntervalSeconds = 0.1f;

    [Header("Save Paths")]
    [Tooltip("Play Mode save folder (relative to project root or absolute). Default: Assets/CSVCollection/NewPL")]
    [SerializeField] private string playModeSaveDirectory = "Assets/CSVCollection/NewPL";

    [Tooltip("Build Mode save folder. Leave blank to use Application.persistentDataPath.")]
    [SerializeField] private string buildModeSaveDirectory = "";

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    // Buffer (write at end)
    private StringBuilder buffer;

    // Save folder + final file path
    private string fullFolderPath;
    private bool hasFlushed = false;

    // Target + speed
    private GameObject car;
    private Rigidbody carRb;
    private Vector3 lastPos;
    private float lastPosTime;
    private float speedMph;

    // Sampling timer (unscaled)
    private float nextSampleTime;

    // Experiment/block/trial state
    private string participantID = "UNKNOWN";
    private string blockLabel = "UNKNOWN";

    // Restart each new scene
    private float blockStartAbs = -1f;

    private int currentTrialIndex = -1;
    private float trialStartAbs = -1f;

    // Trial metadata from ExperimentController.CurrentCondition
    private string trialType = "";
    private string expectancy = "";
    private string signalColor = "";
    private string mergeSide = "";

    // Trial end + survey
    private bool trialEnded = false;
    private string trialEndReason = "";
    private float trialEndAbs = -1f;
    private float trialEndRel = -1f;

    private float surveyStartAbs = -1f;
    private string surveyResponse = "";
    private float surveyRT = -1f;

    // Optional metric (if you set it externally)
    private float laneDeviation = float.NaN;

    // PostTrialOverlay button hook
    private bool surveyButtonsHooked = false;

    // Detect trial end via timescale pause
    private float lastTimeScale = 1f;

    // Car 2 merge tracking
    private MoveOnWaypoints car2Mover;
    private bool car2MergeLogged = false; // ensures Car2Marge is a one-row pulse

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;

        buffer = new StringBuilder(1024 * 64);
        buffer.Append(GetHeaderLine());
    }

    private void Start()
    {
        ResolveFolderPathOnly();
        RefreshFromExperimentController();

        nextSampleTime = Time.unscaledTime + sampleIntervalSeconds;
        lastTimeScale = Time.timeScale;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnApplicationQuit()
    {
        FlushToDiskAtEnd();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        FlushToDiskAtEnd();
    }

    private void Update()
    {
        RefreshFromExperimentController();
        TryHookSurveyButtons();
        DetectTrialEndTransition();

        if (Time.unscaledTime >= nextSampleTime)
        {
            nextSampleTime += sampleIntervalSeconds;
            SampleAndBufferRow();
        }

        lastTimeScale = Time.timeScale;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Restart BlockStartAbs every scene
        blockStartAbs = Time.realtimeSinceStartup;

        car = FindInActiveSceneByName(playerObjectName);
        carRb = car != null ? car.GetComponent<Rigidbody>() : null;

        // Refresh Car2 mover reference per scene
        car2Mover = FindObjectOfType<MoveOnWaypoints>();
        car2MergeLogged = false;

        if (car != null)
        {
            lastPos = car.transform.position;
            lastPosTime = Time.unscaledTime;
            speedMph = 0f;

            if (verboseLogs)
                Debug.Log($"[DataRecorderV2] Found '{playerObjectName}' in scene '{scene.name}'. BlockStartAbs reset.");
        }
        else
        {
            if (verboseLogs)
                Debug.LogWarning($"[DataRecorderV2] '{playerObjectName}' NOT found in scene '{scene.name}'. X/Y/Z will be blank until found. BlockStartAbs reset.");
        }

        surveyButtonsHooked = false;
    }

    public void SetLaneDeviation(float deviation)
    {
        laneDeviation = deviation;
    }

    private void RefreshFromExperimentController()
    {
        if (ExperimentController.Instance == null)
            return;

        if (!string.IsNullOrWhiteSpace(ExperimentController.Instance.participantID))
            participantID = ExperimentController.Instance.participantID.Trim();

        blockLabel = ExperimentController.Instance.currentBlock.ToString();

        int idx = ExperimentController.Instance.currentTrialIndex;
        if (idx != currentTrialIndex)
        {
            currentTrialIndex = idx;

            if (ExperimentController.Instance.experimentRunning && currentTrialIndex >= 0)
            {
                trialStartAbs = Time.realtimeSinceStartup;

                trialEnded = false;
                trialEndReason = "";
                trialEndAbs = -1f;
                trialEndRel = -1f;

                surveyStartAbs = -1f;
                surveyResponse = "";
                surveyRT = -1f;
            }
        }

        var cond = ExperimentController.Instance.CurrentCondition;

        trialType = cond.isPractice ? "Practice" : "Main";
        expectancy = cond.expectancy.ToString();
        signalColor = cond.signalColor.ToString();
        mergeSide = cond.mergeSide.ToString();
    }

    private void DetectTrialEndTransition()
    {
        if (lastTimeScale > 0f && Time.timeScale == 0f && !trialEnded)
        {
            trialEnded = true;
            trialEndAbs = Time.realtimeSinceStartup;
            trialEndRel = GetTrialTimeRel();
            trialEndReason = "TrialEnd";

            surveyStartAbs = Time.realtimeSinceStartup;

            SampleAndBufferRow();
        }
    }

    private void TryHookSurveyButtons()
    {
        if (surveyButtonsHooked)
            return;

        var overlay = PostTrialOverlay.Instance;
        if (overlay == null || overlay.answerButtons == null || overlay.answerButtons.Length == 0)
            return;

        for (int i = 0; i < overlay.answerButtons.Length; i++)
        {
            int capturedIndex = i;
            Button b = overlay.answerButtons[i];
            if (b == null) continue;

            b.onClick.AddListener(() => OnSurveyButtonClicked(overlay, capturedIndex));
        }

        surveyButtonsHooked = true;

        if (verboseLogs)
            Debug.Log("[DataRecorderV2] Hooked PostTrialOverlay answer buttons for survey logging.");
    }

    private void OnSurveyButtonClicked(PostTrialOverlay overlay, int buttonIndex)
    {
        string resp = $"Button{buttonIndex}";

        Button b = (overlay != null && overlay.answerButtons != null && buttonIndex >= 0 && buttonIndex < overlay.answerButtons.Length)
            ? overlay.answerButtons[buttonIndex]
            : null;

        if (b != null)
        {
            var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text))
                resp = tmp.text.Trim();
            else
                resp = b.name;
        }

        surveyResponse = resp;

        if (surveyStartAbs > 0f)
            surveyRT = Time.realtimeSinceStartup - surveyStartAbs;
        else
            surveyRT = -1f;

        SampleAndBufferRow();
    }

    private void SampleAndBufferRow()
    {
        float timeAbs = Time.realtimeSinceStartup;
        float blockTimeRel = (blockStartAbs > 0f) ? (timeAbs - blockStartAbs) : -1f;
        float trialTimeRel = GetTrialTimeRel();

        float x = float.NaN, y = float.NaN, z = float.NaN;
        if (car != null)
        {
            Vector3 p = car.transform.position;
            x = p.x; y = p.y; z = p.z;
            UpdateSpeedMph(p);
        }

        string sceneName = SceneManager.GetActiveScene().name;

        // SubBlock scenes: blank these fields because they aren't chosen yet
        bool isSubBlock = !string.IsNullOrWhiteSpace(sceneName) &&
                          sceneName.ToLowerInvariant().Contains("subblock");

        string eventValue = isSubBlock ? "" : trialType;
        string expValue = isSubBlock ? "" : expectancy;
        string colorValue = isSubBlock ? "" : signalColor;
        string mergeValue = isSubBlock ? "" : mergeSide;

        // SurveyResponseCorrect:
        // blank if can't evaluate; else 1/0 if matches SignalColor (case-insensitive)
        string surveyCorrect = "";
        if (!string.IsNullOrWhiteSpace(surveyResponse) && !string.IsNullOrWhiteSpace(colorValue))
        {
            bool match = string.Equals(
                surveyResponse.Trim(),
                colorValue.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
            surveyCorrect = match ? "1" : "0";
        }

        // Inputs (scaled)
        float steeringInput = GetSteeringScaledMinus100To100(); // -100..100
        float brakeInput = GetBrakeScaled0To100();              // 0..100
        float throttleInput = GetThrottleScaled0To100();        // 0..100

        // Car2Marge: one-row pulse at merge trigger moment
        int car2Marge = 0;
        if (!car2MergeLogged && car2Mover != null && car2Mover.HasMergeStarted && car2Mover.MergeStartAbs > 0f)
        {
            if (timeAbs >= car2Mover.MergeStartAbs)
            {
                car2Marge = 1;
                car2MergeLogged = true;
            }
        }

        string row =
            Csv(participantID) + "," +
            Csv(blockLabel) + "," +
            F(blockStartAbs) + "," +
            F(blockTimeRel) + "," +
            currentTrialIndex.ToString() + "," +
            Csv(sceneName) + "," +
            Csv(eventValue) + "," +
            Csv(expValue) + "," +
            Csv(colorValue) + "," +
            Csv(mergeValue) + "," +
            F(timeAbs) + "," +
            F(trialTimeRel) + "," +
            F(x) + "," +
            F(y) + "," +
            F(z) + "," +
            F(laneDeviation) + "," +
            F(speedMph) + "," +
            (trialEnded ? "1" : "0") + "," +
            Csv(trialEndReason) + "," +
            F(trialEndAbs) + "," +
            F(trialEndRel) + "," +
            Csv(surveyResponse) + "," +
            Csv(surveyCorrect) + "," +
            F(surveyRT) + "," +
            F(brakeInput) + "," +
            F(throttleInput) + "," +
            F(steeringInput) + "," +
            car2Marge.ToString() +
            "\n";

        buffer.Append(row);
    }

    private float GetSteeringScaledMinus100To100()
    {
        // Prefer wheel/controller axis first (usually mapped)
        float axis = TryGetAxisRawSafe("Horizontal");
        if (!float.IsNaN(axis))
            return Mathf.Clamp(axis * 100f, -100f, 100f);

        // Fallback keyboard
        float kb = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kb -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kb += 1f;

        if (Mathf.Abs(kb) > 0f) return kb * 100f;
        return 0f;
    }

    private float GetBrakeScaled0To100()
    {
        // Prefer wheel brake axis if present
        float brake01 = TryGetAxis01Safe("Brake");
        if (!float.IsNaN(brake01))
            return Mathf.Clamp(brake01 * 100f, 0f, 100f);

        // Keyboard brake fallback (Space)
        return Input.GetKey(KeyCode.Space) ? 100f : 0f;
    }

    private float GetThrottleScaled0To100()
    {
        // Prefer wheel throttle axis if present
        float thr01 = TryGetAxis01Safe("Throttle");
        if (!float.IsNaN(thr01))
            return Mathf.Clamp(thr01 * 100f, 0f, 100f);

        // Keyboard throttle fallback (W)
        return Input.GetKey(KeyCode.W) ? 100f : 0f;
    }

    // -1..1 -> 0..1 conversion (safe). Returns NaN if axis doesn't exist.
    private float TryGetAxis01Safe(string axisName)
    {
        try
        {
            float raw = Input.GetAxisRaw(axisName); // often -1..1
            float v01 = Mathf.Clamp01((raw + 1f) * 0.5f);
            return v01;
        }
        catch
        {
            return float.NaN;
        }
    }

    // Returns axis raw (-1..1) or NaN if axis doesn't exist.
    private float TryGetAxisRawSafe(string axisName)
    {
        try
        {
            return Input.GetAxisRaw(axisName);
        }
        catch
        {
            return float.NaN;
        }
    }

    private float GetTrialTimeRel()
    {
        if (trialStartAbs < 0f) return -1f;
        return Time.realtimeSinceStartup - trialStartAbs;
    }

    private void FlushToDiskAtEnd()
    {
        if (hasFlushed) return;
        hasFlushed = true;

        if (buffer == null || buffer.Length == 0)
            return;

        if (string.IsNullOrWhiteSpace(fullFolderPath))
            ResolveFolderPathOnly();

        if (string.IsNullOrWhiteSpace(fullFolderPath))
            return;

        try
        {
            if (!Directory.Exists(fullFolderPath))
                Directory.CreateDirectory(fullFolderPath);

            RefreshFromExperimentController();

            string pid = string.IsNullOrWhiteSpace(participantID) ? "UNKNOWN" : participantID.Trim();
            string blk = string.IsNullOrWhiteSpace(blockLabel) ? "UNKNOWN" : blockLabel.Trim();

            string finalPath = Path.Combine(
                fullFolderPath,
                $"{pid}_{blk}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );

            File.WriteAllText(finalPath, buffer.ToString());

#if UNITY_EDITOR
            Debug.Log("[DataRecorderV2] (Play Mode) Final CSV saved at:\n" + finalPath);
#else
            Debug.Log("[DataRecorderV2] (Build) Final CSV saved at:\n" + finalPath);
#endif
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to write final CSV:\n" + e);
        }
        finally
        {
            buffer = null;
        }
    }

    private string GetHeaderLine()
    {
        return
            "ParticipantID,Block,BlockStartAbs,BlockTimeRel," +
            "TrialIndex,Scene,Event," +
            "Expectancy,SignalColor,MergeSide," +
            "TimeAbs,TrialTimeRel," +
            "X,Y,Z,LaneDeviation,SpeedMPH," +
            "TrialEnded,TrialEndReason,TrialEndAbs,TrialEndRel," +
            "SurveyResponse,SurveyResponseCorrect,SurveyRT," +
            "BrakeInput,ThrottleInput,SteeringInput,Car2Marge\n";
    }

    private void ResolveFolderPathOnly()
    {
#if UNITY_EDITOR
        fullFolderPath = ResolveEditorPath(playModeSaveDirectory);
#else
        fullFolderPath = string.IsNullOrWhiteSpace(buildModeSaveDirectory)
            ? Application.persistentDataPath
            : ResolveBuildPath(buildModeSaveDirectory);
#endif

        try
        {
            if (!Directory.Exists(fullFolderPath))
                Directory.CreateDirectory(fullFolderPath);

            if (verboseLogs)
            {
#if UNITY_EDITOR
                Debug.Log("[DataRecorderV2] (Play Mode) Save folder:\n" + fullFolderPath);
#else
                Debug.Log("[DataRecorderV2] (Build) Save folder:\n" + fullFolderPath);
#endif
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to create/verify save folder:\n" + fullFolderPath + "\n" + e);
            fullFolderPath = null;
        }
    }

    private static string ResolveEditorPath(string pathFromInspector)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.IsPathRooted(pathFromInspector)
            ? pathFromInspector
            : Path.Combine(projectRoot, pathFromInspector);
    }

    private static string ResolveBuildPath(string pathFromInspector)
    {
        return Path.IsPathRooted(pathFromInspector)
            ? pathFromInspector
            : Path.Combine(Application.persistentDataPath, pathFromInspector);
    }

    private GameObject FindInActiveSceneByName(string exactName)
    {
        if (string.IsNullOrWhiteSpace(exactName))
            return null;

        var activeScene = SceneManager.GetActiveScene();
        var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        return allObjects.FirstOrDefault(go =>
            go != null &&
            go.name == exactName &&
            go.scene == activeScene &&
            (go.hideFlags & HideFlags.HideInHierarchy) == 0);
    }

    private void UpdateSpeedMph(Vector3 currentPos)
    {
        if (carRb != null)
        {
            speedMph = carRb.velocity.magnitude * 2.23693629f; // m/s -> mph
            return;
        }

        float now = Time.unscaledTime;
        float dt = now - lastPosTime;
        if (dt > 0.0001f)
        {
            float speedMs = (currentPos - lastPos).magnitude / dt;
            speedMph = speedMs * 2.23693629f;
        }
        lastPos = currentPos;
        lastPosTime = now;
    }

    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool needsQuotes = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
        return needsQuotes ? $"\"{s}\"" : s;
    }

    private static string F(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v) || v < -999999f) return "";
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
