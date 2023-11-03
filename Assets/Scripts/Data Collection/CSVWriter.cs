using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.IO;
using System.Text;


public class CSVWriter : MonoBehaviour
{
    private string csvFilePath = "C:/Unity/Projects/3D_P2/Assets/Scripts/Data Collection/MyData.csv";
    private StringBuilder csvContent = new StringBuilder();

    // Start is called before the first frame update
    void Start()
    {
        WriteData("Name,Score,Time");
        WriteData("John,100,12:30");
        WriteData("Jane,150,12:35");
        SaveCSV();
    }

    void WriteData(string data)
    {
        csvContent.AppendLine(data);
    }

    void SaveCSV()
    {
        File.WriteAllText(csvFilePath, csvContent.ToString());
        Debug.Log("CSV written to: " + csvFilePath);
    }
}