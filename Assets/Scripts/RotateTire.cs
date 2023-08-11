using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RotateTire : MonoBehaviour
{
    Rigidbody m_Rigidbody;
    Vector3 m_EulerAngleVelocity;

    void Start()
    {
        //Fetch the Rigidbody from the GameObject with this script attached
        m_Rigidbody = GetComponent<Rigidbody>();

        //Set the angular velocity of the Rigidbody (rotate on x, y, or z axis, in deg/sec)
        m_EulerAngleVelocity = new Vector3(0, 200, 0);
    }

    void FixedUpdate()
    {
        if(Input.GetKey("w"))
        {
            Quaternion deltaRotation = Quaternion.Euler(-(m_EulerAngleVelocity) * Time.fixedDeltaTime);
            m_Rigidbody.MoveRotation(m_Rigidbody.rotation * deltaRotation);
        }
        if(Input.GetKey("s"))
        {
            Quaternion deltaRotation = Quaternion.Euler(m_EulerAngleVelocity * Time.fixedDeltaTime);
            m_Rigidbody.MoveRotation(m_Rigidbody.rotation * deltaRotation);
        }

    }
}