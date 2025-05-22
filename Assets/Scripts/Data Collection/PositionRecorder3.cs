using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PositionRecorder3 : MonoBehaviour
{
    public GameObject target;               // Assign the target GameObject in Inspector
    public float recordInterval = 0.1f;    // Seconds between records

    private List<string> positionData = new List<string>();
    private float timer = 0f;

    // Folder path relative to the project
    private string relativeFolderPath = "Assets/CSVCollection/NewPL";

    private Rigidbody targetRigidbody;

    void Start()
    {
        positionData.Add("Time,X,Y,Z,SpeedMPH");  // Add SpeedMPH to CSV header

        // Ensure the folder exists
        if (!Directory.Exists(relativeFolderPath))
        {
            Directory.CreateDirectory(relativeFolderPath);
            Debug.Log($"Created folder at: {relativeFolderPath}");
        }

        // Try to get Rigidbody from target for speed calculation
        if (target != null)
            targetRigidbody = target.GetComponent<Rigidbody>();

        if (targetRigidbody == null)
            Debug.LogWarning("No Rigidbody found on target. Speed will be recorded as 0.");
    }

    void Update()
    {
        if (target == null) return;

        timer += Time.deltaTime;
        if (timer >= recordInterval)
        {
            timer = 0f;
            Vector3 pos = target.transform.position;

            float speedMPH = 0f;
            if (targetRigidbody != null)
                speedMPH = targetRigidbody.velocity.magnitude * 2.23694f;  // Convert m/s to MPH

            string entry = $"{Time.time:F2},{pos.x:F4},{pos.y:F4},{pos.z:F4},{speedMPH:F2}";
            positionData.Add(entry);
        }
    }

    void OnApplicationQuit()
    {
        SaveToCSV();
    }

    private void SaveToCSV()
    {
        string fileName = $"PositionData_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        string filePath = Path.Combine(relativeFolderPath, fileName);

        try
        {
            File.WriteAllLines(filePath, positionData);
            Debug.Log($"Position and speed data saved to: {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"Failed to save file: {e.Message}");
        }
    }
}
