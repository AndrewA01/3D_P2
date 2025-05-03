using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;

public class csv1build : MonoBehaviour

{

[Serializable]
public class KeyFrame
{
    public int Value;
    public float Time;

    public KeyFrame(){}

    public KeyFrame (int value, float time)
    {
        Value = value;
        Time = time;
    }
}

private List<KeyFrame> keyFrames = new List<KeyFrame>(10000);

private int _counter;
public int Counter
{
    get => _counter;
    set
    {
        _counter = value;
        keyFrames.Add(new KeyFrame (value, Time.time));
    }
}
public string ToCSV()
{
    var sb = new StringBuilder("Time,Value");
    foreach(var frame in keyFrames)
    {
        sb.Append('\n').Append(frame.Time.ToString()).Append(',').Append(frame.Value.ToString());
    }

    return sb.ToString();
}

}

