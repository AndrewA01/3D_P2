using UnityEngine;
using System.Collections;
using System.IO;
using System.Text;

public class PositionRecorder : MonoBehaviour
{
    public GameObject objectToRecord;
    public float recordingInterval = 0.1f; // 0.1 seconds (10 times per second)
    private float timer = 0;
    private string csvHeader = "Time,PositionX,PositionY,PositionZ";
    private StringBuilder csvData = new StringBuilder();

    void Start()
    {
        // Start recording when the game starts
        StartRecording();
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= recordingInterval)
        {
            RecordPosition();
            timer = 0;
        }
    }

    void RecordPosition()
    {
        float time = Time.time;
        Vector3 position = objectToRecord.transform.position;

        string record = string.Format("{0},{1},{2},{3}", time, position.x, position.y, position.z);
        csvData.AppendLine(record);
    }

    void StartRecording()
    {
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = "PositionRecording_" + timestamp + ".csv";
        string filePath = Path.Combine(Application.dataPath, fileName);

        // Write the CSV header to the file
        File.WriteAllText(filePath, csvHeader);

        // Start recording
        StartCoroutine(RecordingRoutine(filePath));
    }

    IEnumerator RecordingRoutine(string filePath)
    {
        while (true)
        {
            yield return new WaitForSeconds(recordingInterval);
            File.AppendAllText(filePath, csvData.ToString());

            // Clear the data for the next recording
            csvData.Length = 0;
        }
    }
}
