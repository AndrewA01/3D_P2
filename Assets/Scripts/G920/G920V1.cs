using UnityEngine;
using System.Linq;

/// <summary>
/// Logitech G920 input using OLD Input Manager only.
/// No New Input System references → guaranteed to compile.
/// Works in builds if axes exist in Project Settings.
/// 
/// Fixes build "creep/stuck pedals" by auto-calibrating pedal REST baselines
/// for a short window after enable/scene load, then computing pedal percent
/// (0 at rest, 100 at full press) relative to that baseline.
/// </summary>
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
    [Range(0f, 0.5f)] public float pedalDeadzone = 0.1f;
    [Range(0f, 0.5f)] public float steerDeadzone = 0.02f;

    [Header("Scene Load / Reinit Safety")]
    [Tooltip("During this time after enable, pedal baselines are sampled and wheel input is ignored.")]
    public float reinitLockSeconds = 0.25f;

    [Header("Debug")]
    public bool verboseLogs = false;

    public float Steer { get; private set; }        // -1..1
    public float Throttle { get; private set; }     // -1..1 (legacy: throttle01 - brake01)
    public bool G920Connected { get; private set; }

    // Pedals on 0..100 scale (0 rest, 100 fully pressed)
    public float ThrottlePercent { get; private set; }  // 0..100
    public float BrakePercent { get; private set; }     // 0..100

    float _lockTimer;

    // Auto-calibration baselines (rest values)
    float _throttleRestRaw = 0f;
    float _brakeRestRaw = 0f;

    // Calibration accumulators
    float _throttleSum;
    float _brakeSum;
    int _calibSamples;

    void OnEnable()
    {
        _lockTimer = Mathf.Max(0f, reinitLockSeconds);

        // Reset Unity's cached input to reduce stale values on scene load.
        Input.ResetInputAxes();

        // Reset calibration state
        _throttleSum = 0f;
        _brakeSum = 0f;
        _calibSamples = 0;

        // Clear outputs immediately
        Steer = 0f;
        Throttle = 0f;
        ThrottlePercent = 0f;
        BrakePercent = 0f;
    }

    void Update()
    {
        // Default-safe outputs every frame (prevents “stuck” persistence if something interrupts flow)
        Steer = 0f;
        Throttle = 0f;
        ThrottlePercent = 0f;
        BrakePercent = 0f;

        // Detect wheel
        G920Connected = IsG920Connected();

        // -------- CALIBRATION LOCKOUT WINDOW --------
        // During lockout: sample pedal rest baselines while user is (ideally) not pressing pedals.
        if (_lockTimer > 0f)
        {
            _lockTimer -= Time.unscaledDeltaTime;

            float tRaw = GetAxisSafe(throttleAxis);
            float bRaw = GetAxisSafe(brakeAxis);

            // Accumulate valid samples (guard against NaN)
            if (!float.IsNaN(tRaw) && !float.IsInfinity(tRaw))
                _throttleSum += tRaw;
            if (!float.IsNaN(bRaw) && !float.IsInfinity(bRaw))
                _brakeSum += bRaw;

            _calibSamples++;

            if (_lockTimer <= 0f && _calibSamples > 0)
            {
                _throttleRestRaw = _throttleSum / _calibSamples;
                _brakeRestRaw = _brakeSum / _calibSamples;

                if (verboseLogs)
                {
                    Debug.Log($"[G920V1] Calibrated rest baselines: ThrottleRestRaw={_throttleRestRaw:F3}, BrakeRestRaw={_brakeRestRaw:F3} (samples={_calibSamples})");
                }
            }

            if (verboseLogs)
                Debug.Log($"[G920V1] Lockout {_lockTimer:F2}s remaining... Wheel:{G920Connected}");

            return;
        }

        // -------- STEERING --------
        float keyboardSteer = 0f;
        if (Input.GetKey(keyLeft)) keyboardSteer -= 1f;
        if (Input.GetKey(keyRight)) keyboardSteer += 1f;

        float wheelSteer = GetAxisSafe(steerAxis);
        wheelSteer = ApplyDeadzoneSymmetric(wheelSteer, steerDeadzone);

        Steer = Mathf.Abs(wheelSteer) > 0.001f ? wheelSteer : keyboardSteer;
        Steer = Mathf.Clamp(Steer, -1f, 1f);

        // -------- THROTTLE / BRAKE --------
        float keyboardThrottle = 0f;
        if (Input.GetKey(keyForward)) keyboardThrottle += 1f;
        if (Input.GetKey(keyBack)) keyboardThrottle -= 1f;

        float throttleRaw = GetAxisSafe(throttleAxis);
        float brakeRaw = GetAxisSafe(brakeAxis);

        // Convert raw axis -> 0..1 pressed amount using calibrated rest baselines
        float throttle01 = PedalRawTo01(throttleRaw, _throttleRestRaw);
        float brake01 = PedalRawTo01(brakeRaw, _brakeRestRaw);

        // Apply deadzone on 0..1 (so tiny jitter doesn’t cause creep)
        throttle01 = ApplyDeadzone01(throttle01, pedalDeadzone);
        brake01 = ApplyDeadzone01(brake01, pedalDeadzone);

        // Convert to 0..100 for logging/recording
        ThrottlePercent = Mathf.Clamp(throttle01 * 100f, 0f, 100f);
        BrakePercent = Mathf.Clamp(brake01 * 100f, 0f, 100f);

        // Legacy combined throttle (-1..1): throttle forward minus brake (reverse)
        float wheelThrottle = throttle01 - brake01;

        Throttle = Mathf.Abs(keyboardThrottle) > 0.01f ? keyboardThrottle : wheelThrottle;

        // Strong creep-kill for wheel input (keyboard should still behave precisely)
        if (G920Connected && Mathf.Abs(Throttle) < pedalDeadzone)
            Throttle = 0f;

        Throttle = Mathf.Clamp(Throttle, -1f, 1f);

        if (verboseLogs)
        {
            Debug.Log($"[G920V1] Wheel:{G920Connected} Steer:{Steer:F2} Throttle:{Throttle:F2} Throttle%:{ThrottlePercent:F0} Brake%:{BrakePercent:F0}  (raw T:{throttleRaw:F3} B:{brakeRaw:F3})");
        }
    }

    // ---------- HELPERS ----------

    float GetAxisSafe(string axis)
    {
        try
        {
            float v = Input.GetAxisRaw(axis);
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Mathf.Clamp(v, -1f, 1f);
        }
        catch
        {
            return 0f;
        }
    }

    bool IsG920Connected()
    {
        var names = Input.GetJoystickNames();
        return names.Any(n =>
            !string.IsNullOrEmpty(n) &&
            (n.ToLower().Contains("g920") || n.ToLower().Contains("logitech")));
    }

    /// <summary>
    /// Converts a pedal raw axis value (-1..1) to a pressed amount 0..1,
    /// using the calibrated restRaw as "0".
    /// 
    /// Assumes full press is toward the opposite extreme from rest:
    /// - If rest is positive (commonly ~+1), press goes toward -1.
    /// - If rest is negative (commonly ~-1), press goes toward +1.
    /// This makes it robust across different driver/build behaviors while keeping 0-at-rest.
    /// </summary>
    float PedalRawTo01(float raw, float restRaw)
    {
        raw = Mathf.Clamp(raw, -1f, 1f);
        restRaw = Mathf.Clamp(restRaw, -1f, 1f);

        // If calibration somehow didn’t happen, fall back to common assumption rest=+1.
        // (But with your reinitLockSeconds this should almost always be calibrated.)
        if (reinitLockSeconds <= 0f)
            restRaw = 1f;

        // Determine press direction based on which side of 0 the rest baseline sits.
        // rest > 0 => pressed trends toward -1
        // rest <= 0 => pressed trends toward +1
        if (restRaw > 0f)
        {
            float denom = restRaw - (-1f); // rest - min
            if (Mathf.Abs(denom) < 0.0001f) return 0f;
            float v01 = (restRaw - raw) / denom; // raw == rest => 0, raw == -1 => 1
            return Mathf.Clamp01(v01);
        }
        else
        {
            float denom = (1f - restRaw); // max - rest
            if (Mathf.Abs(denom) < 0.0001f) return 0f;
            float v01 = (raw - restRaw) / denom; // raw == rest => 0, raw == +1 => 1
            return Mathf.Clamp01(v01);
        }
    }

    float ApplyDeadzone01(float v01, float dz)
    {
        if (v01 <= dz) return 0f;
        return (v01 - dz) / (1f - dz);
    }

    float ApplyDeadzoneSymmetric(float v, float dz)
    {
        if (Mathf.Abs(v) <= dz) return 0f;
        float sign = Mathf.Sign(v);
        float mag = (Mathf.Abs(v) - dz) / (1f - dz);
        return sign * mag;
    }
}
