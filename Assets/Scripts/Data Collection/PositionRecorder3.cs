using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PositionRecorder3 : MonoBehaviour
{
    public static PositionRecorder3 Current { get; private set; }

    [Header("Target (Ego Vehicle)")]
    [Tooltip("If empty, auto-finds GameObject named ExperimentController.Instance.playerObjectName each trial.")]
    public GameObject target;

    [Tooltip("Seconds between samples.")]
    [Min(0.01f)]
    public float recordInterval = 0.1f;

    [Header("Lane Center (optional)")]
    public Transform laneCenter;

    [Header("Folders")]
    [Tooltip("Runtime writes to persistentDataPath/<relativeFolderPath>. Editor copies final file into Assets/<relativeFolderPath> at block end.")]
    public string relativeFolderPath = "CSVCollection/NewPL";

    [Header("Persist")]
    [Tooltip("Recommended ON so the logger survives trial -> PTQ -> trial scene loads.")]
    public bool detachAndPersist = true;

    [Header("Debug / Trial End Controls")]
    public bool enableDebugHotkey = true;
    public KeyCode debugEndKey = KeyCode.P;

    [Tooltip("If enabled, trial ends automatically after trialTimeoutSeconds.")]
    public bool enableTrialTimeout = false;

    [Min(1f)]
    public float trialTimeoutSeconds = 60f;

    [Header("Optional: show save path on screen")]
    public bool showSavePathOnScreen = false;

    private Rigidbody targetRigidbody;

    private float sampleTimer;
    private float blockStartTimeAbs;
    private float trialStartTimeAbs;

    private bool blockInitialized;
    private bool blockEnded;
    private bool samplingEnabled;
    private bool sawController;
    private int lastTrialIndex = int.MinValue;

    private string participantID = "P000";
    private string blockLabel = "NA";

    private string trialType = "Unknown";
    private string expectancyLabel = "Unknown";
    private string signalColorLabel = "Unknown";
    private string mergeSideLabel = "Unknown";

    private bool trialEnded;
    private string trialEndReason = "";
    private float trialEndTimeAbs = -1f;
    private float trialEndTimeRel = -1f;

    private string surveyQuestion = "";
    private string surveyResponse = "";
    private float surveyRT = -1f;

    private bool mergeCueMarked;
    private float mergeCueTimeRel = -1f;

    private bool responseMarked;
    private float driverResponseTimeRel = -1f;

    private bool brakeMarked;
    private bool steerMarked;

    private string responseType = "None";
    private float reactionTime = -1f;

    private StreamWriter writer;
    private string runtimeFolder;
    private string runtimeFilePath;

#if UNITY_EDITOR
    private string assetsFolder;
    private string assetsCopyPath;
#endif

    public string RuntimeFolderPath => runtimeFolder;
    public string RuntimeFilePath => runtimeFilePath;

    private const string Header =
        "ParticipantID,Block,BlockStartTimeAbs,BlockTimeRel," +
        "TrialIndex,TrialType,Expectancy,SignalColor,MergeSide," +
        "SceneName,Event," +
        "TimeAbs,TrialTimeRel," +
        "X,Y,Z,LaneDeviation,SpeedMPH," +
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

        if (detachAndPersist)
        {
            transform.SetParent(null, true);
            DontDestroyOnLoad(gameObject);
            gameObject.name = "PositionRecorder3_BlockLogger";
        }
    }

    private void Update()
    {
        UpdateBlockAndTrialState();

        if (samplingEnabled && enableDebugHotkey && Input.GetKeyDown(debugEndKey))
        {
            EndTrialAndGoToPTQ("KeyPress_" + debugEndKey);
            return;
        }

        if (samplingEnabled && enableTrialTimeout)
        {
            float trialElapsed = Time.time - trialStartTimeAbs;
            if (trialStartTimeAbs > 0f && trialElapsed >= trialTimeoutSeconds)
            {
                EndTrialAndGoToPTQ("Timeout_" + trialTimeoutSeconds.ToString("F0") + "s");
                return;
            }
        }

        if (!samplingEnabled) return;

        EnsureTarget();
        if (target == null) return;

        sampleTimer += Time.deltaTime;
        if (sampleTimer < recordInterval) return;
        sampleTimer = 0f;

        float timeAbs = Time.time;
        float blockTimeRel = timeAbs - blockStartTimeAbs;
        float trialTimeRel = timeAbs - trialStartTimeAbs;

        Vector3 pos = target.transform.position;

        float speedMPH = 0f;
        if (targetRigidbody != null)
            speedMPH = targetRigidbody.velocity.magnitude * 2.23694f;

        float laneDev = 0f;
        if (laneCenter != null)
            laneDev = laneCenter.InverseTransformPoint(pos).x;

        WriteRow("SAMPLE", timeAbs, blockTimeRel, trialTimeRel, pos, laneDev, speedMPH);
    }

    public void EndTrialAndGoToPTQ(string reason)
    {
        if (!blockInitialized) return;
        if (!samplingEnabled) return;
        if (trialEnded) return;

        StopRecordingForQuestion(reason);

        var ec = ExperimentController.Instance;
        if (ec != null) ec.GoToPostTrialQuestion();
        else Debug.LogWarning("[PositionRecorder3] ExperimentController.Instance not found; cannot load PTQ.");
    }

    public void StopRecordingForQuestion(string endReason)
    {
        if (!blockInitialized) return;

        samplingEnabled = false;

        trialEnded = true;
        trialEndReason = endReason ?? "";
        trialEndTimeAbs = Time.time;
        trialEndTimeRel = trialEndTimeAbs - trialStartTimeAbs;

        WriteMarker("TRIAL_END");
        Flush();
    }

    public void SetSurveyResult(string question, string response, float rtSeconds)
    {
        if (!blockInitialized) return;

        surveyQuestion = question ?? "";
        surveyResponse = response ?? "";
        surveyRT = rtSeconds;

        WriteMarker("SURVEY");
        Flush();
    }

    public void SaveNowAndCleanup() => Flush();
    public void SaveNow() => Flush();

    public void MarkMergeCue()
    {
        if (!blockInitialized || mergeCueMarked) return;
        mergeCueMarked = true;
        mergeCueTimeRel = Time.time - trialStartTimeAbs;
        RecomputeRT();
    }

    public void MarkDriverBrake()
    {
        if (!blockInitialized || brakeMarked) return;
        brakeMarked = true;
        MarkResponseInternal();
        UpdateResponseType();
    }

    public void MarkDriverSteer()
    {
        if (!blockInitialized || steerMarked) return;
        steerMarked = true;
        MarkResponseInternal();
        UpdateResponseType();
    }

    private void MarkResponseInternal()
    {
        if (responseMarked) return;
        responseMarked = true;
        driverResponseTimeRel = Time.time - trialStartTimeAbs;
        RecomputeRT();
    }

    private void UpdateResponseType()
    {
        if (brakeMarked && steerMarked) responseType = "Both";
        else if (brakeMarked) responseType = "Brake";
        else if (steerMarked) responseType = "Steer";
        else responseType = "None";
    }

    private void RecomputeRT()
    {
        reactionTime = (mergeCueMarked && responseMarked) ? (driverResponseTimeRel - mergeCueTimeRel) : -1f;
    }

    private void UpdateBlockAndTrialState()
    {
        var ec = ExperimentController.Instance;
        if (ec != null) sawController = true;

        if (!blockInitialized && ec != null && ec.experimentRunning)
        {
            StartBlock(ec);
        }

        if (!blockInitialized) return;

        if (ec != null && ec.experimentRunning)
        {
            int t = ec.currentTrialIndex;
            if (t != lastTrialIndex)
                StartTrial(ec, t);
        }

        if (!blockEnded)
        {
            bool controllerGone = (sawController && ec == null);
            bool controllerStopped = (ec != null && !ec.experimentRunning && ec.currentTrialIndex == -1);

            if (controllerGone || controllerStopped)
                EndBlock();
        }
    }

    private void StartBlock(ExperimentController ec)
    {
        blockInitialized = true;
        blockStartTimeAbs = Time.time;

        participantID = string.IsNullOrWhiteSpace(ec.participantID) ? "P000" : ec.participantID;
        blockLabel = ec.currentBlock.ToString();

        runtimeFolder = Path.Combine(Application.persistentDataPath, relativeFolderPath);
        Directory.CreateDirectory(runtimeFolder);

        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"P{participantID}_{blockLabel}_BlockLog_{stamp}.csv";
        runtimeFilePath = Path.Combine(runtimeFolder, fileName);

#if UNITY_EDITOR
        assetsFolder = Path.Combine(Application.dataPath, relativeFolderPath);
        assetsCopyPath = Path.Combine(assetsFolder, fileName);
#endif

        writer = new StreamWriter(runtimeFilePath, append: false);
        writer.WriteLine(Header);
        writer.Flush();

        Debug.Log($"[PositionRecorder3] BLOCK START. Runtime CSV:\n{runtimeFilePath}");
#if UNITY_EDITOR
        Debug.Log($"[PositionRecorder3] Will copy into Assets at block end:\n{assetsCopyPath}");
#endif

        lastTrialIndex = int.MinValue;
    }

    private void StartTrial(ExperimentController ec, int newTrialIndex)
    {
        lastTrialIndex = newTrialIndex;

        trialStartTimeAbs = Time.time;
        sampleTimer = 0f;
        samplingEnabled = true;

        trialEnded = false;
        trialEndReason = "";
        trialEndTimeAbs = -1f;
        trialEndTimeRel = -1f;

        surveyQuestion = "";
        surveyResponse = "";
        surveyRT = -1f;

        mergeCueMarked = false;
        mergeCueTimeRel = -1f;

        responseMarked = false;
        driverResponseTimeRel = -1f;
        brakeMarked = false;
        steerMarked = false;
        responseType = "None";
        reactionTime = -1f;

        var cond = ec.CurrentCondition;
        trialType = cond.isPractice ? "Practice" : "Main";
        expectancyLabel = cond.expectancy.ToString();
        signalColorLabel = cond.signalColor.ToString();
        mergeSideLabel = cond.mergeSide.ToString();

        EnsureTarget(forceFind: true);

        WriteMarker("TRIAL_START");
        Flush();
    }

    private void EndBlock()
    {
        blockEnded = true;

        WriteMarker("BLOCK_END");
        Flush();

        try { writer?.Close(); } catch { }
        writer = null;

#if UNITY_EDITOR
        try
        {
            Directory.CreateDirectory(assetsFolder);
            File.Copy(runtimeFilePath, assetsCopyPath, overwrite: true);
            AssetDatabase.Refresh();
            Debug.Log($"[PositionRecorder3] Copied final CSV into Assets:\n{assetsCopyPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PositionRecorder3] Failed to copy into Assets: {e.Message}");
        }
#endif

        Debug.Log($"[PositionRecorder3] BLOCK END. Saved runtime CSV at:\n{runtimeFilePath}");

        if (Current == this) Current = null;
        Destroy(gameObject);
    }

    private void EnsureTarget(bool forceFind = false)
    {
        if (!forceFind && target != null) return;

        var ec = ExperimentController.Instance;
        if (ec != null && !string.IsNullOrWhiteSpace(ec.playerObjectName))
        {
            var found = GameObject.Find(ec.playerObjectName);
            if (found != null)
            {
                target = found;
                targetRigidbody = target.GetComponent<Rigidbody>();
                return;
            }
        }

        target = null;
        targetRigidbody = null;
    }

    private void WriteMarker(string eventName)
    {
        float timeAbs = Time.time;
        float blockTimeRel = timeAbs - blockStartTimeAbs;
        float trialTimeRel = (trialStartTimeAbs > 0f) ? (timeAbs - trialStartTimeAbs) : 0f;

        WriteRow(eventName, timeAbs, blockTimeRel, trialTimeRel, null, null, null);
    }

    private void WriteRow(string eventName, float timeAbs, float blockTimeRel, float trialTimeRel,
                          Vector3? pos, float? laneDev, float? speedMPH)
    {
        if (writer == null) return;

        string sceneName = SceneManager.GetActiveScene().name;

        string x = pos.HasValue ? pos.Value.x.ToString("F4") : "";
        string y = pos.HasValue ? pos.Value.y.ToString("F4") : "";
        string z = pos.HasValue ? pos.Value.z.ToString("F4") : "";
        string ld = laneDev.HasValue ? laneDev.Value.ToString("F4") : "";
        string sp = speedMPH.HasValue ? speedMPH.Value.ToString("F2") : "";

        UpdateResponseType();
        RecomputeRT();

        int endedInt = trialEnded ? 1 : 0;

        string line =
            $"{participantID}," +
            $"{blockLabel}," +
            $"{blockStartTimeAbs:F2}," +
            $"{blockTimeRel:F2}," +
            $"{lastTrialIndex}," +
            $"{trialType}," +
            $"{expectancyLabel}," +
            $"{signalColorLabel}," +
            $"{mergeSideLabel}," +
            $"{CsvEscape(sceneName)}," +
            $"{eventName}," +
            $"{timeAbs:F2}," +
            $"{trialTimeRel:F2}," +
            $"{x},{y},{z}," +
            $"{ld}," +
            $"{sp}," +
            $"{endedInt}," +
            $"{CsvEscape(trialEndReason)}," +
            $"{trialEndTimeAbs:F2}," +
            $"{trialEndTimeRel:F2}," +
            $"{mergeCueTimeRel:F3}," +
            $"{driverResponseTimeRel:F3}," +
            $"{reactionTime:F3}," +
            $"{CsvEscape(responseType)}," +
            $"{CsvEscape(surveyQuestion)}," +
            $"{CsvEscape(surveyResponse)}," +
            $"{surveyRT:F3}";

        try { writer.WriteLine(line); }
        catch (Exception e)
        {
            Debug.LogError("[PositionRecorder3] Failed writing row: " + e.Message);
        }
    }

    private void Flush()
    {
        try { writer?.Flush(); } catch { }
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool mustQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
        return mustQuote ? $"\"{s}\"" : s;
    }

    private void OnApplicationQuit()
    {
        if (blockInitialized && !blockEnded)
        {
            try
            {
                WriteMarker("BLOCK_END");
                Flush();
                writer?.Close();
            }
            catch { }
        }
    }

    private void OnGUI()
    {
        if (!showSavePathOnScreen) return;
        if (!blockInitialized) return;

        GUI.Label(new Rect(10, 10, 1400, 22), $"CSV saving to: {runtimeFilePath}");
        if (samplingEnabled)
            GUI.Label(new Rect(10, 32, 1400, 22), $"Trial end: press {debugEndKey}   |   Timeout: {(enableTrialTimeout ? trialTimeoutSeconds + "s" : "OFF")}");
    }
}
