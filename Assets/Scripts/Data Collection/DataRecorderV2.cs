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

    [Tooltip("Build Mode save folder (legacy). Ignored because builds write to: <BuildFolder>/<ProductName> Data/CSVData")]
    [SerializeField] private string buildModeSaveDirectory = "";

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = true;

    [Header("Merge RT Thresholds")]
    [Tooltip("Minimum absolute change in steering input (-100..100) to count as a response after Car2Merge.")]
    [SerializeField] private float mergeRTSteerThreshold = 2f;

    [Tooltip("Minimum absolute change in brake input (0..100) to count as a response after Car2Merge.")]
    [SerializeField] private float mergeRTBrakeThreshold = 2f;

    [Tooltip("Minimum absolute change in throttle input (0..100) to count as a response after Car2Merge.")]
    [SerializeField] private float mergeRTThrottleThreshold = 2f;

    // Buffer (write at end / end-of-block)
    private StringBuilder buffer;

    // Save folder
    private string fullFolderPath;

    // Sampling timer (unscaled)
    private float nextSampleTime;

    // Target + speed
    private GameObject car;
    private Rigidbody carRb;
    private Vector3 lastPos;
    private float lastPosTime;
    private float speedMph;

    // Experiment/block/trial state
    private string participantID = "UNKNOWN";
    private string blockLabel = "UNKNOWN";

    // For block splitting
    private bool lastExperimentRunning = false;
    private string lastKnownParticipantID = "UNKNOWN";
    private string lastKnownBlockLabel = "UNKNOWN";

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

    // Car 2 tracking
    private MoveOnWaypoints car2Mover;

    // ===== Merge RT state (per trial) =====
    private bool mergeRTInitialized = false;
    private float mergeStartAbsCached = -1f;

    private float mergeBaselineSteer = float.NaN;
    private float mergeBaselineBrake = float.NaN;
    private float mergeBaselineThrottle = float.NaN;

    private float mergeRTSteer = -1f;
    private float mergeRTBrake = -1f;
    private float mergeRTThrottle = -1f;

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
        FlushToDiskIfHasData(lastKnownParticipantID, lastKnownBlockLabel);
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        FlushToDiskIfHasData(lastKnownParticipantID, lastKnownBlockLabel);
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
        // End-of-block detection even if controller gets destroyed
        if (ExperimentController.Instance == null)
        {
            if (lastExperimentRunning)
            {
                if (verboseLogs)
                    Debug.Log("[DataRecorderV2] ExperimentController vanished after running; treating as block end -> flushing CSV.");

                FlushToDiskIfHasData(lastKnownParticipantID, lastKnownBlockLabel);
                ResetBufferForNextBlock();
            }

            lastExperimentRunning = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(ExperimentController.Instance.participantID))
            participantID = ExperimentController.Instance.participantID.Trim();

        blockLabel = ExperimentController.Instance.currentBlock.ToString();

        lastKnownParticipantID = string.IsNullOrWhiteSpace(participantID) ? "UNKNOWN" : participantID;
        lastKnownBlockLabel = string.IsNullOrWhiteSpace(blockLabel) ? "UNKNOWN" : blockLabel;

        bool runningNow = ExperimentController.Instance.experimentRunning;

        // End-of-block transition: true -> false
        if (lastExperimentRunning && !runningNow)
        {
            if (verboseLogs)
                Debug.Log("[DataRecorderV2] Detected end-of-block (experimentRunning true -> false); flushing CSV and starting new buffer.");

            FlushToDiskIfHasData(lastKnownParticipantID, lastKnownBlockLabel);
            ResetBufferForNextBlock();
        }

        lastExperimentRunning = runningNow;

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

                // Reset merge RT state each trial
                ResetMergeRTState();
            }
        }

        var cond = ExperimentController.Instance.CurrentCondition;

        trialType = cond.isPractice ? "Practice" : "Main";

        // Robust decode for combined codes like "E_Red_L" if they ever appear
        string rawExp = cond.expectancy != null ? cond.expectancy.ToString() : "";
        string rawColor = cond.signalColor != null ? cond.signalColor.ToString() : "";
        string rawSide = cond.mergeSide != null ? cond.mergeSide.ToString() : "";

        if (TryParseCombinedCondition(rawExp, out string pExp, out string pColor, out string pSide) ||
            TryParseCombinedCondition(rawColor, out pExp, out pColor, out pSide) ||
            TryParseCombinedCondition(rawSide, out pExp, out pColor, out pSide))
        {
            expectancy = pExp;
            signalColor = pColor;
            mergeSide = pSide;
        }
        else
        {
            expectancy = NormalizeExpectancy(rawExp);
            signalColor = NormalizeColor(rawColor);
            mergeSide = NormalizeSide(rawSide);
        }
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

        // Inputs (scaled)
        float steeringInput = GetSteeringScaledMinus100To100(); // -100..100
        float brakeInput = GetBrakeScaled0To100();              // 0..100
        float throttleInput = GetThrottleScaled0To100();        // 0..100

        // SurveyResponseCorrect (1 if SurveyResponse matches SignalColor)
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

        // --- Car2 export fields ---
        float car2MergeStartAbs = -1f;
        float car2DecelStartAbsVal = -1f;
        float car2CollisionAbsVal = -1f;

        if (!isSubBlock && car2Mover != null)
        {
            if (car2Mover.MergeStartAbs > 0f) car2MergeStartAbs = car2Mover.MergeStartAbs;
            if (car2Mover.DecelStartAbs > 0f) car2DecelStartAbsVal = car2Mover.DecelStartAbs;
            if (car2Mover.CollisionAbs > 0f) car2CollisionAbsVal = car2Mover.CollisionAbs;
        }

        // Car2Merge: 1 for all rows starting at lateral merge start
        int car2Merge = (!isSubBlock && car2MergeStartAbs > 0f && timeAbs >= car2MergeStartAbs) ? 1 : 0;

        // Collision: 1 for all rows starting at collision time (can happen before merge)
        int collision01 = (!isSubBlock && car2CollisionAbsVal > 0f && timeAbs >= car2CollisionAbsVal) ? 1 : 0;

        // Keep Car2DecelStartAbs
        string car2DecelStartAbs = (car2DecelStartAbsVal > 0f) ? F(car2DecelStartAbsVal) : "";

        // Collision time
        string car2CollisionAbs = (car2CollisionAbsVal > 0f) ? F(car2CollisionAbsVal) : "";

        // TimeToCollision: collision - mergeStart (only if collision occurs after merge)
        string timeToCollision = "";
        if (car2MergeStartAbs > 0f && car2CollisionAbsVal > 0f && car2CollisionAbsVal >= car2MergeStartAbs)
        {
            timeToCollision = F(car2CollisionAbsVal - car2MergeStartAbs);
        }

        // ===== Merge RT calculations =====
        // Initialize baseline exactly when we first reach/see the merge start time.
        if (!isSubBlock && car2MergeStartAbs > 0f && timeAbs >= car2MergeStartAbs)
        {
            if (!mergeRTInitialized || mergeStartAbsCached != car2MergeStartAbs)
            {
                mergeRTInitialized = true;
                mergeStartAbsCached = car2MergeStartAbs;

                mergeBaselineSteer = steeringInput;
                mergeBaselineBrake = brakeInput;
                mergeBaselineThrottle = throttleInput;

                // Only reset RTs when a new merge start is detected
                mergeRTSteer = -1f;
                mergeRTBrake = -1f;
                mergeRTThrottle = -1f;
            }

            // Once initialized, look for first meaningful changes
            if (mergeRTSteer < 0f && Mathf.Abs(steeringInput - mergeBaselineSteer) >= Mathf.Max(0f, mergeRTSteerThreshold))
                mergeRTSteer = timeAbs - mergeStartAbsCached;

            if (mergeRTBrake < 0f && Mathf.Abs(brakeInput - mergeBaselineBrake) >= Mathf.Max(0f, mergeRTBrakeThreshold))
                mergeRTBrake = timeAbs - mergeStartAbsCached;

            if (mergeRTThrottle < 0f && Mathf.Abs(throttleInput - mergeBaselineThrottle) >= Mathf.Max(0f, mergeRTThrottleThreshold))
                mergeRTThrottle = timeAbs - mergeStartAbsCached;
        }

        string mergeRTSteerStr = (mergeRTSteer >= 0f) ? F(mergeRTSteer) : "";
        string mergeRTBrakeStr = (mergeRTBrake >= 0f) ? F(mergeRTBrake) : "";
        string mergeRTThrottleStr = (mergeRTThrottle >= 0f) ? F(mergeRTThrottle) : "";

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
            car2Merge.ToString() + "," +
            Csv(mergeRTSteerStr) + "," +
            Csv(mergeRTBrakeStr) + "," +
            Csv(mergeRTThrottleStr) + "," +
            collision01.ToString() + "," +
            Csv(car2DecelStartAbs) + "," +
            Csv(car2CollisionAbs) + "," +
            Csv(timeToCollision) +
            "\n";

        buffer.Append(row);
    }

    private float GetSteeringScaledMinus100To100()
    {
        float axis = TryGetAxisRawSafe("Horizontal");
        if (!float.IsNaN(axis))
            return Mathf.Clamp(axis * 100f, -100f, 100f);

        float kb = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kb -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kb += 1f;

        if (Mathf.Abs(kb) > 0f) return kb * 100f;
        return 0f;
    }

    private float GetBrakeScaled0To100()
    {
        float brake01 = TryGetAxis01Safe("Brake");
        if (!float.IsNaN(brake01))
            return Mathf.Clamp(brake01 * 100f, 0f, 100f);

        return Input.GetKey(KeyCode.Space) ? 100f : 0f;
    }

    private float GetThrottleScaled0To100()
    {
        float thr01 = TryGetAxis01Safe("Throttle");
        if (!float.IsNaN(thr01))
            return Mathf.Clamp(thr01 * 100f, 0f, 100f);

        return Input.GetKey(KeyCode.W) ? 100f : 0f;
    }

    private float TryGetAxis01Safe(string axisName)
    {
        try
        {
            float raw = Input.GetAxisRaw(axisName);
            float v01 = Mathf.Clamp01((raw + 1f) * 0.5f);
            return v01;
        }
        catch
        {
            return float.NaN;
        }
    }

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

    private void ResetMergeRTState()
    {
        mergeRTInitialized = false;
        mergeStartAbsCached = -1f;

        mergeBaselineSteer = float.NaN;
        mergeBaselineBrake = float.NaN;
        mergeBaselineThrottle = float.NaN;

        mergeRTSteer = -1f;
        mergeRTBrake = -1f;
        mergeRTThrottle = -1f;
    }

    private void ResetBufferForNextBlock()
    {
        buffer = new StringBuilder(1024 * 64);
        buffer.Append(GetHeaderLine());

        currentTrialIndex = -1;
        trialStartAbs = -1f;

        trialEnded = false;
        trialEndReason = "";
        trialEndAbs = -1f;
        trialEndRel = -1f;

        surveyStartAbs = -1f;
        surveyResponse = "";
        surveyRT = -1f;

        ResetMergeRTState();
    }

    private void FlushToDiskIfHasData(string pidForName, string blockForName)
    {
        if (buffer == null) return;
        string header = GetHeaderLine();
        if (buffer.Length <= header.Length + 2) return;

        if (string.IsNullOrWhiteSpace(fullFolderPath))
            ResolveFolderPathOnly();

        if (string.IsNullOrWhiteSpace(fullFolderPath))
            return;

        try
        {
            if (!Directory.Exists(fullFolderPath))
                Directory.CreateDirectory(fullFolderPath);

            string pid = string.IsNullOrWhiteSpace(pidForName) ? "UNKNOWN" : pidForName.Trim();
            string blk = string.IsNullOrWhiteSpace(blockForName) ? "UNKNOWN" : blockForName.Trim();

            string finalPath = Path.Combine(
                fullFolderPath,
                $"{pid}_{blk}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            );

            File.WriteAllText(finalPath, buffer.ToString());

#if UNITY_EDITOR
            Debug.Log("[DataRecorderV2] Saved CSV:\n" + finalPath);
#else
            Debug.Log("[DataRecorderV2] Saved CSV:\n" + finalPath);
#endif
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to write CSV:\n" + e);
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
            "BrakeInput,ThrottleInput,SteeringInput,Car2Merge," +
            "MergeRTSteer,MergeRTBrake,MergeRTThrottle," +
            "Collision,Car2DecelStartAbs,Car2CollisionAbs,TimeToCollision\n";
    }

    private void ResolveFolderPathOnly()
    {
#if UNITY_EDITOR
        fullFolderPath = ResolveEditorPath(playModeSaveDirectory);
#else
        string buildRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(buildRoot))
            buildRoot = Application.persistentDataPath;

        string dataFolderName = $"{Application.productName} Data";
        string dataFolderPath = Path.Combine(buildRoot, dataFolderName);
        fullFolderPath = Path.Combine(dataFolderPath, "CSVData");
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
            speedMph = carRb.velocity.magnitude * 2.23693629f;
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

    // =========================================================
    // Combined token decoding (safety net)
    // =========================================================

    private static bool TryParseCombinedCondition(string input, out string exp, out string color, out string side)
    {
        exp = "";
        color = "";
        side = "";

        if (string.IsNullOrWhiteSpace(input))
            return false;

        string s = input.Trim();

        if (!s.Contains("_") && !s.Contains("-"))
            return false;

        char[] seps = new char[] { '_', '-' };
        string[] parts = s.Split(seps, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        string p0 = parts[0].Trim();
        string p1 = parts[1].Trim();
        string p2 = parts[2].Trim();

        string nExp = NormalizeExpectancy(p0);
        string nColor = NormalizeColor(p1);
        string nSide = NormalizeSide(p2);

        if (string.IsNullOrWhiteSpace(nExp) ||
            string.IsNullOrWhiteSpace(nColor) ||
            string.IsNullOrWhiteSpace(nSide))
            return false;

        exp = nExp;
        color = nColor;
        side = nSide;
        return true;
    }

    private static string NormalizeExpectancy(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim().ToLowerInvariant();

        if (s == "e" || s == "exp" || s == "expected" || s.Contains("expected"))
            return "Expected";

        if (s == "u" || s == "unexp" || s == "unexpected" || s.Contains("unexpected"))
            return "Unexpected";

        return raw.Trim();
    }

    private static string NormalizeColor(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim().ToLowerInvariant();

        if (s == "red" || s.Contains("red")) return "Red";
        if (s == "amber" || s == "amb" || s.Contains("amber")) return "Amber";
        if (s == "yellow" || s.Contains("yellow")) return "Yellow";
        if (s == "green" || s.Contains("green")) return "Green";

        return raw.Trim();
    }

    private static string NormalizeSide(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim().ToLowerInvariant();

        if (s == "l" || s == "left" || s.Contains("left")) return "Left";
        if (s == "r" || s == "right" || s.Contains("right")) return "Right";

        return raw.Trim();
    }
}
