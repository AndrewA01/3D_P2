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

    [Header("INPUT SOURCE")]
    [SerializeField] G920V1 input;   // drag your G920V1 component here (or auto-find below)

    [Header("DEBUG INPUT")]
    [SerializeField] float verticalInput;
    [SerializeField] float horizontalInput;

    float currentAcceleration = 0f;
    float currentBreakForce = 0f;
    float currentTurnAngle = 0f;

    void Awake()
    {
        if (!input) input = FindFirstObjectByType<G920V1>();
    }

    void Update()
    {
        // Use filtered wheel+keyboard values (prevents reverse creep)
        if (input)
        {
            verticalInput = input.Throttle;
            horizontalInput = input.Steer;
        }
        else
        {
            // fallback if G920V1 isn't present
            verticalInput = Input.GetAxis("Vertical");
            horizontalInput = Input.GetAxis("Horizontal");
        }
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
    }
}
