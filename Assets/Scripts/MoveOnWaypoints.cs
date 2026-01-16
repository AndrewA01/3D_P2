using System.Collections;
using UnityEngine;

public class MoveOnWaypoints : MonoBehaviour
{
    [Header("Player Reference")]
    public Transform player;
    public bool autoFindPlayerByTag = true;
    public string playerTag = "Player";

    [Header("Phase 1: Delayed Spawn")]
    public float spawnDelaySeconds = 10f;
    public float phase1SpawnX = -3.5f;

    [Tooltip("Hold at Phase 1 X for this many seconds (so you can see it spawn there).")]
    public float phase1HoldSeconds = 1.0f;

    public float spawnYawDegrees = 0f;

    [Header("Phase 2: Pre-merge (combined)")]
    [Tooltip("Adjacent lane X to hold until merge triggers.")]
    public float adjacentLaneX = -3.5f;

    [Tooltip("How fast it slides laterally to adjacent lane (units/sec).")]
    public float lateralSpeed = 2.0f;

    [Tooltip("Desired gap behind player during Phase 2 (meters).")]
    public float gapBehindMeters = 30f;

    [Tooltip("Never allow gap smaller than this when supposed to be behind.")]
    public float minGapMeters = 15f;

    [Header("Speed / Gap Control (used in Phase 2)")]
    [Tooltip("How strongly the bot corrects gap error (higher = tighter).")]
    public float gapKp = 0.8f;

    [Tooltip("How fast the bot can change speed (m/s^2).")]
    public float accel = 8.0f;

    [Tooltip("Clamp how much faster/slower than player the bot can go while correcting gap.")]
    public float maxSpeedDeltaFromPlayer = 10f;

    [Tooltip("Absolute cap for bot speed in Phase 2 (m/s).")]
    public float maxSpeed = 40f;

    [Header("Phase 3: Merge Event (feel is controlled ONLY by mergeLateralSpeed)")]
    [Tooltip("Lane center X to merge into.")]
    public float mergeTargetX = 0f;

    [Tooltip("How fast the bot moves sideways during the merge (units/sec).")]
    public float mergeLateralSpeed = 2.0f;

    [Tooltip("Final lead after merge completes (meters ahead).")]
    public float mergeLeadMeters = 12f;

    [Tooltip("Lead required BEFORE the bot starts moving sideways (meters ahead).")]
    public float mergeStartLeadMeters = 20f;

    [Tooltip("Absolute cap for bot speed during merge phases (m/s).")]
    public float mergeMaxSpeed = 45f;

    [Tooltip("How fast the bot can change speed during merge phases (m/s^2).")]
    public float mergeAccel = 10.0f;

    [Header("Post-merge behavior")]
    [Tooltip("If true, after merge completes the bot keeps moving forward independently by COASTING at its current speed (no player speed coupling).")]
    public bool coastAfterMerge = true;

    [Header("Merge visuals (steer/yaw for smoother look)")]
    [Tooltip("How many degrees the car yaws (steers) at peak during the lateral merge. Typical: 3–8.")]
    public float mergeSteerYawDegrees = 6f;

    [Tooltip("How quickly the yaw reaches its target (deg/sec). Higher = snappier, lower = smoother.")]
    public float mergeYawLerpSpeedDegPerSec = 240f;

    [Header("Runtime Status (watch these at runtime)")]
    [SerializeField] private bool spawned;
    [SerializeField] private bool mergeTriggered;

    [Header("WheelCollider / Jitter Fix")]
    public bool disableWheelCollidersUntilSpawn = true;
    public bool lockYToSpawnHeight = true;
    public float yLockLerpSpeed = 20f;

    // =========================
    // INTERNAL
    // =========================
    private enum BotPhase
    {
        NotSpawnedYet,
        Phase1_HoldAtSpawnX,
        Phase2_PreMergeCombined,
        Merge_OwnLaneOvertake,
        Merge_Lateral,
        PostMergeCoast
    }

    private BotPhase phase = BotPhase.NotSpawnedYet;

    private float currentForwardSpeed = 0f;
    private float phase1HoldUntilTime = 0f;
    private float spawnedY = 0f;

    private Coroutine spawnRoutine;
    private Coroutine mergeRoutine;

    private Renderer[] cachedRenderers;
    private Rigidbody rb;
    private WheelCollider[] wheelColliders;

    private float mergeLaneHoldX = 0f;

    // used for smooth yaw during Merge_Lateral
    private float mergeStartX = 0f;
    private float currentYaw = 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            Debug.LogError($"{nameof(MoveOnWaypoints)} requires a Rigidbody on the same GameObject.");

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        wheelColliders = GetComponentsInChildren<WheelCollider>(true);

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.useGravity = false;
            rb.isKinematic = true; // scripted motion
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
    }

    void Start()
    {
        spawned = false;
        mergeTriggered = false;
        phase = BotPhase.NotSpawnedYet;

        ResolvePlayer();

        // Keep object ACTIVE so coroutines run. Hide visuals only.
        SetRenderersEnabled(false);

        // Disable wheel colliders until spawn (prevents suspension popping while hidden)
        if (disableWheelCollidersUntilSpawn)
            SetWheelCollidersEnabled(false);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        currentYaw = spawnYawDegrees;
        ApplyYawNow(currentYaw);

        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        spawnRoutine = StartCoroutine(DelayedSpawnRoutine());
    }

    void FixedUpdate()
    {
        ResolvePlayer();
        if (player == null) return;
        if (!spawned) return;

        float dt = Time.fixedDeltaTime;
        Vector3 pos = rb.position;

        float playerSpeedAbs = GetPlayerForwardSpeedAbs();
        float playerVzSigned = GetPlayerZVelocitySigned();
        float dir = (Mathf.Abs(playerVzSigned) > 0.1f) ? Mathf.Sign(playerVzSigned) : 1f; // +1 or -1

        float targetYaw = spawnYawDegrees;

        switch (phase)
        {
            case BotPhase.Phase1_HoldAtSpawnX:
            {
                pos.x = phase1SpawnX;

                // Exact speed match in phase 1 hold
                currentForwardSpeed = playerSpeedAbs;
                pos.z += dir * currentForwardSpeed * dt;

                if (Time.time >= phase1HoldUntilTime)
                    phase = BotPhase.Phase2_PreMergeCombined;

                break;
            }

            case BotPhase.Phase2_PreMergeCombined:
            {
                pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);
                pos = ApplyGapBehindFollowZ(pos, playerSpeedAbs, dir, dt);
                break;
            }

            case BotPhase.Merge_OwnLaneOvertake:
            {
                // Keep X locked; this phase is ONLY about building enough lead
                pos.x = mergeLaneHoldX;

                // Build lead up to mergeStartLeadMeters, but do NOT brake in this phase.
                pos = ApplyLeadBuildOnlyZ(pos, playerSpeedAbs, dir, dt, mergeStartLeadMeters, kp: 0.9f, maxDelta: 8f);

                float startTargetZ = player.position.z + dir * mergeStartLeadMeters;
                bool aheadEnough = ((pos.z - startTargetZ) * dir >= 0f);
                if (aheadEnough)
                    phase = BotPhase.Merge_Lateral;

                break;
            }

            case BotPhase.Merge_Lateral:
            {
                // Sideways move (this is your "turning only" window)
                pos.x = Mathf.MoveTowards(pos.x, mergeTargetX, mergeLateralSpeed * dt);

                // COAST FORWARD during the lane change (no player speed coupling = no brake moment)
                pos.z += dir * currentForwardSpeed * dt;

                // Yaw steer look
                float totalDx = Mathf.Abs(mergeTargetX - mergeStartX);
                float remainingDx = Mathf.Abs(pos.x - mergeTargetX);

                float t = 1f;
                if (totalDx > 0.0001f)
                    t = Mathf.Clamp01(1f - (remainingDx / totalDx)); // 0->1 across merge

                float ease = Mathf.Sin(t * Mathf.PI);
                float lateralDir = Mathf.Sign(mergeTargetX - mergeStartX);
                float yawOffset = lateralDir * mergeSteerYawDegrees * ease;
                targetYaw = spawnYawDegrees + yawOffset;

                bool doneX = Mathf.Abs(pos.x - mergeTargetX) <= 0.05f;
                if (doneX)
                    phase = coastAfterMerge ? BotPhase.PostMergeCoast : BotPhase.Phase2_PreMergeCombined;

                break;
            }

            case BotPhase.PostMergeCoast:
            {
                pos.x = Mathf.MoveTowards(pos.x, mergeTargetX, mergeLateralSpeed * dt);

                // Independent coast
                pos.z += dir * currentForwardSpeed * dt;

                break;
            }
        }

        if (lockYToSpawnHeight)
            pos.y = Mathf.Lerp(pos.y, spawnedY, yLockLerpSpeed * dt);

        rb.MovePosition(pos);

        currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, mergeYawLerpSpeedDegPerSec * dt);
        ApplyYawNow(currentYaw);
    }

    // =========================
    // SPAWN
    // =========================
    private IEnumerator DelayedSpawnRoutine()
    {
        phase = BotPhase.NotSpawnedYet;
        yield return new WaitForSeconds(spawnDelaySeconds);

        float maxWaitForPlayer = 3f;
        float t = 0f;
        while (player == null && t < maxWaitForPlayer)
        {
            ResolvePlayer();
            t += Time.deltaTime;
            yield return null;
        }

        InitializeSpawnNow();
    }

    private void InitializeSpawnNow()
    {
        ResolvePlayer();

        float playerVzSigned = GetPlayerZVelocitySigned();
        float dir = (Mathf.Abs(playerVzSigned) > 0.1f) ? Mathf.Sign(playerVzSigned) : 1f;

        Vector3 pos = rb.position;
        pos.x = phase1SpawnX;

        if (player != null)
            pos.z = player.position.z - dir * gapBehindMeters;

        rb.MoveRotation(Quaternion.Euler(0f, spawnYawDegrees, 0f));

        rb.position = pos;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        spawnedY = rb.position.y;

        SetRenderersEnabled(true);

        if (disableWheelCollidersUntilSpawn)
            SetWheelCollidersEnabled(true);

        spawned = true;
        mergeTriggered = false;

        phase = BotPhase.Phase1_HoldAtSpawnX;
        phase1HoldUntilTime = Time.time + Mathf.Max(0f, phase1HoldSeconds);

        // Seed speed so it doesn't "drop back"
        currentForwardSpeed = GetPlayerForwardSpeedAbs();

        currentYaw = spawnYawDegrees;
        ApplyYawNow(currentYaw);

        BeginMergeTimerAlways();
    }

    // =========================
    // PHASE 2 GAP FOLLOW (behind player)
    // =========================
    private Vector3 ApplyGapBehindFollowZ(Vector3 pos, float playerSpeedAbs, float dir, float dt)
    {
        float targetZ = player.position.z - dir * gapBehindMeters;
        float zErrorAlongDir = (targetZ - pos.z) * dir;

        float correction = Mathf.Clamp(zErrorAlongDir * gapKp, -maxSpeedDeltaFromPlayer, maxSpeedDeltaFromPlayer);
        float desiredSpeed = playerSpeedAbs + correction;

        float actualGapAlongDir = (player.position.z - pos.z) * dir;
        if (actualGapAlongDir < minGapMeters)
            desiredSpeed = 0f;

        desiredSpeed = Mathf.Clamp(desiredSpeed, 0f, maxSpeed);

        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, accel * dt);
        pos.z += dir * currentForwardSpeed * dt;

        return pos;
    }

    // =========================
    // LEAD BUILD ONLY (Merge_OwnLaneOvertake)
    // =========================
    private Vector3 ApplyLeadBuildOnlyZ(Vector3 pos, float playerSpeedAbs, float dir, float dt, float desiredLeadMeters, float kp, float maxDelta)
    {
        float targetZ = player.position.z + dir * desiredLeadMeters;
        float zErrorAlongDir = (targetZ - pos.z) * dir;

        // Only allow positive correction (speed up). Never brake here.
        float correction = Mathf.Clamp(zErrorAlongDir * kp, 0f, maxDelta);

        float desiredSpeed = playerSpeedAbs + correction;
        desiredSpeed = Mathf.Clamp(desiredSpeed, 0f, mergeMaxSpeed);

        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, mergeAccel * dt);
        pos.z += dir * currentForwardSpeed * dt;

        return pos;
    }

    // =========================
    // MERGE TIMER (ALWAYS 15–45)
    // =========================
    private void BeginMergeTimerAlways()
    {
        float delay = Random.Range(15f, 45f);

        if (mergeRoutine != null) StopCoroutine(mergeRoutine);
        mergeRoutine = StartCoroutine(MergeAfterDelay(delay));
    }

    private IEnumerator MergeAfterDelay(float delaySeconds)
    {
        yield return new WaitForSeconds(delaySeconds);
        TriggerMergeNow();
    }

    public void TriggerMergeNow()
    {
        if (mergeRoutine != null)
        {
            StopCoroutine(mergeRoutine);
            mergeRoutine = null;
        }

        mergeTriggered = true;

        mergeLaneHoldX = rb.position.x;
        mergeStartX = mergeLaneHoldX;

        phase = BotPhase.Merge_OwnLaneOvertake;
    }

    // =========================
    // PLAYER SPEED
    // =========================
    private float GetPlayerForwardSpeedAbs()
    {
        if (player == null) return 0f;

        Rigidbody prb = player.GetComponent<Rigidbody>();
        if (prb == null) return 0f;

        return Mathf.Abs(prb.velocity.z);
    }

    private float GetPlayerZVelocitySigned()
    {
        if (player == null) return 0f;

        Rigidbody prb = player.GetComponent<Rigidbody>();
        if (prb == null) return 0f;

        return prb.velocity.z;
    }

    // =========================
    // VISUALS / WHEELS
    // =========================
    private void SetRenderersEnabled(bool enabled)
    {
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
            cachedRenderers[i].enabled = enabled;
    }

    private void SetWheelCollidersEnabled(bool enabled)
    {
        if (wheelColliders == null) return;
        for (int i = 0; i < wheelColliders.Length; i++)
            wheelColliders[i].enabled = enabled;
    }

    private void ResolvePlayer()
    {
        if (player != null) return;
        if (!autoFindPlayerByTag) return;

        GameObject p = GameObject.FindGameObjectWithTag(playerTag);
        if (p != null) player = p.transform;
    }

    private void ApplyYawNow(float yawDegrees)
    {
        if (rb == null) return;
        rb.MoveRotation(Quaternion.Euler(0f, yawDegrees, 0f));
    }
}
