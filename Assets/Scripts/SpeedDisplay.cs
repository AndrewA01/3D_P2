using UnityEngine;
using UnityEngine.UI;

public class SpeedDisplay : MonoBehaviour
{
    public Text speedText;
    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>(); // Assuming you have a Rigidbody on the GameObject
    }

    private void Update()
    {
        if (rb != null)
        {
            float speed = rb.velocity.magnitude * 2;
            speedText.text = " " + speed.ToString("F2") + " MPH"; // Display speed with 2 decimal places.
        }
    }
}
