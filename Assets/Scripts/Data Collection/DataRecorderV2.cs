using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Collections.Generic;
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
    [SerializeField] private float sampleIntervalSeconds = 0.01f; // CHANGED as requested

    [Header("Save Paths")]
    [SerializeField] private string playModeSaveDirectory = "Assets/CSVCollection/NewPL";

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    // thresholds
    private const float STEER_RT_THRESHOLD = 5f;
    private const float BRAKE_RT_THRESHOLD = 5f;
    private const float THROTTLE_RT_THRESHOLD = 5f;

    // lane bounds
    private const float LANE_BOUND_X = 1.5f;
    private bool wasOutOfLane = false;
    private int laneDeviationCount = 0;

    // buffer
    private StringBuilder buffer;

    // captured naming state (captured at first MAIN trial start)
    private bool mainTrialStartCaptured = false;
    private string capturedParticipantID = "";
    private string capturedBlockLabel = ""; // "Day"/"Night"
    private string activeBlockKey = "";     // capturedParticipantID + "|" + capturedBlockLabel
    private bool blockHasData = false;

    // prevent duplicate writes for same captured block
    private readonly HashSet<string> flushedBlockKeys = new HashSet<string>();

    // runtime references
    private GameObject car;
    private Rigidbody carRb;
    private Vector3 lastPos;
    private float lastPosTime;
    private float speedMph;

    // decel state
    private bool hasPrevSpeedSample = false;
    private float prevSpeedMph = 0f;
    private float prevSpeedTimeAbs = 0f;
    private float decelerationMs2 = float.NaN;

    private float nextSampleTime;

    // trial state
    private int currentTrialIndex = -1;
    private float trialStartAbs = -1f;
    private string trialType = "";
    private bool trialEnded = false;
    private float trialEndRel = -1f;

    // survey
    private float surveyStartAbs = -1f;
    private string surveyResponse = "";
    private float surveyRT = -1f;

    private float laneDeviation = float.NaN;
    private bool surveyButtonsHooked = false;
    private float lastTimeScale = 1f;

    private MoveOnWaypoints car2Mover;

    // merge RT state
    private bool mergeRTInitialized = false;
    private float mergeStartAbsCached = -1f;
    private float mergeBaselineSteer = float.NaN;
    private float mergeBaselineBrake = float.NaN;
    private float mergeBaselineThrottle = float.NaN;
    private float mergeRTSteer = -1f;
    private float mergeRTBrake = -1f;
    private float mergeRTThrottle = -1f;

    // PlayerPrefs key for fallback participant ID (if you want persistence)
    private const string PREF_LAST_PID = "HF_LastParticipantID";

    private void Awake()
    {
        // strong singleton
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
        nextSampleTime = Time.unscaledTime + sampleIntervalSeconds;
        lastTimeScale = Time.timeScale;

        try
        {
            if (!Directory.Exists(playModeSaveDirectory))
                Directory.CreateDirectory(playModeSaveDirectory);
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to create save folder: " + e);
        }
    }

    private void Update()
    {
        RefreshFromExperimentController();
        TryHookSurveyButtons();
        DetectTrialEndTransition();
        AutoFlushWhenBlockEndsByTrialIndex();

        if (Time.unscaledTime >= nextSampleTime)
        {
            nextSampleTime += sampleIntervalSeconds;
            SampleAndBufferRow();
        }

        lastTimeScale = Time.timeScale;
    }

    private void OnApplicationQuit()
    {
        FlushCapturedBlockNow();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        FlushCapturedBlockNow();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        car = FindInActiveSceneByName(playerObjectName);
        carRb = car != null ? car.GetComponent<Rigidbody>() : null;
        car2Mover = FindObjectOfType<MoveOnWaypoints>();

        if (car != null)
        {
            lastPos = car.transform.position;
            lastPosTime = Time.unscaledTime;
            speedMph = 0f;
        }

        surveyButtonsHooked = false;
        ResetDecelState();
    }

    private void RefreshFromExperimentController()
    {
        var ctrl = ExperimentController.Instance;
        if (ctrl == null) return;

        // detect first MAIN trial start and capture block + participant ID at that moment
        // practiceTrialCount is the index where main begins (e.g., 2)
        if (!mainTrialStartCaptured && ctrl.currentTrialIndex == ctrl.practiceTrialCount)
        {
            // capture participantID (prefer controller value; fallback to PlayerPrefs if empty)
            string pid = (ctrl.participantID ?? "").Trim();
            if (string.IsNullOrEmpty(pid))
                pid = PlayerPrefs.GetString(PREF_LAST_PID, "P000").Trim();
            if (string.IsNullOrEmpty(pid)) pid = "P000";

            // capture block label from controller (normalize)
            string blk = NormalizeBlockLabel(ctrl.currentBlock.ToString());

            // only accept Day/Night labels; otherwise leave capture disabled
            if (!string.IsNullOrWhiteSpace(blk) && (blk == "Day" || blk == "Night"))
            {
                capturedParticipantID = pid;
                capturedBlockLabel = blk;
                activeBlockKey = capturedParticipantID + "|" + capturedBlockLabel;
                mainTrialStartCaptured = true;

                if (verboseLogs) Debug.Log($"[DataRecorderV2] Captured block naming: {activeBlockKey}");
            }
            else
            {
                if (verboseLogs) Debug.LogWarning($"[DataRecorderV2] Block label not valid for capture: '{blk}'");
            }
        }

        // update trial index change handling (reset per-trial state when trial begins)
        int idx = ctrl.currentTrialIndex;
        if (idx != currentTrialIndex)
        {
            currentTrialIndex = idx;

            if (ctrl.experimentRunning && currentTrialIndex >= 0)
            {
                trialStartAbs = Time.realtimeSinceStartup;

                trialEnded = false;
                trialEndRel = -1f;

                surveyStartAbs = -1f;
                surveyResponse = "";
                surveyRT = -1f;

                wasOutOfLane = false;
                laneDeviationCount = 0;

                ResetMergeRTState();
                ResetDecelState();
            }
        }

        var cond = ctrl.CurrentCondition;
        trialType = cond.isPractice ? "Practice" : "Main";
    }

    // Auto-flush when the controller completes the last index (e.g., after index 17)
    private void AutoFlushWhenBlockEndsByTrialIndex()
    {
        var ctrl = ExperimentController.Instance;
        if (ctrl == null) return;

        int totalTrials = Mathf.Max(0, ctrl.practiceTrialCount + ctrl.mainTrialCount);
        if (totalTrials <= 0) return;

        int lastIndex = totalTrials - 1;

        // arm flush when we reach last index
        if (ctrl.experimentRunning && ctrl.currentTrialIndex >= lastIndex)
            pendingFlushArmed = true;

        // execute flush after controller finishes and returns to subblock (experimentRunning=false or idx < 0)
        if (pendingFlushArmed && (!ctrl.experimentRunning || ctrl.currentTrialIndex < 0))
        {
            if (verboseLogs) Debug.Log("[DataRecorderV2] Auto-flush triggered by trial index end.");
            FlushCapturedBlockNow();
            pendingFlushArmed = false;

            // prepare for next block run
            mainTrialStartCaptured = false;
            capturedParticipantID = "";
            capturedBlockLabel = "";
            activeBlockKey = "";
        }
    }

    private bool pendingFlushArmed = false;

    /// <summary>
    /// Write the CSV for the captured block (if captured). If nothing was captured,
    /// try to derive a participantID + blockLabel at flush time (fall back to PlayerPrefs).
    /// </summary>
    public void FlushCapturedBlockNow()
    {
        // Determine final pid/block to use
        string pid = capturedParticipantID;
        string blk = capturedBlockLabel;

        // If we didn't capture when main started, attempt to derive at flush time from ExperimentController
        if (string.IsNullOrWhiteSpace(pid) || string.IsNullOrWhiteSpace(blk))
        {
            var ctrl = ExperimentController.Instance;
            if (ctrl != null)
            {
                if (string.IsNullOrWhiteSpace(pid) && !string.IsNullOrWhiteSpace(ctrl.participantID))
                    pid = ctrl.participantID.Trim();

                if (string.IsNullOrWhiteSpace(blk))
                    blk = NormalizeBlockLabel(ctrl.currentBlock.ToString());
            }

            // final fallback to PlayerPrefs or P000
            if (string.IsNullOrWhiteSpace(pid))
                pid = PlayerPrefs.GetString(PREF_LAST_PID, "P000").Trim();
            if (string.IsNullOrWhiteSpace(pid)) pid = "P000";
        }

        // normalize blk
        blk = NormalizeBlockLabel(blk);

        if (string.IsNullOrWhiteSpace(pid) || pid.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (verboseLogs) Debug.LogWarning("[DataRecorderV2] Flush skipped: invalid participant ID.");
            ResetBufferForNextBlock(); // avoid writing junk later
            return;
        }

        if (blk != "Day" && blk != "Night")
        {
            if (verboseLogs) Debug.LogWarning("[DataRecorderV2] Flush skipped: invalid block label.");
            ResetBufferForNextBlock();
            return;
        }

        string key = pid + "|" + blk;
        if (flushedBlockKeys.Contains(key))
        {
            if (verboseLogs) Debug.Log($"[DataRecorderV2] Flush skipped: already flushed {key}");
            ResetBufferForNextBlock();
            return;
        }

        if (!blockHasData || buffer == null || buffer.Length == 0)
        {
            if (verboseLogs) Debug.LogWarning("[DataRecorderV2] Flush skipped: no data for block.");
            ResetBufferForNextBlock();
            flushedBlockKeys.Add(key); // mark as flushed to avoid repeated empty writes
            return;
        }

        // EXACT filename you requested: ParticipantID_Day.csv or ParticipantID_Night.csv
        string finalPath = Path.Combine(playModeSaveDirectory, $"{pid}_{blk}.csv");

        try
        {
            File.WriteAllText(finalPath, buffer.ToString());
            flushedBlockKeys.Add(key);
            if (verboseLogs) Debug.Log($"[DataRecorderV2] Wrote block CSV: {finalPath}");
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to write CSV:\n" + e);
        }

        ResetBufferForNextBlock();
    }

    private void ResetBufferForNextBlock()
    {
        buffer = new StringBuilder(1024 * 64);
        buffer.Append(GetHeaderLine());
        blockHasData = false;

        // reset trial-local things
        surveyResponse = "";
        surveyRT = -1f;
        trialEnded = false;
        trialEndRel = -1f;
        trialStartAbs = -1f;
        currentTrialIndex = -1;

        ResetMergeRTState();
        ResetDecelState();
    }

    private void DetectTrialEndTransition()
    {
        if (lastTimeScale > 0f && Time.timeScale == 0f && !trialEnded)
        {
            trialEnded = true;
            trialEndRel = GetTrialTimeRel();
            surveyStartAbs = Time.realtimeSinceStartup;

            SampleAndBufferRow();
        }
    }

    private void TryHookSurveyButtons()
    {
        if (surveyButtonsHooked) return;

        var overlay = PostTrialOverlay.Instance;
        if (overlay == null || overlay.answerButtons == null) return;

        for (int i = 0; i < overlay.answerButtons.Length; i++)
        {
            int captured = i;
            Button b = overlay.answerButtons[i];
            if (b == null) continue;

            b.onClick.AddListener(() => OnSurveyButtonClicked(overlay, captured));
        }

        surveyButtonsHooked = true;
    }

    private void OnSurveyButtonClicked(PostTrialOverlay overlay, int index)
    {
        Button b = overlay.answerButtons[index];
        var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
        surveyResponse = tmp != null ? tmp.text.Trim() : b.name;

        if (surveyStartAbs > 0f)
            surveyRT = Time.realtimeSinceStartup - surveyStartAbs;

        SampleAndBufferRow();
    }

    private void SampleAndBufferRow()
    {
        float timeAbs = Time.realtimeSinceStartup;
        float trialTimeRel = GetTrialTimeRel();

        float x = float.NaN, y = float.NaN, z = float.NaN;

        if (car != null)
        {
            Vector3 p = car.transform.position;
            x = p.x; y = p.y; z = p.z;

            laneDeviation = Mathf.Abs(x);
            bool outNow = laneDeviation > LANE_BOUND_X;

            if (!wasOutOfLane && outNow)
                laneDeviationCount++;

            wasOutOfLane = outNow;

            UpdateSpeedMph(p);
            UpdateDecelerationMs2(timeAbs);
        }
        else
        {
            laneDeviation = float.NaN;
            decelerationMs2 = float.NaN;
            hasPrevSpeedSample = false;
        }

        string sceneName = SceneManager.GetActiveScene().name ?? "";

        // NEW: scene condition columns (blank for Subblock)
        string expectancy = "";
        string tsColor = "";
        string mergeFrom = "";
        GetSceneConditionFields(sceneName, out expectancy, out tsColor, out mergeFrom);

        string surveyCorrect = "";
        if (!string.IsNullOrWhiteSpace(surveyResponse))
        {
            bool ok = false;
            if (surveyResponse.Equals("Red", StringComparison.OrdinalIgnoreCase))
                ok = sceneName.IndexOf("Red", StringComparison.OrdinalIgnoreCase) >= 0;
            else if (surveyResponse.Equals("Amber", StringComparison.OrdinalIgnoreCase) ||
                     surveyResponse.Equals("Amb", StringComparison.OrdinalIgnoreCase))
                ok = sceneName.IndexOf("Amb", StringComparison.OrdinalIgnoreCase) >= 0;

            surveyCorrect = ok ? "1" : "0";
        }

        float steeringInput = GetSteeringScaledMinus100To100();
        float brakeInput = GetBrakeScaled0To100();
        float throttleInput = GetThrottleScaled0To100();

        // car2 fields
        string car2MergeTimeRelStr = "";   // CHANGED: now rel, but name in CSV stays "Car2MergeTime" per header
        int collision01 = 0;
        string car2DecelStartRel = "";
        string car2CollisionRelStr = "";  // CHANGED: was abs, now rel
        string timeToCollisionRelStr = ""; // CHANGED: based on rel

        float mergeStartAbs = -1f;
        float collisionAbs = -1f;
        float decelStartAbs = -1f;

        if (car2Mover != null)
        {
            if (car2Mover.MergeStartAbs > 0f) mergeStartAbs = car2Mover.MergeStartAbs;
            if (car2Mover.CollisionAbs > 0f) collisionAbs = car2Mover.CollisionAbs;
            if (car2Mover.DecelStartAbs > 0f) decelStartAbs = car2Mover.DecelStartAbs;

            // CHANGED: Car2MergeTime is now trial-relative
            if (mergeStartAbs > 0f && trialStartAbs > 0f)
                car2MergeTimeRelStr = F(mergeStartAbs - trialStartAbs);

            if (collisionAbs > 0f && timeAbs >= collisionAbs) collision01 = 1;

            // CHANGED: Car2CollisionRel is trial-relative
            if (collisionAbs > 0f && trialStartAbs > 0f)
                car2CollisionRelStr = F(collisionAbs - trialStartAbs);

            // CHANGED: TimeToCollision based on trial-relative times
            if (mergeStartAbs > 0f && collisionAbs > 0f && trialStartAbs > 0f)
            {
                float mergeRel = mergeStartAbs - trialStartAbs;
                float collisionRel = collisionAbs - trialStartAbs;
                timeToCollisionRelStr = F(collisionRel - mergeRel);
            }

            if (decelStartAbs > 0f && trialStartAbs > 0f)
                car2DecelStartRel = F(decelStartAbs - trialStartAbs);
        }

        // Merge RT detection (still computed in absolute time, but values are RT durations; unchanged)
        if (mergeStartAbs > 0f && timeAbs >= mergeStartAbs)
        {
            if (!mergeRTInitialized || mergeStartAbsCached != mergeStartAbs)
            {
                mergeRTInitialized = true;
                mergeStartAbsCached = mergeStartAbs;

                mergeBaselineSteer = steeringInput;
                mergeBaselineBrake = brakeInput;
                mergeBaselineThrottle = throttleInput;

                mergeRTSteer = -1f;
                mergeRTBrake = -1f;
                mergeRTThrottle = -1f;
            }

            if (mergeRTSteer < 0f && Mathf.Abs(steeringInput - mergeBaselineSteer) >= STEER_RT_THRESHOLD)
                mergeRTSteer = timeAbs - mergeStartAbsCached;

            if (mergeRTBrake < 0f && Mathf.Abs(brakeInput - mergeBaselineBrake) >= BRAKE_RT_THRESHOLD)
                mergeRTBrake = timeAbs - mergeStartAbsCached;

            if (mergeRTThrottle < 0f && Mathf.Abs(throttleInput - mergeBaselineThrottle) >= THROTTLE_RT_THRESHOLD)
                mergeRTThrottle = timeAbs - mergeStartAbsCached;
        }

        string mergeRTSteerStr = (mergeRTSteer >= 0f) ? F(mergeRTSteer) : "";
        string mergeRTBrakeStr = (mergeRTBrake >= 0f) ? F(mergeRTBrake) : "";
        string mergeRTThrottleStr = (mergeRTThrottle >= 0f) ? F(mergeRTThrottle) : "";

        string row =
            Csv(capturedParticipantID != "" ? capturedParticipantID : PlayerPrefs.GetString(PREF_LAST_PID, "P000")) + "," +
            Csv(capturedBlockLabel != "" ? capturedBlockLabel : "UNKNOWN") + "," +
            currentTrialIndex + "," +
            Csv(sceneName) + "," +
            Csv(expectancy) + "," +        // NEW
            Csv(tsColor) + "," +           // NEW
            Csv(mergeFrom) + "," +         // NEW
            Csv(trialType) + "," +
            F(timeAbs) + "," +
            F(trialTimeRel) + "," +
            F(x) + "," +
            F(y) + "," +
            F(z) + "," +
            F(laneDeviation) + "," +
            laneDeviationCount + "," +
            F(speedMph) + "," +
            F(decelerationMs2) + "," +
            F(trialEndRel) + "," +
            Csv(surveyResponse) + "," +
            Csv(surveyCorrect) + "," +
            F(surveyRT) + "," +
            F(brakeInput) + "," +
            F(throttleInput) + "," +
            F(steeringInput) + "," +
            Csv(car2MergeTimeRelStr) + "," +     // CHANGED to rel
            mergeRTSteerStr + "," +
            mergeRTBrakeStr + "," +
            mergeRTThrottleStr + "," +
            collision01 + "," +
            Csv(car2DecelStartRel) + "," +
            Csv(car2CollisionRelStr) + "," +     // CHANGED + renamed column in header
            Csv(timeToCollisionRelStr) +          // CHANGED to rel-based
            "\n";

        buffer.Append(row);
        blockHasData = true;
    }

    private void UpdateDecelerationMs2(float timeAbs)
    {
        if (!hasPrevSpeedSample)
        {
            hasPrevSpeedSample = true;
            prevSpeedMph = speedMph;
            prevSpeedTimeAbs = timeAbs;
            decelerationMs2 = float.NaN;
            return;
        }

        float dt = timeAbs - prevSpeedTimeAbs;
        if (dt <= 0.00001f)
        {
            decelerationMs2 = float.NaN;
            return;
        }

        float vNow = speedMph * 0.44704f;
        float vPrev = prevSpeedMph * 0.44704f;
        float accel = (vNow - vPrev) / dt;
        decelerationMs2 = Mathf.Max(0f, -accel);

        prevSpeedMph = speedMph;
        prevSpeedTimeAbs = timeAbs;
    }

    private void ResetDecelState()
    {
        hasPrevSpeedSample = false;
        prevSpeedMph = 0f;
        prevSpeedTimeAbs = 0f;
        decelerationMs2 = float.NaN;
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

    private string GetHeaderLine()
    {
        return
            "ParticipantID,Block,TrialIndex,Scene,Expectancy,TSColor,MergeFrom,Event," + // NEW
            "TimeAbs,TrialTimeRel," +
            "X,Y,Z,LaneDeviationAbs,LaneDeviationCount,SpeedMPH,Deceleration," +
            "TrialEndRel," +
            "SurveyResponse,SurveyResponseCorrect,SurveyRT," +
            "BrakeInput,ThrottleInput,SteeringInput,Car2MergeTime," +   // name kept, but values now rel
            "MergeRTSteer,MergeRTBrake,MergeRTThrottle," +
            "Collision,Car2DecelStartRel,Car2CollisionRel,TimeToCollision\n"; // CHANGED names
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
            speedMph = ((currentPos - lastPos).magnitude / dt) * 2.23693629f;

        lastPos = currentPos;
        lastPosTime = now;
    }

    private float GetSteeringScaledMinus100To100()
    {
        try { return Mathf.Clamp(Input.GetAxisRaw("Horizontal") * 100f, -100f, 100f); } catch { return 0f; }
    }

    private float GetBrakeScaled0To100()
    {
        try { return Mathf.Clamp(Input.GetAxisRaw("Brake") * 100f, 0f, 100f); } catch { return 0f; }
    }

    private float GetThrottleScaled0To100()
    {
        try { return Mathf.Clamp(Input.GetAxisRaw("Throttle") * 100f, 0f, 100f); } catch { return 0f; }
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
        if (float.IsNaN(v) || float.IsInfinity(v)) return "";
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string NormalizeBlockLabel(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "UNKNOWN";
        s = s.Trim();
        if (s == "0") return "Day";
        if (s == "1") return "Night";
        if (s.IndexOf("Day", StringComparison.OrdinalIgnoreCase) >= 0) return "Day";
        if (s.IndexOf("Night", StringComparison.OrdinalIgnoreCase) >= 0) return "Night";
        return s;
    }

    private GameObject FindInActiveSceneByName(string name)
    {
        return SceneManager.GetActiveScene()
            .GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == name)?.gameObject;
    }

    // NEW: derives Expectancy / TSColor / MergeFrom from scene name (blank for Subblock)
    private static void GetSceneConditionFields(string sceneName, out string expectancy, out string tsColor, out string mergeFrom)
    {
        expectancy = "";
        tsColor = "";
        mergeFrom = "";

        if (string.IsNullOrWhiteSpace(sceneName)) return;
        if (sceneName.IndexOf("Subblock", StringComparison.OrdinalIgnoreCase) >= 0) return;

        // Expectancy: E -> Expected, U -> U
        if (sceneName.StartsWith("E_", StringComparison.OrdinalIgnoreCase) || sceneName.Equals("E", StringComparison.OrdinalIgnoreCase))
            expectancy = "Expected";
        else if (sceneName.StartsWith("U_", StringComparison.OrdinalIgnoreCase) || sceneName.Equals("U", StringComparison.OrdinalIgnoreCase))
            expectancy = "U";

        // TSColor: Red / Amb
        if (sceneName.IndexOf("_Red_", StringComparison.OrdinalIgnoreCase) >= 0 || sceneName.EndsWith("_Red", StringComparison.OrdinalIgnoreCase))
            tsColor = "Red";
        else if (sceneName.IndexOf("_Amb_", StringComparison.OrdinalIgnoreCase) >= 0 || sceneName.EndsWith("_Amb", StringComparison.OrdinalIgnoreCase))
            tsColor = "Amb";

        // MergeFrom: L -> Left, R -> Right
        if (sceneName.EndsWith("_L", StringComparison.OrdinalIgnoreCase) || sceneName.Equals("L", StringComparison.OrdinalIgnoreCase))
            mergeFrom = "Left";
        else if (sceneName.EndsWith("_R", StringComparison.OrdinalIgnoreCase) || sceneName.Equals("R", StringComparison.OrdinalIgnoreCase))
            mergeFrom = "Right";
    }
}