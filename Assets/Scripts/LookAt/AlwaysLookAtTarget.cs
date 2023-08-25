using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AlwaysLookAtTarget : MonoBehaviour
{
    public Transform Target;

    public void Update()
    {
        transform.LookAt(Target);
        
    }

}