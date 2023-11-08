using UnityEngine;
using UnityEngine.UI;

public class Speedometer : MonoBehaviour
{
    public Transform targetObject;  // The object whose speed you want to measure
    public Text speedText;          // Reference to the UI Text element

    private Rigidbody rb;           // Rigidbody of the target object
    private float speedMPS;         // Speed in meters per second

    private void Start()
    {
        // Ensure you have a reference to the Rigidbody component
        rb = targetObject.GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("The target object does not have a Rigidbody component.");
            enabled = false;
            return;
        }
    }

    private void Update()
    {
        // Calculate speed in meters per second
        speedMPS = rb.velocity.magnitude;

        // Convert speed to mph
        float speedMPH = ConvertMPStoMPH(speedMPS);

        // Update the UI Text with the speed in mph
        speedText.text = "Speed: " + speedMPH.ToString("F2") + " mph";
    }

    // Helper method to convert meters per second to miles per hour
    private float ConvertMPStoMPH(float speedMPS)
    {
        const float metersPerMile = 1609.344f;
        const float secondsPerHour = 3600f;
        return (speedMPS * metersPerMile) / secondsPerHour;
    }
}
