using UnityEngine;
using System.Collections;
using System.IO;

public class PositionLogger : MonoBehaviour
{
    public string fileName = "position_log.csv";
    public float logInterval = 0.1f; // Log every tenth of a second

    private StreamWriter writer;
    private float timer = 0f;

    void Start()
    {
        writer = new StreamWriter(fileName);
        writer.WriteLine("Time,PositionX,PositionY,PositionZ"); // Write header row
    }

    void Update()
    {
        timer += Time.deltaTime;

        if (timer >= logInterval)
        {
            Vector3 position = transform.position;
            writer.WriteLine($"{Time.time},{position.x},{position.y},{position.z}"); // Write position data
            timer = 0f;
        }
    }

    void OnDestroy()
    {
        if (writer != null)
        {
            writer.Close();
        }
    }
}
