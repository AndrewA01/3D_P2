using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class Collider2 : MonoBehaviour
{
    public string tagToDetect = "Player";
    public float logInterval = 0.1f;

    private List<string> logData = new List<string>();
    private string folderPath = "Assets/CSVCollection/BoxCollider";

    private bool isOnCollider = false;
    private int collisionCount = 0;
    private float timer = 0f;

    void Start()
    {
        logData.Add("Time,OnCollider,CollisionCount");

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= logInterval)
        {
            timer = 0f;
            float currentTime = Time.time;
            int state = isOnCollider ? 1 : 0;
            logData.Add($"{currentTime:F2},{state},{collisionCount}");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(tagToDetect))
        {
            isOnCollider = true;
            collisionCount++;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(tagToDetect))
        {
            isOnCollider = false;
        }
    }

    void OnApplicationQuit()
    {
        SaveToCSV();
    }

    void SaveToCSV()
    {
        string fileName = $"ColliderStateLog_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";
        string filePath = Path.Combine(folderPath, fileName);

        try
        {
            File.WriteAllLines(filePath, logData);
            Debug.Log($"Collider state log saved to: {filePath}");
        }
        catch (IOException e)
        {
            Debug.LogError($"Failed to save log: {e.Message}");
        }
    }
}
