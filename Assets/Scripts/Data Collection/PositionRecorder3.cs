using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PositionRecorder3 : MonoBehaviour
{
    [Header("Target")]
    public GameObject target;
    public float recordInterval = 0.1f;

    private List<string> positionRows = new List<string>();
    private float timer = 0f;
    private float trialStartTime = 0f;

    private string relativeFolderPath = "Assets/CSVCollection/NewPL";

    private Rigidbody targetRigidbody;

    // Metadata
    private string participantID = "NA";
    private string blockLabel = "NA";  // Day / Night
    private int trialIndex = -1;

    private string trialType = "Unknown";
    private string expectancyLabel = "Unknown";
    private string signalColorLabel = "Unknown";
    private string mergeSideLabel = "Unknown";

    private const string CsvHeader =
        "ParticipantID,Block,TrialIndex,TrialType,Expectancy,SignalColor,MergeSide," +
        "TimeAbsolute,TimeRelative,X,Y,Z,SpeedMPH";

    void Start()
    {
        trialStartTime = Time.time;

        // Pull metadata from ExperimentController
        if (ExperimentController.Instance != null && ExperimentController.Instance.experimentRunning)
        {
            participantID = ExperimentController.Instance.participantID; 
            blockLabel = ExperimentController.Instance.currentBlock.ToString(); // Day/Night
            trialIndex = ExperimentController.Instance.currentTrialIndex;

            var cond = ExperimentController.Instance.CurrentCondition;

            trialType = cond.isPractice ? "Practice" : "Main";
            expectancyLabel = cond.expectancy.ToString();
            signalColorLabel = cond.signalColor.ToString();
            mergeSideLabel = cond.mergeSide.ToString();
        }

        // Ensure folder exists
        if (!Directory.Exists(relativeFolderPath))
            Directory.CreateDirectory(relativeFolderPath);

        // Rigidbody for speed
        if (target != null)
            targetRigidbody = target.GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (target == null) return;

        timer += Time.deltaTime;
        if (timer >= recordInterval)
        {
            timer = 0f;

            Vector3 pos = target.transform.position;

            float speedMPH = (targetRigidbody != null)
                ? targetRigidbody.velocity.magnitude * 2.23694f
                : 0f;

            float timeAbsolute = Time.time;
            float timeRelative = Time.time - trialStartTime;

            string row =
                $"{participantID}," +
                $"{blockLabel}," +
                $"{trialIndex}," +
                $"{trialType}," +
                $"{expectancyLabel}," +
                $"{signalColorLabel}," +
                $"{mergeSideLabel}," +
                $"{timeAbsolute:F2}," +
                $"{timeRelative:F2}," +
                $"{pos.x:F4},{pos.y:F4},{pos.z:F4}," +
                $"{speedMPH:F2}";

            positionRows.Add(row);
        }
    }

    void OnDestroy()
    {
        SaveToCSV();
    }

    private void SaveToCSV()
    {
        // -----------------------------------------
        // ⭐ NEW NAMING FORMAT:
        // P{ID}_{Day/Night}.csv
        // -----------------------------------------
        string fileName = $"P{participantID}_{blockLabel}.csv";
        string filePath = Path.Combine(relativeFolderPath, fileName);

        try
        {
            bool exists = File.Exists(filePath);

            using (StreamWriter sw = new StreamWriter(filePath, append: true))
            {
                // Write header ONLY once
                if (!exists)
                    sw.WriteLine(CsvHeader);

                // Write all data rows for this trial
                foreach (string row in positionRows)
                    sw.WriteLine(row);
            }

            Debug.Log($"Appended {positionRows.Count} rows → {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"Failed to save file: {e.Message}");
        }
    }
}
