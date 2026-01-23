using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PositionRecorder3 : MonoBehaviour
{
    public static PositionRecorder3 Current { get; private set; }

    [Header("Target")]
    public GameObject target;
    public float recordInterval = 0.1f;

    [Header("Lane Center (optional)")]
    public Transform laneCenter;

    [Header("Output Folder (relative to project)")]
    public string relativeFolderPath = "Assets/CSVCollection/NewPL";

    private Rigidbody targetRigidbody;
    private float timer;
    private float trialStartTime;

    private bool recordingEnabled = true;
    private bool saved = false;

    // ===== Metadata from ExperimentController =====
    private string participantID = "NA";
    private string blockLabel = "NA";
    private int trialIndex = -1;

    private string trialType = "Unknown";
    private string expectancyLabel = "Unknown";
    private string signalColorLabel = "Unknown";
    private string mergeSideLabel = "Unknown";

    // ===== Trial end =====
    private bool trialEnded = false;
    private string trialEndReason = "";
    private float trialEndTimeAbs = -1f;
    private float trialEndTimeRel = -1f;

    // ===== Survey =====
    private string surveyQuestion = "";
    private string surveyResponse = "";
    private float surveyRT = -1f;

    // NEW: Color identification accuracy
    private int colorIDAccuracy = -1; // 1 = correct, 0 = incorrect

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
        "SurveyQuestion,SurveyResponse,SurveyRT,ColorID_Accuracy";

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
        if (!recordingEnabled || target == null) return;

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

    // Call BEFORE loading PTQ
    public void StopRecordingForQuestion(string endReason)
    {
        if (trialEnded) return;

        recordingEnabled = false;
        trialEnded = true;
        trialEndReason = endReason ?? "";
        trialEndTimeAbs = Time.time;
        trialEndTimeRel = Time.time - trialStartTime;
    }

    // Called by PTQS_1
    public void SetSurveyResult(string question, string response, float rt)
    {
        surveyQuestion = question ?? "";
        surveyResponse = response ?? "";
        surveyRT = rt;

        // ===== Compute ColorID_Accuracy =====
        colorIDAccuracy = ComputeColorAccuracy(surveyResponse, signalColorLabel);
    }

    public void SaveNowAndCleanup()
    {
        if (saved) return;
        saved = true;

        SaveToCSV();

        if (Current == this) Current = null;
        Destroy(gameObject);
    }

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
                        $"{CsvEscape(trialEndReason)}," +
                        $"{trialEndTimeAbs:F2}," +
                        $"{trialEndTimeRel:F2}," +
                        $"{CsvEscape(surveyQuestion)}," +
                        $"{CsvEscape(surveyResponse)}," +
                        $"{surveyRT:F3}," +
                        $"{colorIDAccuracy}";

                    sw.WriteLine(row);
                }
            }

            Debug.Log($"PositionRecorder3: Saved {samples.Count} rows → {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"PositionRecorder3: Save failed: {e.Message}");
        }
    }

    // ===== Color accuracy logic =====
    private int ComputeColorAccuracy(string response, string signalColor)
    {
        if (string.IsNullOrWhiteSpace(response) || string.IsNullOrWhiteSpace(signalColor))
            return 0;

        string r = response.Trim().ToLowerInvariant();
        string s = signalColor.Trim().ToLowerInvariant();

        return r.Contains(s) ? 1 : 0;
    }

    private static string CsvEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        bool mustQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (s.Contains("\"")) s = s.Replace("\"", "\"\"");
        return mustQuote ? $"\"{s}\"" : s;
    }
}
