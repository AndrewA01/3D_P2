using UnityEngine;

public class G920NamedAxisLogger : MonoBehaviour
{
    public string steerAxis = "Horizontal";
    public string throttleAxis = "Throttle";
    public string brakeAxis = "Brake";

    public float changeThreshold = 0.02f;

    float lastSteer, lastThrot, lastBrake;

    void Update()
    {
        float steerRaw = Input.GetAxisRaw(steerAxis);
        float throtRaw = Input.GetAxisRaw(throttleAxis);
        float brakeRaw = Input.GetAxisRaw(brakeAxis);

        float throtPct = RawToPercent(throtRaw);
        float brakePct = RawToPercent(brakeRaw);

        if (Mathf.Abs(steerRaw - lastSteer) > changeThreshold ||
            Mathf.Abs(throtRaw - lastThrot) > changeThreshold ||
            Mathf.Abs(brakeRaw - lastBrake) > changeThreshold)
        {
            Debug.Log($"Steer raw:{steerRaw:F3} | Throttle raw:{throtRaw:F3} pct:{throtPct:F1} | Brake raw:{brakeRaw:F3} pct:{brakePct:F1}");
            lastSteer = steerRaw;
            lastThrot = throtRaw;
            lastBrake = brakeRaw;
        }
    }

    float RawToPercent(float raw)
    {
        // -1..1 -> 0..100
        return Mathf.Clamp01((raw + 1f) * 0.5f) * 100f;
    }
}
