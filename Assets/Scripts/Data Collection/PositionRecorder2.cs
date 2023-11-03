using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class PositionRecorder2 : MonoBehaviour
{
    public float recordingInterval = 0.1f; // Recording interval in seconds
    public GameObject targetObject; // The GameObject whose position you want to record

    private string csvFilePath;
    private StreamWriter csvWriter;
    private float elapsedTime;

    private void Start()
    {
        // Create a directory to store the CSV files (if it doesn't exist)
        string directoryPath = Application.dataPath + "/PositionRecordings";
        Directory.CreateDirectory(directoryPath);

        // Generate a unique filename based on the current date and time
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        csvFilePath = directoryPath + "/PositionRecording_" + timestamp + ".csv";

        // Open the CSV file for writing
        csvWriter = new StreamWriter(csvFilePath);

        // Write a header row to the CSV file
        csvWriter.WriteLine("Time,Position_X,Position_Y,Position_Z");

        // Initialize the timer
        elapsedTime = 0f;
    }

    private void Update()
    {
        elapsedTime += Time.deltaTime;

        if (elapsedTime >= recordingInterval)
        {
            RecordPosition();
            elapsedTime = 0f;
        }
    }

    private void RecordPosition()
    {
        // Get the current time and the target object's position
        string currentTime = Time.time.ToString();
        Vector3 position = targetObject.transform.position;

        // Write the data to the CSV file
        string data = currentTime + "," + position.x + "," + position.y + "," + position.z;
        csvWriter.WriteLine(data);
    }

    private void OnApplicationQuit()
    {
        // Close the CSV file when the application quits
        if (csvWriter != null)
        {
            csvWriter.Close();
        }
    }
}
