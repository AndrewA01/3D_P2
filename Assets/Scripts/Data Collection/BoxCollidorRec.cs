using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class BoxCollidorRec : MonoBehaviour
{
    public string tagToDetect = "Player"; // Set this to the tag of the object you're detecting
    private List<string> logData = new List<string>();
    private int collisionCount = 0;
    private string folderPath = "Assets/CSVCollection/BoxCollidor";

    void Start()
    {
        logData.Add("Time,CollisionCount");

        // Make sure the folder exists
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(tagToDetect))
        {
            collisionCount++;
            string entry = $"{Time.time:F2},{collisionCount}";
            logData.Add(entry);
            Debug.Log($"Collision #{collisionCount} at {Time.time:F2} seconds.");
        }
    }

    void OnApplicationQuit()
    {
        SaveToCSV();
    }

    void SaveToCSV()
    {
        string fileName = $"CollisionLog_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        string filePath = Path.Combine(folderPath, fileName);

        try
        {
            File.WriteAllLines(filePath, logData);
            Debug.Log($"Collision log saved to: {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"Failed to save log: {e.Message}");
        }
    }
}
