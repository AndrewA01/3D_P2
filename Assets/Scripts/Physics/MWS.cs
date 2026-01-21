using UnityEngine;

public class MWS : MonoBehaviour
{
    [SerializeField] WheelCollider FL;
    [SerializeField] WheelCollider FR;
    [SerializeField] WheelCollider RL;
    [SerializeField] WheelCollider RR;

    public float acceleration = 500f;
    public float breakingForce = 250f;
    public float maxTurnAngle = 15f;

    [Header("DEBUG INPUT")]
    [SerializeField] float verticalInput;
    [SerializeField] float horizontalInput;

    float currentAcceleration = 0f;
    float currentBreakForce = 0f;
    float currentTurnAngle = 0f;

    void Update()
    {
        verticalInput = Input.GetAxis("Vertical");
        horizontalInput = Input.GetAxis("Horizontal");
    }

    void FixedUpdate()
    {
        currentAcceleration = acceleration * verticalInput;
        currentBreakForce = Input.GetKey(KeyCode.Space) ? breakingForce : 0f;
        currentTurnAngle = maxTurnAngle * horizontalInput;

        FL.motorTorque = FR.motorTorque = RL.motorTorque = RR.motorTorque = currentAcceleration;
        FL.brakeTorque = FR.brakeTorque = RL.brakeTorque = RR.brakeTorque = currentBreakForce;

        FL.steerAngle = currentTurnAngle;
        FR.steerAngle = currentTurnAngle;

        // ---- DEBUG ONCE PER SECOND ----
        if (Time.frameCount % 60 == 0)
        {
            DebugWheel("FL", FL);
            DebugWheel("FR", FR);
            DebugWheel("RL", RL);
            DebugWheel("RR", RR);

            var rb = GetComponentInParent<Rigidbody>() ?? GetComponent<Rigidbody>();
            if (rb)
            {
                Debug.Log($"RB: vel={rb.velocity.magnitude:F2} kinematic={rb.isKinematic} constraints={rb.constraints}");
            }
        }
    }

    void DebugWheel(string name, WheelCollider wc)
    {
        if (!wc) { Debug.Log($"{name}: MISSING"); return; }

        WheelHit hit;
        bool grounded = wc.GetGroundHit(out hit);

        Debug.Log($"{name}: grounded={grounded} rpm={wc.rpm:F1} torque={wc.motorTorque:F1} brake={wc.brakeTorque:F1}");
    }
}
