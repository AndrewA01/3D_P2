using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class PositioRecorder3 : MonoBehaviour
{
    public GameObject target; // Assign in Inspector
    public float recordInterval = 0.1f; // Time in seconds between records

    private List<string> positionData = new List<string>();
    private float timer = 0f;

    // Folder path relative to the project
    private string relativeFolderPath = "Assets/CSVCollection/NewPL";

    void Start()
    {
        positionData.Add("Time,X,Y,Z");

        // Ensure the folder exists
        if (!Directory.Exists(relativeFolderPath))
        {
            Directory.CreateDirectory(relativeFolderPath);
            Debug.Log($"Created folder at: {relativeFolderPath}");
        }
    }

    void Update()
    {
        if (target == null) return;

        timer += Time.deltaTime;
        if (timer >= recordInterval)
        {
            timer = 0f;
            Vector3 pos = target.transform.position;
            string entry = $"{Time.time:F2},{pos.x:F4},{pos.y:F4},{pos.z:F4}";
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
            Debug.Log($"Position data saved to: {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"Failed to save file: {e.Message}");
        }
    }
}
