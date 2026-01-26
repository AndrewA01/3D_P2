using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// PositionRecorder3 (Position Logger 3)
/// - Records per-sample kinematics to CSV
/// - Persists across scenes (DontDestroyOnLoad) so post-trial question can be answered before saving
/// - Adds merge-event timing columns:
///     MergeCueTimeRel
///     DriverResponseTimeRel
///     ReactionTime
///     ResponseType (Brake / Steer / Both / None)
///
/// ReactionTime definition:
///   ReactionTime = DriverResponseTimeRel - MergeCueTimeRel
///
/// How to use for event timing:
/// 1) When adjacent merging vehicle begins lateral movement (and turn signal cue), call:
///      PositionRecorder3.Current.MarkMergeCue();
/// 2) Driver response onset can be detected automatically via input axis thresholds (optional),
///    OR you can call:
///      PositionRecorder3.Current.MarkDriverBrake();
///      PositionRecorder3.Current.MarkDriverSteer();
/// </summary>
public class PositionRecorder3 : MonoBehaviour
{
    public static PositionRecorder3 Current { get; private set; }

    [Header("Target (Ego Vehicle)")]
    public GameObject target;
    public float recordInterval = 0.1f;

    [Header("Lane Center (optional)")]
    public Transform laneCenter;

    [Header("Output Folder (relative to project)")]
    public string relativeFolderPath = "Assets/CSVCollection/NewPL";

    [Header("Optional: Auto-detect driver response from Input axes")]
    [Tooltip("If enabled, logger will mark the first driver response using input axis thresholds.")]
    public bool autoDetectResponseFromInput = false;

    [Tooltip("Input axis name for steering (e.g., Horizontal). Leave blank to disable steering auto-detect.")]
    public string steeringAxisName = "Horizontal";

    [Tooltip("Input axis name for braking (e.g., Brake). Leave blank to disable brake auto-detect.")]
    public string brakeAxisName = "Brake";

    [Tooltip("Absolute steering axis must exceed this to count as response.")]
    public float steeringThreshold = 0.15f;

    [Tooltip("Brake axis must exceed this to count as response.")]
    public float brakeThreshold = 0.10f;

    private Rigidbody targetRigidbody;
    private float timer;
    private float trialStartTime;

    private bool recordingEnabled = true;
    private bool saved = false;

    // ===== Trial metadata (from ExperimentController) =====
    private string participantID = "NA";
    private string blockLabel = "NA";
    private int trialIndex = -1;

    private string trialType = "Unknown";
    private string expectancyLabel = "Unknown";
    private string signalColorLabel = "Unknown";
    private string mergeSideLabel = "Unknown";

    // ===== Trial-end fields (optional but useful) =====
    private bool trialEnded = false;
    private string trialEndReason = "";
    private float trialEndTimeAbs = -1f;
    private float trialEndTimeRel = -1f;

    // ===== Survey fields (set in post-trial question scene) =====
    private string surveyQuestion = "";
    private string surveyResponse = "";
    private float surveyRT = -1f;

    // ===== Merge cue + driver response timing =====
    private bool mergeCueMarked = false;
    private float mergeCueTimeRel = -1f;

    private bool responseMarked = false;
    private float driverResponseTimeRel = -1f;

    // These track the earliest input type if both happen nearly together
    private bool brakeMarked = false;
    private bool steerMarked = false;

    private string responseType = "None"; // Brake / Steer / Both / None
    private float reactionTime = -1f;     // DriverResponseTimeRel - MergeCueTimeRel (if both exist)

    private struct Sample
    {
        public float timeAbs;
        public float timeRel;
        public Vector3 pos;
        public float laneDev;
        public float speedMPH;
    }

    private readonly List<Sample> samples = new();

    private const string CsvHeader =
        "ParticipantID,Block,TrialIndex,TrialType,Expectancy,SignalColor,MergeSide," +
        "TimeAbsolute,TimeRelative,X,Y,Z,LaneDeviation,SpeedMPH," +
        "TrialEnded,TrialEndReason,TrialEndTimeAbs,TrialEndTimeRel," +
        "MergeCueTimeRel,DriverResponseTimeRel,ReactionTime,ResponseType," +
        "SurveyQuestion,SurveyResponse,SurveyRT";

    private void Awake()
    {
        if (Current != null && Current != this)
        {
            Destroy(gameObject);
            return;
        }

        Current = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        trialStartTime = Time.time;

        // Pull metadata from ExperimentController (if present)
        if (ExperimentController.Instance != null && ExperimentController.Instance.experimentRunning)
        {
            participantID = ExperimentController.Instance.participantID;
            blockLabel = ExperimentController.Instance.currentBlock.ToString();
            trialIndex = ExperimentController.Instance.currentTrialIndex;

            var cond = ExperimentController.Instance.CurrentCondition;
            trialType = cond.isPractice ? "Practice" : "Main";
            expectancyLabel = cond.expectancy.ToString();
            signalColorLabel = cond.signalColor.ToString();
            mergeSideLabel = cond.mergeSide.ToString();
        }

        if (!Directory.Exists(relativeFolderPath))
            Directory.CreateDirectory(relativeFolderPath);

        if (target != null)
            targetRigidbody = target.GetComponent<Rigidbody>();
    }

    private void Update()
    {
        // Optional: detect first driver response from input automatically
        if (autoDetectResponseFromInput && mergeCueMarked && !responseMarked)
        {
            TryAutoDetectDriverResponse();
        }

        if (!recordingEnabled) return;
        if (target == null) return;

        timer += Time.deltaTime;
        if (timer < recordInterval) return;
        timer = 0f;

        Vector3 pos = target.transform.position;

        float speedMPH = (targetRigidbody != null)
            ? targetRigidbody.velocity.magnitude * 2.23694f
            : 0f;

        float timeAbs = Time.time;
        float timeRel = Time.time - trialStartTime;

        float laneDev = 0f;
        if (laneCenter != null)
            laneDev = laneCenter.InverseTransformPoint(pos).x;

        samples.Add(new Sample
        {
            timeAbs = timeAbs,
            timeRel = timeRel,
            pos = pos,
            laneDev = laneDev,
            speedMPH = speedMPH
        });
    }

    // ============================
    //   Merge cue + response API
    // ============================

    /// <summary>
    /// Call this at the moment the adjacent merging vehicle begins lateral movement
    /// (and turn signal cue occurs).
    /// </summary>
    public void MarkMergeCue()
    {
        if (mergeCueMarked) return;

        mergeCueMarked = true;
        mergeCueTimeRel = Time.time - trialStartTime;

        // If a response was marked earlier (rare), compute RT now
        RecomputeReactionTimeIfPossible();
    }

    /// <summary>
    /// Call this at the FIRST moment the driver brakes (response onset).
    /// If steering also occurs at the same time, ResponseType will become "Both".
    /// </summary>
    public void MarkDriverBrake()
    {
        if (brakeMarked) return;

        brakeMarked = true;
        MarkDriverResponseInternal();
        UpdateResponseType();
    }

    /// <summary>
    /// Call this at the FIRST moment the driver steers (response onset).
    /// If braking also occurs at the same time, ResponseType will become "Both".
    /// </summary>
    public void MarkDriverSteer()
    {
        if (steerMarked) return;

        steerMarked = true;
        MarkDriverResponseInternal();
        UpdateResponseType();
    }

    private void MarkDriverResponseInternal()
    {
        if (responseMarked) return;

        responseMarked = true;
        driverResponseTimeRel = Time.time - trialStartTime;

        RecomputeReactionTimeIfPossible();
    }

    private void UpdateResponseType()
    {
        if (brakeMarked && steerMarked) responseType = "Both";
        else if (brakeMarked) responseType = "Brake";
        else if (steerMarked) responseType = "Steer";
        else responseType = "None";
    }

    private void RecomputeReactionTimeIfPossible()
    {
        if (mergeCueMarked && responseMarked)
        {
            reactionTime = driverResponseTimeRel - mergeCueTimeRel;
        }
        else
        {
            reactionTime = -1f;
        }
    }

    private void TryAutoDetectDriverResponse()
    {
        bool brakeTriggered = false;
        bool steerTriggered = false;

        // Steering
        if (!string.IsNullOrWhiteSpace(steeringAxisName))
        {
            float steer = 0f;
            try { steer = Input.GetAxis(steeringAxisName); }
            catch { /* axis may not exist */ }

            if (Mathf.Abs(steer) >= steeringThreshold)
                steerTriggered = true;
        }

        // Brake
        if (!string.IsNullOrWhiteSpace(brakeAxisName))
        {
            float brake = 0f;
            try { brake = Input.GetAxis(brakeAxisName); }
            catch { /* axis may not exist */ }

            if (brake >= brakeThreshold)
                brakeTriggered = true;
        }

        // Mark whichever happened this frame (could be both)
        if (steerTriggered) MarkDriverSteer();
        if (brakeTriggered) MarkDriverBrake();
    }

    // ============================
    //   Trial end + survey API
    // ============================

    /// <summary>
    /// Call BEFORE loading PostTrialQuestion.
    /// Stops sampling and stamps trial end fields.
    /// </summary>
    public void StopRecordingForQuestion(string endReason)
    {
        if (trialEnded) return;

        recordingEnabled = false;

        trialEnded = true;
        trialEndReason = endReason ?? "";
        trialEndTimeAbs = Time.time;
        trialEndTimeRel = Time.time - trialStartTime;

        // If no response was detected, keep ResponseType "None" and reactionTime -1
        // (you can still mark response later if you want to allow response in question scene, but typically not)
    }

    /// <summary>
    /// Called by post-trial question scene after participant clicks response button.
    /// </summary>
    public void SetSurveyResult(string question, string response, float rtSeconds)
    {
        surveyQuestion = question ?? "";
        surveyResponse = response ?? "";
        surveyRT = rtSeconds;
    }

    /// <summary>
    /// Called by post-trial question scene to save after response is collected.
    /// </summary>
    public void SaveNowAndCleanup()
    {
        if (saved) return;
        saved = true;

        SaveToCSV();

        if (Current == this) Current = null;
        Destroy(gameObject);
    }

    // ============================
    //   CSV writing
    // ============================

    private void SaveToCSV()
    {
        string fileName = $"P{participantID}_{blockLabel}.csv";
        string filePath = Path.Combine(relativeFolderPath, fileName);

        try
        {
            bool exists = File.Exists(filePath);

            using (StreamWriter sw = new StreamWriter(filePath, append: true))
            {
                if (!exists)
                    sw.WriteLine(CsvHeader);

                int endedInt = trialEnded ? 1 : 0;

                string endR = CsvEscape(trialEndReason);
                string q = CsvEscape(surveyQuestion);
                string r = CsvEscape(surveyResponse);

                // Make sure responseType reflects any marks that happened
                UpdateResponseType();
                RecomputeReactionTimeIfPossible();

                foreach (var s in samples)
                {
                    string row =
                        $"{participantID}," +
                        $"{blockLabel}," +
                        $"{trialIndex}," +
                        $"{trialType}," +
                        $"{expectancyLabel}," +
                        $"{signalColorLabel}," +
                        $"{mergeSideLabel}," +
                        $"{s.timeAbs:F2}," +
                        $"{s.timeRel:F2}," +
                        $"{s.pos.x:F4},{s.pos.y:F4},{s.pos.z:F4}," +
                        $"{s.laneDev:F4}," +
                        $"{s.speedMPH:F2}," +
                        $"{endedInt}," +
                        $"{endR}," +
                        $"{trialEndTimeAbs:F2}," +
                        $"{trialEndTimeRel:F2}," +
                        $"{mergeCueTimeRel:F3}," +
                        $"{driverResponseTimeRel:F3}," +
                        $"{reactionTime:F3}," +
                        $"{CsvEscape(responseType)}," +
                        $"{q}," +
                        $"{r}," +
                        $"{surveyRT:F3}";

                    sw.WriteLine(row);
                }
            }

            Debug.Log($"PositionRecorder3: Appended {samples.Count} rows → {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"PositionRecorder3: Failed to save file: {e.Message}");
        }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool mustQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
        return mustQuote ? $"\"{s}\"" : s;
    }
}
