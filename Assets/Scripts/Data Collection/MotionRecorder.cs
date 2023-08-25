using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class MotionRecorder : MonoBehaviour
{
[System.Serializable]
private struct MotionFrame
{
public int frameIndex;
public float timeStamp;
public Vector3 position;
public Quaternion orientation;
}

public GameObject vrcam;

private StreamWriter writer;
private bool firstFrameWritten;

// Start is called before the first frame update
void Start()
{
string path = Application.persistentDataPath + "/" + "motion_" + System.DateTime.Now.ToString("s") + ".json";
writer = new StreamWriter(path, false);
writer.AutoFlush = false;
writer.WriteLine("{");
writer.WriteLine("\"frames\":[");
}

// Update is called once per frame
void LateUpdate()
{
MotionFrame frame;
frame.frameIndex = Time.frameCount;
frame.timeStamp = Time.time;
frame.position = vrcam.transform.position;
frame.orientation = vrcam.transform.rotation;

string jsonFrame = JsonUtility.ToJson(frame, true);
if (firstFrameWritten)
{
writer.WriteLine(",");
}
writer.Write(jsonFrame);
writer.FlushAsync();
firstFrameWritten = true;
}

void OnApplicationQuit()
{
writer.WriteLine("]");
writer.WriteLine("}");
writer.Close();
}
}