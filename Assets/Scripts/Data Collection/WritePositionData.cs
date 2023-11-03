// using System.Collections;
// using System.Collections.Generic;
// using UnityEngine;

// public class WritePositionData : MonoBehaviour
// {
//     private string positionDataFilePath;
//     public GameObject Head;
//     public GameObject Stylus;
//     private StreamWriter file;
//     void Start()
//     {
//         positionData = new List<string>();
//         positionDataFilePath = Path.Combine(Application.persistentDataPath, "TEST.csv");
//         file = new StreamWriter(new FileStream(positionDataFilePath, FileMode.Create), Encoding.UTF8);
//         // Write the header line to the position data file
//         file.WriteLine("TimeStamp,StylusY,StylusZ,StylusX,HeadY,HeadZ,HeadX");
//     }
//     void Update()
//     {
//         // Fetch the positions of the VR headset and stylus
//         Vector3 currentHeadsetPosition = Head.transform.position;
//         Vector3 currentStylusPosition = Stylus.transform.position;
//         // Format the current positions into a string for the CSV file
//         string positionDataLine = string.Format(
//             "{0},{1},{2},{3},{4},{5},{6}",
//             DateTime.Now,
//             currentStylusPosition.x, currentStylusPosition.y, currentStylusPosition.z,
//             currentHeadsetPosition.x, currentHeadsetPosition.y, currentHeadsetPosition.z
//         );
//         file.WriteLine(positionDataLine);
//     }
//     void OnApplicationQuit()
//     {
//         file.Flush();
//         file.Close();
//     }
// }