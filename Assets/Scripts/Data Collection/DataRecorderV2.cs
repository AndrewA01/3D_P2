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
    [SerializeField] private float sampleIntervalSeconds = 0.1f;

    [Header("Save Paths")]
    [SerializeField] private string playModeSaveDirectory = "Assets/CSVCollection/NewPL";

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    // ---- Merge RT thresholds (tune if needed) ----
    private const float STEER_RT_THRESHOLD = 2f;     // on -100..100 scale
    private const float BRAKE_RT_THRESHOLD = 2f;     // on 0..100 scale
    private const float THROTTLE_RT_THRESHOLD = 2f;  // on 0..100 scale

    // ---- Lane bounds (world X centerline = 0, markers at +-1.5) ----
    private const float LANE_BOUND_X = 1.5f;
    private bool wasOutOfLane = false;
    private int laneDeviationCount = 0;

    private StringBuilder buffer;
    private bool hasFlushed = false;

    private GameObject car;
    private Rigidbody carRb;
    private Vector3 lastPos;
    private float lastPosTime;
    private float speedMph;

    // ---- Deceleration (m/s^2) state ----
    // Deceleration is stored as positive magnitude when slowing: decel = max(0, -accel)
    private bool hasPrevSpeedSample = false;
    private float prevSpeedMph = 0f;
    private float prevSpeedTimeAbs = 0f;
    private float decelerationMs2 = float.NaN;

    private float nextSampleTime;

    private string participantID = "UNKNOWN";
    private string blockLabel = "UNKNOWN";

    private int currentTrialIndex = -1;
    private float trialStartAbs = -1f;

    private string trialType = "";

    private bool trialEnded = false;
    private string trialEndReason = "";
    private float trialEndAbs = -1f;
    private float trialEndRel = -1f;

    private float surveyStartAbs = -1f;
    private string surveyResponse = "";
    private float surveyRT = -1f;

    // NOTE: This now stores ABSOLUTE deviation (always positive)
    private float laneDeviation = float.NaN;

    private bool surveyButtonsHooked = false;
    private float lastTimeScale = 1f;

    private MoveOnWaypoints car2Mover;

    // ---- Merge RT state per trial ----
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

        if (Time.unscaledTime >= nextSampleTime)
        {
            nextSampleTime += sampleIntervalSeconds;
            SampleAndBufferRow();
        }

        lastTimeScale = Time.timeScale;
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

        // reset per-scene hook flags
        surveyButtonsHooked = false;

        // reset decel state for clean first sample
        ResetDecelState();
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

                // ---- reset lane deviation counting per trial ----
                wasOutOfLane = false;
                laneDeviationCount = 0;

                ResetMergeRTState();

                // ---- reset decel state per trial ----
                ResetDecelState();
            }
        }

        var cond = ExperimentController.Instance.CurrentCondition;
        trialType = cond.isPractice ? "Practice" : "Main";
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

            // ---- lane deviation ABS (world X centerline = 0) + count out-of-bounds entries ----
            laneDeviation = Mathf.Abs(x);              // always positive
            bool outNow = laneDeviation > LANE_BOUND_X;

            // count only when transitioning from in-bounds -> out-of-bounds
            if (!wasOutOfLane && outNow)
                laneDeviationCount++;

            wasOutOfLane = outNow;

            UpdateSpeedMph(p);

            // ---- compute deceleration (m/s^2) from SpeedMPH over TimeAbs ----
            UpdateDecelerationMs2(timeAbs);
        }
        else
        {
            laneDeviation = float.NaN;
            decelerationMs2 = float.NaN;
            hasPrevSpeedSample = false;
        }

        string sceneName = SceneManager.GetActiveScene().name ?? "";

        // Survey correctness based on Scene string
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

        // ===== Car2 Logic + Merge Time as timestamp (ABS) =====
        string car2MergeTimeAbsStr = "";
        int collision01 = 0;

        string car2DecelStartRel = "";
        string car2CollisionAbs = "";
        string timeToCollision = "";

        float mergeStartAbs = -1f;
        float collisionAbs = -1f;
        float decelStartAbs = -1f;

        if (car2Mover != null)
        {
            if (car2Mover.MergeStartAbs > 0f) mergeStartAbs = car2Mover.MergeStartAbs;
            if (car2Mover.CollisionAbs > 0f) collisionAbs = car2Mover.CollisionAbs;
            if (car2Mover.DecelStartAbs > 0f) decelStartAbs = car2Mover.DecelStartAbs;

            // Car2MergeTime = mergeStartAbs (absolute timestamp), like Car2CollisionAbs
            if (mergeStartAbs > 0f) car2MergeTimeAbsStr = F(mergeStartAbs);

            if (collisionAbs > 0f && timeAbs >= collisionAbs) collision01 = 1;

            if (collisionAbs > 0f) car2CollisionAbs = F(collisionAbs);

            // TimeToCollision = collision - mergeStart (can be negative)
            if (mergeStartAbs > 0f && collisionAbs > 0f)
                timeToCollision = F(collisionAbs - mergeStartAbs);

            // DecelStartRel = decelAbs - trialStartAbs
            if (decelStartAbs > 0f && trialStartAbs > 0f)
                car2DecelStartRel = F(decelStartAbs - trialStartAbs);
        }

        // ===== Merge RT columns (from merge start to first input change) =====
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
            Csv(participantID) + "," +
            Csv(blockLabel) + "," +
            currentTrialIndex + "," +
            Csv(sceneName) + "," +
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
            // removed: TrialEnded, TrialEndReason, TrialEndAbs
            F(trialEndRel) + "," +
            Csv(surveyResponse) + "," +
            Csv(surveyCorrect) + "," +
            F(surveyRT) + "," +
            F(brakeInput) + "," +
            F(throttleInput) + "," +
            F(steeringInput) + "," +
            Csv(car2MergeTimeAbsStr) + "," +   // <-- Car2MergeTime (ABS timestamp)
            mergeRTSteerStr + "," +
            mergeRTBrakeStr + "," +
            mergeRTThrottleStr + "," +
            collision01 + "," +
            Csv(car2DecelStartRel) + "," +
            Csv(car2CollisionAbs) + "," +
            Csv(timeToCollision) +
            "\n";

        buffer.Append(row);
    }

    private void UpdateDecelerationMs2(float timeAbs)
    {
        // Need two samples to compute delta
        if (!hasPrevSpeedSample)
        {
            hasPrevSpeedSample = true;
            prevSpeedMph = speedMph;
            prevSpeedTimeAbs = timeAbs;
            decelerationMs2 = float.NaN; // first row has no decel
            return;
        }

        float dt = timeAbs - prevSpeedTimeAbs;
        if (dt <= 0.00001f)
        {
            decelerationMs2 = float.NaN;
            return;
        }

        // Convert mph -> m/s
        float vNow = speedMph * 0.44704f;
        float vPrev = prevSpeedMph * 0.44704f;

        // acceleration (m/s^2)
        float accel = (vNow - vPrev) / dt;

        // deceleration as positive magnitude when slowing down
        decelerationMs2 = Mathf.Max(0f, -accel);

        // update previous
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

    private void FlushToDiskAtEnd()
    {
        if (hasFlushed) return;
        hasFlushed = true;

        if (buffer == null || buffer.Length == 0)
            return;

        string pid = string.IsNullOrWhiteSpace(participantID) ? "UNKNOWN" : participantID.Trim();
        string blk = string.IsNullOrWhiteSpace(blockLabel) ? "UNKNOWN" : blockLabel.Trim();

        string finalPath = Path.Combine(
            playModeSaveDirectory,
            $"{pid}_{blk}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        );

        try
        {
            File.WriteAllText(finalPath, buffer.ToString());
        }
        catch (Exception e)
        {
            Debug.LogError("[DataRecorderV2] Failed to write CSV:\n" + e);
        }
    }

    private string GetHeaderLine()
    {
        return
            "ParticipantID,Block,TrialIndex,Scene,Event," +
            "TimeAbs,TrialTimeRel," +
            "X,Y,Z,LaneDeviationAbs,LaneDeviationCount,SpeedMPH,Deceleration," +
            // removed: TrialEnded,TrialEndReason,TrialEndAbs
            "TrialEndRel," +
            "SurveyResponse,SurveyResponseCorrect,SurveyRT," +
            "BrakeInput,ThrottleInput,SteeringInput,Car2MergeTime," + // <-- renamed
            "MergeRTSteer,MergeRTBrake,MergeRTThrottle," +
            "Collision,Car2DecelStartRel,Car2CollisionAbs,TimeToCollision\n";
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
        try { return Mathf.Clamp(Input.GetAxisRaw("Horizontal") * 100f, -100f, 100f); }
        catch { return 0f; }
    }

    private float GetBrakeScaled0To100()
    {
        try { return Mathf.Clamp(Input.GetAxisRaw("Brake") * 100f, 0f, 100f); }
        catch { return 0f; }
    }

    private float GetThrottleScaled0To100()
    {
        try { return Mathf.Clamp(Input.GetAxisRaw("Throttle") * 100f, 0f, 100f); }
        catch { return 0f; }
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

    private GameObject FindInActiveSceneByName(string name)
    {
        return SceneManager.GetActiveScene()
            .GetRootGameObjects()
            .SelectMany(go => go.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == name)?.gameObject;
    }
}
