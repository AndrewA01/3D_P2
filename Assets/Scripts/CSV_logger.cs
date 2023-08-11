using UnityEngine;
using System.Collections.Generic;
using System.Text;
using System.IO;

public class CSV_logger : MonoBehaviour
{
private List<string[]> rowData = new List<string[]>();

public Vector3 lastposition;

// Use this for initialization
void Start()
{
lastposition = transform.position;
// Creating First row of titles manually..
string[] rowDataTemp = new string[3];
rowDataTemp[0] = "X";
rowDataTemp[1] = "Y";
rowDataTemp[2] = "Z";
rowData.Add(rowDataTemp);
}

private void Update()
{
if(lastposition != transform.position)
{
Save();
lastposition = transform.position;
}
}

void Save()
{
string[] rowDataTemp = new string[3];
rowDataTemp[0] = transform.position.x.ToString();
rowDataTemp[1] = transform.position.y.ToString();
rowDataTemp[2] = transform.position.z.ToString();
rowData.Add(rowDataTemp);

string[][] output = new string[rowData.Count][];

for (int i = 0; i < output.Length; i++)
{
output[i] = rowData[i];
}

int length = output.GetLength(0);
string delimiter = ",";

StringBuilder sb = new StringBuilder();

for (int index = 0; index < length; index++)
sb.AppendLine(string.Join(delimiter, output[index]));


string filePath = getPath();

StreamWriter outStream = System.IO.File.CreateText(filePath);
outStream.WriteLine(sb);
outStream.Close();
}

// Following method is used to retrive the relative path as device platform
private string getPath()
{
#if UNITY_EDITOR
return Application.dataPath + "/CSV/" + "Saved_data.csv";
#elif UNITY_ANDROID
return Application.persistentDataPath+"Saved_data.csv";
#elif UNITY_IPHONE
return Application.persistentDataPath+"/"+"Saved_data.csv";
#else
return Application.dataPath +"/"+"Saved_data.csv";
#endif
}
}