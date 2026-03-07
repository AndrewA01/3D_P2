using UnityEngine;
using System.Linq;

/// <summary>
/// Logitech G920 input using OLD Input Manager only.
/// Works in builds if axes exist in Project Settings.
///
/// Auto-calibrates pedal rest positions on scene load.
/// Includes sensitivity tuning + mild rolling resistance.
/// Also includes optional post-collision stability assist (recommended when rotation X/Z are unfrozen).
///
/// IMPORTANT: This script does NOT change any Input axis names used by other scripts.
/// DataRecorderV2 can continue using Input.GetAxisRaw("Horizontal"/"Throttle"/"Brake") unchanged.
/// </summary>
[DisallowMultipleComponent]
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
    [Range(0.1f, 10f)] public float brakeSensitivity = 6f;

    [Header("Coast Friction / Rolling Resistance")]
    public bool enableCoastFriction = true;
    [Range(0f, 1f)] public float coastDragAdd = 0.008f;
    [Range(0f, 20f)] public float coastMinSpeedMs = 5f;
    [Tooltip("How quickly drag changes to target. Higher = snappier.")]
    [Range(0.1f, 30f)] public float coastDragLerpSpeed = 8f;

    [Header("Scene Load / Reinit Safety")]
    public float reinitLockSeconds = 0.25f;

    [Header("Stability Assist (recommended if you unfreeze rotation X/Z)")]
    public bool enableStabilityAssist = true;

    [Tooltip("Max allowed angular velocity (rad/s). Lower = less spinning after impacts.")]
    [Range(1f, 20f)] public float maxAngularVelocity = 6f;

    [Tooltip("Extra angular drag applied for a short time after a big collision.")]
    [Range(0f, 10f)] public float crashExtraAngularDrag = 2.5f;

    [Tooltip("How long the extra damping lasts after a crash (seconds).")]
    [Range(0f, 5f)] public float crashAssistSeconds = 1.0f;

    [Tooltip("Impulse magnitude threshold to consider something a crash.")]
    [Range(0f, 50f)] public float crashImpulseThreshold = 8f;

    [Tooltip("Damping applied directly to roll/pitch angular velocity during crash assist.")]
    [Range(0f, 30f)] public float rollPitchDamp = 10f;

    [Tooltip("Damping applied to yaw angular velocity during crash assist.")]
    [Range(0f, 30f)] public float yawDamp = 2.5f;

    [Header("Debug")]
    public bool verboseLogs = false;

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
    float _baseAngularDrag;

    // Cached values from Update (input) for physics usage in FixedUpdate
    float _cachedThrottle01;
    float _cachedBrake01;
    float _cachedKeyboardThrottle;

    // Crash assist timer
    float _crashAssistTimer;

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
        brakeSensitivity = 6f;

        enableCoastFriction = true;
        coastDragAdd = 0.008f;
        coastMinSpeedMs = 5f;
        coastDragLerpSpeed = 8f;

        reinitLockSeconds = 0.25f;

        enableStabilityAssist = true;
        maxAngularVelocity = 6f;
        crashExtraAngularDrag = 2.5f;
        crashAssistSeconds = 1.0f;
        crashImpulseThreshold = 8f;
        rollPitchDamp = 10f;
        yawDamp = 2.5f;

        verboseLogs = false;
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb)
        {
            _baseDrag = _rb.drag;
            _baseAngularDrag = _rb.angularDrag;

            // Ensure this is set (Unity may clamp internally; we re-apply in FixedUpdate too)
            _rb.maxAngularVelocity = maxAngularVelocity;
        }
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

        _cachedThrottle01 = 0f;
        _cachedBrake01 = 0f;
        _cachedKeyboardThrottle = 0f;

        _crashAssistTimer = 0f;

        if (_rb)
        {
            _baseDrag = _rb.drag;
            _baseAngularDrag = _rb.angularDrag;
        }
    }

    void OnDisable()
    {
        if (_rb)
        {
            _rb.drag = _baseDrag;
            _rb.angularDrag = _baseAngularDrag;
        }
    }

    void Update()
    {
        // Reset outputs every frame
        Steer = Throttle = 0f;
        ThrottlePercent = BrakePercent = 0f;

        G920Connected = IsG920Connected();

        // Calibration lock: learn pedal rest positions for first moments after enabling
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

            // Cache "no input" for physics step
            _cachedThrottle01 = 0f;
            _cachedBrake01 = 0f;
            _cachedKeyboardThrottle = 0f;

            return;
        }

        // Steering: wheel takes precedence if moved; otherwise keyboard
        float keyboardSteer = (Input.GetKey(keyRight) ? 1f : 0f) - (Input.GetKey(keyLeft) ? 1f : 0f);
        float wheelSteer = ApplyDeadzoneSymmetric(GetAxisSafe(steerAxis), steerDeadzone);
        Steer = Mathf.Clamp((Mathf.Abs(wheelSteer) > 0.001f ? wheelSteer : keyboardSteer) * steeringSensitivity, -1f, 1f);

        // Throttle/brake: wheel pedals -> combined, with keyboard fallback
        float keyboardThrottle = (Input.GetKey(keyForward) ? 1f : 0f) - (Input.GetKey(keyBack) ? 1f : 0f);

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

        // Cache for FixedUpdate (physics)
        _cachedThrottle01 = throttle01;
        _cachedBrake01 = brake01;
        _cachedKeyboardThrottle = keyboardThrottle;

        if (verboseLogs)
            Debug.Log($"[G920V1] Steer:{Steer:F2} Thr:{Throttle:F2} Thr%:{ThrottlePercent:F0} Brk%:{BrakePercent:F0}");
    }

    void FixedUpdate()
    {
        if (!_rb) return;

        // Keep max angular velocity applied
        _rb.maxAngularVelocity = maxAngularVelocity;

        // Apply coast drag in physics step (prevents render-frame jitter)
        ApplyCoastDragFixed(_cachedThrottle01, _cachedBrake01, _cachedKeyboardThrottle);

        // Apply post-crash stability assist (optional)
        if (enableStabilityAssist)
            ApplyCrashAssistFixed();
    }

    void OnCollisionEnter(Collision c)
    {
        if (!enableStabilityAssist) return;

        // impulse is a decent proxy for "how hard was the hit?"
        float impulse = c.impulse.magnitude;
        if (impulse >= crashImpulseThreshold)
        {
            _crashAssistTimer = crashAssistSeconds;
        }
    }

    void ApplyCoastDragFixed(float throttle01, float brake01, float keyboardThrottle)
    {
        if (!enableCoastFriction) return;

        bool coasting = throttle01 < 0.001f && brake01 < 0.001f && Mathf.Abs(keyboardThrottle) < 0.01f;

        float targetDrag =
            (coasting && _rb.velocity.magnitude >= coastMinSpeedMs)
                ? _baseDrag + coastDragAdd
                : _baseDrag;

        _rb.drag = Mathf.MoveTowards(_rb.drag, targetDrag, coastDragLerpSpeed * Time.fixedDeltaTime);
    }

    void ApplyCrashAssistFixed()
    {
        if (_crashAssistTimer > 0f)
        {
            _crashAssistTimer -= Time.fixedDeltaTime;

            // Temporarily increase angular drag to quickly settle after crash
            float targetAngDrag = _baseAngularDrag + crashExtraAngularDrag;
            _rb.angularDrag = Mathf.MoveTowards(_rb.angularDrag, targetAngDrag, 10f * Time.fixedDeltaTime);

            // Dampen roll/pitch more than yaw
            Vector3 av = _rb.angularVelocity;
            av.x = Mathf.Lerp(av.x, 0f, rollPitchDamp * Time.fixedDeltaTime);
            av.z = Mathf.Lerp(av.z, 0f, rollPitchDamp * Time.fixedDeltaTime);
            av.y = Mathf.Lerp(av.y, 0f, yawDamp * Time.fixedDeltaTime);
            _rb.angularVelocity = av;
        }
        else
        {
            // Return to baseline angular drag smoothly
            _rb.angularDrag = Mathf.MoveTowards(_rb.angularDrag, _baseAngularDrag, 5f * Time.fixedDeltaTime);
        }
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