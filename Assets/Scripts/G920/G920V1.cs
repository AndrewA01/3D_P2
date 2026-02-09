using UnityEngine;
using System.Linq;

/// <summary>
/// Logitech G920 input using OLD Input Manager only.
/// Works in builds if axes exist in Project Settings.
///
/// Auto-calibrates pedal rest positions on scene load.
/// Includes sensitivity tuning + mild rolling resistance.
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
    [Range(0f, 0.5f)] public float steerDeadzone = 0.001f;

    [Header("Sensitivity")]
    [Range(0.1f, 3f)] public float steeringSensitivity = 0.5f;
    [Range(0.1f, 3f)] public float throttleSensitivity = 1.0f;
    [Range(0.1f, 3f)] public float brakeSensitivity = 1.25f;

    [Header("Coast Friction / Rolling Resistance")]
    public bool enableCoastFriction = true;
    [Range(0f, 1f)] public float coastDragAdd = 0.008f;
    [Range(0f, 20f)] public float coastMinSpeedMs = 5f;

    [Header("Scene Load / Reinit Safety")]
    public float reinitLockSeconds = 0.25f;

    [Header("Debug")]
    public bool verboseLogs = true;

    public float Steer { get; private set; }
    public float Throttle { get; private set; }
    public bool G920Connected { get; private set; }

    public float ThrottlePercent { get; private set; }
    public float BrakePercent { get; private set; }

    float _lockTimer;
    float _throttleRestRaw, _brakeRestRaw;
    float _throttleSum, _brakeSum;
    int _calibSamples;

    Rigidbody _rb;
    float _baseDrag;

    void Reset()
    {
        steerAxis = "Horizontal";
        throttleAxis = "Throttle";
        brakeAxis = "Brake";

        keyForward = KeyCode.W;
        keyBack = KeyCode.S;
        keyLeft = KeyCode.A;
        keyRight = KeyCode.D;

        pedalDeadzone = 0.1f;
        steerDeadzone = 0.001f;

        steeringSensitivity = 0.5f;
        throttleSensitivity = 1.0f;
        brakeSensitivity = 1.25f;

        enableCoastFriction = true;
        coastDragAdd = 0.008f;
        coastMinSpeedMs = 5f;

        reinitLockSeconds = 0.25f;
        verboseLogs = true;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb) _baseDrag = _rb.drag;
    }

    void OnEnable()
    {
        Input.ResetInputAxes();
        _lockTimer = reinitLockSeconds;

        _throttleSum = 0f;
        _brakeSum = 0f;
        _calibSamples = 0;

        Steer = Throttle = 0f;
        ThrottlePercent = BrakePercent = 0f;

        if (_rb) _baseDrag = _rb.drag;
    }

    void OnDisable()
    {
        if (_rb) _rb.drag = _baseDrag;
    }

    void Update()
    {
        Steer = Throttle = 0f;
        ThrottlePercent = BrakePercent = 0f;

        G920Connected = IsG920Connected();

        if (_lockTimer > 0f)
        {
            _lockTimer -= Time.unscaledDeltaTime;

            _throttleSum += GetAxisSafe(throttleAxis);
            _brakeSum += GetAxisSafe(brakeAxis);
            _calibSamples++;

            if (_lockTimer <= 0f && _calibSamples > 0)
            {
                _throttleRestRaw = _throttleSum / _calibSamples;
                _brakeRestRaw = _brakeSum / _calibSamples;
            }
            return;
        }

        float keyboardSteer = (Input.GetKey(keyRight) ? 1 : 0) - (Input.GetKey(keyLeft) ? 1 : 0);
        float wheelSteer = ApplyDeadzoneSymmetric(GetAxisSafe(steerAxis), steerDeadzone);
        Steer = Mathf.Clamp((Mathf.Abs(wheelSteer) > 0.001f ? wheelSteer : keyboardSteer) * steeringSensitivity, -1f, 1f);

        float keyboardThrottle = (Input.GetKey(keyForward) ? 1 : 0) - (Input.GetKey(keyBack) ? 1 : 0);

        float throttle01 = ApplyDeadzone01(PedalRawTo01(GetAxisSafe(throttleAxis), _throttleRestRaw), pedalDeadzone);
        float brake01 = ApplyDeadzone01(PedalRawTo01(GetAxisSafe(brakeAxis), _brakeRestRaw), pedalDeadzone);

        throttle01 = Mathf.Clamp01(throttle01 * throttleSensitivity);
        brake01 = Mathf.Clamp01(brake01 * brakeSensitivity);

        ThrottlePercent = throttle01 * 100f;
        BrakePercent = brake01 * 100f;

        float wheelThrottle = throttle01 - brake01;
        Throttle = Mathf.Abs(keyboardThrottle) > 0.01f ? keyboardThrottle : wheelThrottle;

        if (G920Connected && Mathf.Abs(Throttle) < pedalDeadzone)
            Throttle = 0f;

        Throttle = Mathf.Clamp(Throttle, -1f, 1f);

        ApplyCoastDrag(throttle01, brake01, keyboardThrottle);

        if (verboseLogs)
            Debug.Log($"[G920V1] Steer:{Steer:F2} Thr:{Throttle:F2} Thr%:{ThrottlePercent:F0} Brk%:{BrakePercent:F0}");
    }

    void ApplyCoastDrag(float throttle01, float brake01, float keyboardThrottle)
    {
        if (!_rb || !enableCoastFriction) return;

        bool coasting = throttle01 < 0.001f && brake01 < 0.001f && Mathf.Abs(keyboardThrottle) < 0.01f;
        float targetDrag = (coasting && _rb.velocity.magnitude >= coastMinSpeedMs)
            ? _baseDrag + coastDragAdd
            : _baseDrag;

        _rb.drag = Mathf.MoveTowards(_rb.drag, targetDrag, 5f * Time.unscaledDeltaTime);
    }

    float GetAxisSafe(string axis)
    {
        try { return Mathf.Clamp(Input.GetAxisRaw(axis), -1f, 1f); }
        catch { return 0f; }
    }

    bool IsG920Connected()
    {
        return Input.GetJoystickNames().Any(n =>
            !string.IsNullOrEmpty(n) &&
            (n.ToLower().Contains("g920") || n.ToLower().Contains("logitech")));
    }

    float PedalRawTo01(float raw, float rest)
    {
        if (rest > 0f) return Mathf.Clamp01((rest - raw) / (rest + 1f));
        else return Mathf.Clamp01((raw - rest) / (1f - rest));
    }

    float ApplyDeadzone01(float v, float dz)
    {
        return v <= dz ? 0f : (v - dz) / (1f - dz);
    }

    float ApplyDeadzoneSymmetric(float v, float dz)
    {
        if (Mathf.Abs(v) <= dz) return 0f;
        return Mathf.Sign(v) * (Mathf.Abs(v) - dz) / (1f - dz);
    }
}
