using UnityEngine;

public class SWRotation : MonoBehaviour
{
    [Header("Steering Input Settings")]
    public float maxSteeringAngle = 270f;      // Full left = -135, right = +135
    public float steerSpeed = 5f;              // How quickly the wheel reacts

    private float currentSteeringAngle = 0f;

    void Update()
    {
        // Get horizontal input (Wheels turn even if car is slow)
        float input = Input.GetAxis("Horizontal"); // Range -1 to 1

        // Target angle based on input
        float targetAngle = input * (maxSteeringAngle / 2f);

        // Smoothly move toward that angle
        currentSteeringAngle = Mathf.Lerp(currentSteeringAngle, targetAngle, steerSpeed * Time.deltaTime);

        // Apply to visual wheel (adjust axis if needed)
        transform.localRotation = Quaternion.Euler(0f, -currentSteeringAngle, 0f); // Use X or Z based on your setup
    }
}
