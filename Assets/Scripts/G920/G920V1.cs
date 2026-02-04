using UnityEngine;
using System.Linq;

public class G920V1 : MonoBehaviour
{
    [Header("Axis Names (Input Manager)")]
    public string steerAxis = "Horizontal";
    public string throttleAxis = "Throttle";
    public string brakeAxis = "Brake";

    [Header("Keyboard Keys")]
    public KeyCode keyForward = KeyCode.W;
    public KeyCode keyBack = KeyCode.S;
    public KeyCode keyLeft = KeyCode.A;
    public KeyCode keyRight = KeyCode.D;

    [Header("Deadzone Settings")]
    public float pedalDeadzone = 0.1f;
    public float steerDeadzone = 0.02f;

    public float Steer { get; private set; }
    public float Throttle { get; private set; }
    public bool G920Connected { get; private set; }

    void Update()
    {
        G920Connected = IsG920Connected();

        // ---------- STEERING ----------
        float keyboardSteer = 0f;
        if (Input.GetKey(keyLeft)) keyboardSteer -= 1f;
        if (Input.GetKey(keyRight)) keyboardSteer += 1f;

        float wheelSteer = 0f;
        TryGetAxisRaw(steerAxis, out wheelSteer);

        if (Mathf.Abs(wheelSteer) > steerDeadzone)
            Steer = wheelSteer;
        else
            Steer = keyboardSteer;

        // ---------- THROTTLE ----------
        float keyboardThrottle = 0f;
        if (Input.GetKey(keyForward)) keyboardThrottle += 1f;
        if (Input.GetKey(keyBack)) keyboardThrottle -= 1f;

        float throttle = 0f;
        float brake = 0f;

        bool hasThrottle = TryGetAxisRaw(throttleAxis, out throttle);
        bool hasBrake = TryGetAxisRaw(brakeAxis, out brake);

        float wheelThrottle = 0f;

        if (hasThrottle || hasBrake)
        {
            float throttle01 = NormalizeTo01(throttle);
            float brake01 = NormalizeTo01(brake);

            if (G920Connected)
            {
                throttle01 = ApplyDeadzone(throttle01, pedalDeadzone);
                brake01 = ApplyDeadzone(brake01, pedalDeadzone);
            }

            wheelThrottle = throttle01 - brake01;
        }

        Throttle = Mathf.Abs(keyboardThrottle) > 0.01f
            ? keyboardThrottle
            : wheelThrottle;

        // Final safety: kill reverse creep
        if (G920Connected && Mathf.Abs(Throttle) < pedalDeadzone)
            Throttle = 0f;
    }

    bool IsG920Connected()
    {
        var names = Input.GetJoystickNames();
        return names.Any(n =>
            !string.IsNullOrEmpty(n) &&
            (n.ToLower().Contains("g920") || n.ToLower().Contains("logitech")));
    }

    bool TryGetAxisRaw(string axis, out float value)
    {
        try
        {
            value = Input.GetAxisRaw(axis);
            return true;
        }
        catch
        {
            value = 0f;
            return false;
        }
    }

    float NormalizeTo01(float v)
    {
        return Mathf.Clamp01((v + 1f) * 0.5f);
    }

    float ApplyDeadzone(float v, float dz)
    {
        if (v < dz) return 0f;
        return (v - dz) / (1f - dz);
    }
}
