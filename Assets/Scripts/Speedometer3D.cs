using UnityEngine;
using TMPro;

public class Speedometer3D : MonoBehaviour
{
    public Rigidbody targetRigidbody;     // Drag your car Rigidbody here
    public TextMeshPro speedText3D;       // Drag the 3D TextMeshPro object here

    void FixedUpdate()
    {
        if (targetRigidbody != null && speedText3D != null)
        {
            float speed = targetRigidbody.velocity.magnitude * 2.23694f; // m/s to MPH
            int mph = Mathf.RoundToInt(speed);
            speedText3D.text = mph + "";
        }
    }
}
