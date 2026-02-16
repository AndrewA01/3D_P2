using UnityEngine;

public class MoveOnWaypoints : MonoBehaviour
{
    [Header("Player Reference")]
    public Transform player;
    public bool autoFindPlayerByTag = true;
    public string playerTag = "Player";

    [Header("Phase 1: Delayed Spawn")]
    public float spawnDelaySeconds = 10f;
    public float phase1SpawnX = 10f;
    public float phase1HoldSeconds = 1f;
    public float spawnYawDegrees = 0f;

    [Tooltip("How far behind the player the bot should be when it FIRST becomes visible (Phase 1).")]
    public float spawnGapBehindMeters = 7f;

    [Header("Phase 2: Adjacent Lane Follow")]
    public float adjacentLaneX = 5.27f;
    public float lateralSpeed = 3f;
    public float gapBehindMeters = 7f;
    public float minGapMeters = 4f;

    [Header("Speed / Gap Control (used in Phase 2)")]
    public float gapKp = 0.8f;
    public float accel = 3f;
    public float maxSpeedDeltaFromPlayer = 10f;
    public float maxSpeed = 75f;

    [Header("Phase 3: Merge (lateral)")]
    public float mergeTargetX = 0f;
    public float mergeLateralSpeed = 1f;
    public float mergeStartLeadMeters = 10f;

    [Header("Turn Signal (optional)")]
    public TurnSignalBlinkerSimpleV2 turnSignal;

    [Header("WheelCollider Stability (recommended if bot has WheelColliders)")]
    [Tooltip("If true, wheel colliders will be disabled while hidden + during the brief hold to prevent suspension jitter, then re-enabled.")]
    public bool disableWheelCollidersUntilHoldEnds = true;

    [Header("Turn Signal Lead Time (NEW)")]
    [Tooltip("Seconds to keep the turn signal visible BEFORE Car 2 starts accelerating / begins the merge sequence.")]
    [SerializeField] private float signalLeadTimeSeconds = 0.5f;

    [Header("Collision Avoidance / Deceleration")]
    [Tooltip("If enabled, Car 2 will decelerate in a controlled way to support collision avoidance measures.")]
    [SerializeField] private bool enableControlledDecel = true;

    [Tooltip("When to begin deceleration relative to merge trigger.")]
    [SerializeField] private DecelStartMode decelStartMode = DecelStartMode.AfterMergeTriggerDelay;

    [Tooltip("Delay after merge trigger before deceleration begins (seconds). Used when DecelStartMode = AfterMergeTriggerDelay.")]
    [SerializeField] private float decelDelayAfterMergeTriggerSeconds = 0.5f;

    [Tooltip("Deceleration rate (m/s^2). Higher = stronger braking.")]
    [SerializeField] private float decelRateMs2 = 4.0f;

    [Tooltip("Minimum forward speed (m/s) once deceleration is active. Use 0 to brake to stop.")]
    [SerializeField] private float decelMinSpeedMs = 13.411f;

    [Tooltip("If true, deceleration will only be applied during MergeLateral and PostMerge phases. If false, it can occur earlier (based on start mode).")]
    [SerializeField] private bool restrictDecelToMergeAndAfter = true;

    public enum DecelStartMode
    {
        OnMergeTriggerInstant,
        AfterMergeTriggerDelay,
        OnMergeLateralStart,
        OnPostMergeStart
    }

    // =========================
    // Merge Trigger Export (for DataRecorder)
    // NOTE: Now means *LATERAL MERGE START* (Phase.MergeLateral entry)
    // =========================
    [Header("Merge Trigger Export (for DataRecorder)")]
    [SerializeField] private bool hasMergeStarted = false;
    [SerializeField] private float mergeStartAbs = -1f;

    public bool HasMergeStarted => hasMergeStarted;
    public float MergeStartAbs => mergeStartAbs;

    // =========================
    // Decel / Collision Export (for DataRecorder)
    // =========================
    [Header("Decel / Collision Export (for DataRecorder)")]
    [SerializeField] private bool hasDecelStarted = false;
    [SerializeField] private float decelStartAbs = -1f;

    [SerializeField] private bool hasCollided = false;
    [SerializeField] private float collisionAbs = -1f;

    public bool HasDecelStarted => hasDecelStarted;
    public float DecelStartAbs => decelStartAbs;

    public bool HasCollided => hasCollided;
    public float CollisionAbs => collisionAbs;

    private Rigidbody rb;
    private bool spawned;
    private float spawnTimer;
    private float holdTimer;

    private float mergeTriggerTime;
    private bool mergeTriggered;

    private float currentSpeed;

    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;
    private WheelCollider[] wheelColliders;

    private Vector3 lastPlayerPos;
    private bool hasLastPlayerPos;

    private bool decelScheduled = false;
    private float decelScheduledAbs = -1f;

    private float signalLeadStartAbs = -1f;

    private enum Phase
    {
        HiddenFollow,
        HoldAtSpawn,
        FollowAdjacentBehind,
        SignalLeadHold,
        PreMergeGetLead,
        MergeLateral,
        PostMerge
    }

    private Phase phase = Phase.HiddenFollow;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);
        wheelColliders = GetComponentsInChildren<WheelCollider>(true);

        SetVisible(false);
        SetCollidersEnabled(false);
        SetWheelCollidersEnabled(false);

        ResolvePlayer();
        if (player != null)
        {
            lastPlayerPos = player.position;
            hasLastPlayerPos = true;
        }

        if (turnSignal == null)
        {
            turnSignal = GetComponent<TurnSignalBlinkerSimpleV2>()
                      ?? GetComponentInChildren<TurnSignalBlinkerSimpleV2>(true)
                      ?? GetComponentInParent<TurnSignalBlinkerSimpleV2>(true);
        }

        // IMPORTANT: hard reset exported fields in case prefab was saved dirty
        ResetExports();
    }

    void OnEnable()
    {
        // Also reset on enable (extra safety)
        ResetExports();
    }

    private void ResetExports()
    {
        hasMergeStarted = false;
        mergeStartAbs = -1f;

        hasDecelStarted = false;
        decelStartAbs = -1f;

        hasCollided = false;
        collisionAbs = -1f;

        decelScheduled = false;
        decelScheduledAbs = -1f;
    }

    void FixedUpdate()
    {
        ResolvePlayer();
        float dt = Time.fixedDeltaTime;

        if (!spawned)
        {
            HiddenFollowUpdate(dt);

            spawnTimer += dt;
            if (spawnTimer >= spawnDelaySeconds)
                SpawnVisibleNow();

            return;
        }

        Vector3 pos = rb.position;

        switch (phase)
        {
            case Phase.HoldAtSpawn:
                {
                    float playerSpeed = GetPlayerForwardSpeed(dt);
                    playerSpeed = Mathf.Clamp(playerSpeed, 0f, maxSpeed);

                    currentSpeed = Mathf.MoveTowards(currentSpeed, playerSpeed, accel * dt);

                    pos.x = phase1SpawnX;
                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);

                    holdTimer += dt;
                    if (holdTimer >= phase1HoldSeconds)
                    {
                        if (disableWheelCollidersUntilHoldEnds) SetWheelCollidersEnabled(true);
                        currentSpeed = Mathf.Clamp(GetPlayerForwardSpeed(dt), 0f, maxSpeed);
                        phase = Phase.FollowAdjacentBehind;
                    }
                    break;
                }

            case Phase.FollowAdjacentBehind:
                {
                    if (!player) break;

                    pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

                    float gap = player.position.z - pos.z;
                    float error = gap - gapBehindMeters;
                    if (gap < minGapMeters) error = gap - minGapMeters;

                    float playerSpeed = Mathf.Clamp(GetPlayerForwardSpeed(dt), 0f, maxSpeed);

                    float targetSpeed = Mathf.Clamp(
                        playerSpeed + error * gapKp,
                        playerSpeed - maxSpeedDeltaFromPlayer,
                        playerSpeed + maxSpeedDeltaFromPlayer
                    );
                    targetSpeed = Mathf.Clamp(targetSpeed, 0f, maxSpeed);

                    currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * dt);

                    ApplyControlledDecelIfActive(dt);

                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);

                    if (!mergeTriggered && Time.time >= mergeTriggerTime)
                    {
                        // Merge trigger moment (signal becomes visible)
                        mergeTriggered = true;

                        if (turnSignal != null) turnSignal.OnMergeStarted();

                        SetupDecelSchedulingOnMergeTrigger();

                        signalLeadStartAbs = Time.realtimeSinceStartup;
                        phase = Phase.SignalLeadHold;
                    }
                    break;
                }

            case Phase.SignalLeadHold:
                {
                    if (!player) break;

                    pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

                    float gap = player.position.z - pos.z;
                    float error = gap - gapBehindMeters;
                    if (gap < minGapMeters) error = gap - minGapMeters;

                    float playerSpeed = Mathf.Clamp(GetPlayerForwardSpeed(dt), 0f, maxSpeed);

                    float targetSpeed = Mathf.Clamp(
                        playerSpeed + error * gapKp,
                        playerSpeed - maxSpeedDeltaFromPlayer,
                        playerSpeed + maxSpeedDeltaFromPlayer
                    );
                    targetSpeed = Mathf.Clamp(targetSpeed, 0f, maxSpeed);

                    currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * dt);

                    ApplyControlledDecelIfActive(dt);

                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);

                    float leadTime = Mathf.Max(0f, signalLeadTimeSeconds);
                    if (leadTime <= 0f || (Time.realtimeSinceStartup - signalLeadStartAbs) >= leadTime)
                    {
                        phase = Phase.PreMergeGetLead;
                    }
                    break;
                }

            case Phase.PreMergeGetLead:
                {
                    if (!player) break;

                    pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

                    float lead = pos.z - player.position.z;
                    float desiredSpeed = Mathf.Min(maxSpeed,
                        Mathf.Clamp(GetPlayerForwardSpeed(dt), 0f, maxSpeed) + maxSpeedDeltaFromPlayer);

                    if (lead >= mergeStartLeadMeters)
                    {
                        // IMPORTANT: lateral merge starts NOW
                        phase = Phase.MergeLateral;

                        if (!hasMergeStarted)
                        {
                            hasMergeStarted = true;
                            mergeStartAbs = Time.realtimeSinceStartup;
                        }

                        if (enableControlledDecel && decelStartMode == DecelStartMode.OnMergeLateralStart)
                            StartDecelNowIfNotStarted();
                    }
                    else
                    {
                        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, accel * dt);
                    }

                    ApplyControlledDecelIfActive(dt);

                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);
                    break;
                }

            case Phase.MergeLateral:
                {
                    pos.x = Mathf.MoveTowards(pos.x, mergeTargetX, mergeLateralSpeed * dt);

                    ApplyControlledDecelIfActive(dt);

                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);

                    if (Mathf.Abs(pos.x - mergeTargetX) < 0.01f)
                    {
                        phase = Phase.PostMerge;
                        if (turnSignal != null) turnSignal.OnMergeEnded();

                        if (enableControlledDecel && decelStartMode == DecelStartMode.OnPostMergeStart)
                            StartDecelNowIfNotStarted();
                    }
                    break;
                }

            case Phase.PostMerge:
                {
                    ApplyControlledDecelIfActive(dt);

                    pos.z += currentSpeed * dt;
                    rb.MovePosition(pos);
                    break;
                }
        }
    }

    private void HiddenFollowUpdate(float dt)
    {
        if (!player) return;

        Vector3 pos = rb.position;
        pos.x = phase1SpawnX;
        pos.z = player.position.z - spawnGapBehindMeters;

        rb.position = pos;
        rb.rotation = Quaternion.Euler(0f, spawnYawDegrees, 0f);

        currentSpeed = Mathf.Clamp(GetPlayerForwardSpeed(dt), 0f, maxSpeed);
    }

    private void SpawnVisibleNow()
    {
        spawned = true;

        ResetExports();

        // Reset signal lead timing
        signalLeadStartAbs = -1f;

        if (player != null)
        {
            Vector3 pos = rb.position;
            pos.x = phase1SpawnX;
            pos.z = player.position.z - spawnGapBehindMeters;

            rb.position = pos;
            rb.rotation = Quaternion.Euler(0f, spawnYawDegrees, 0f);

            currentSpeed = Mathf.Clamp(GetPlayerForwardSpeed(Time.fixedDeltaTime), 0f, maxSpeed);
        }

        Physics.SyncTransforms();

        SetVisible(true);
        SetCollidersEnabled(true);

        if (!disableWheelCollidersUntilHoldEnds) SetWheelCollidersEnabled(true);

        holdTimer = 0f;
        phase = Phase.HoldAtSpawn;

        mergeTriggerTime = Time.time + Random.Range(15f, 45f);
        mergeTriggered = false;
    }

    private void ResolvePlayer()
    {
        if (player != null || !autoFindPlayerByTag) return;
        GameObject p = GameObject.FindGameObjectWithTag(playerTag);
        if (p) player = p.transform;
    }

    private float GetPlayerForwardSpeed(float dt)
    {
        if (player == null || dt <= 0f) return 0f;

        Rigidbody prb = player.GetComponent<Rigidbody>();
        if (prb != null) return prb.velocity.z;

        if (!hasLastPlayerPos)
        {
            lastPlayerPos = player.position;
            hasLastPlayerPos = true;
            return 0f;
        }

        float dz = player.position.z - lastPlayerPos.z;
        lastPlayerPos = player.position;
        return dz / dt;
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in cachedRenderers)
            if (r != null) r.enabled = visible;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (var c in cachedColliders)
            if (c != null) c.enabled = enabled;
    }

    private void SetWheelCollidersEnabled(bool enabled)
    {
        if (wheelColliders == null) return;
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (wheelColliders[i] != null)
                wheelColliders[i].enabled = enabled;
        }
    }

    // =========================
    // Deceleration behavior
    // =========================
    private void SetupDecelSchedulingOnMergeTrigger()
    {
        if (!enableControlledDecel) return;

        if (decelStartMode == DecelStartMode.OnMergeTriggerInstant)
        {
            StartDecelNowIfNotStarted();
            return;
        }

        if (decelStartMode == DecelStartMode.AfterMergeTriggerDelay)
        {
            decelScheduled = true;
            decelScheduledAbs = Time.realtimeSinceStartup + Mathf.Max(0f, decelDelayAfterMergeTriggerSeconds);
        }
    }

    private void StartDecelNowIfNotStarted()
    {
        if (!enableControlledDecel) return;
        if (hasDecelStarted) return;

        hasDecelStarted = true;
        decelStartAbs = Time.realtimeSinceStartup;
    }

    private bool IsDecelAllowedInCurrentPhase()
    {
        if (!restrictDecelToMergeAndAfter) return true;
        return (phase == Phase.MergeLateral || phase == Phase.PostMerge);
    }

    private void ApplyControlledDecelIfActive(float dt)
    {
        if (!enableControlledDecel) return;

        if (!hasDecelStarted && decelScheduled && decelScheduledAbs > 0f)
        {
            if (Time.realtimeSinceStartup >= decelScheduledAbs)
                StartDecelNowIfNotStarted();
        }

        if (!hasDecelStarted) return;
        if (!IsDecelAllowedInCurrentPhase()) return;

        float decelRate = Mathf.Max(0f, decelRateMs2);
        float minSpeed = Mathf.Max(0f, decelMinSpeedMs);

        currentSpeed = Mathf.MoveTowards(currentSpeed, minSpeed, decelRate * dt);
    }

    // =========================
    // Collision detection: ONLY player-car collision, first impact only
    // =========================
    private void OnCollisionEnter(Collision collision)
    {
        if (hasCollided) return;
        if (!spawned) return;

        if (!IsPlayerCollision(collision)) return;

        hasCollided = true;
        collisionAbs = Time.realtimeSinceStartup;
    }

    private bool IsPlayerCollision(Collision collision)
    {
        if (collision == null) return false;

        // Prefer explicit player reference
        if (player != null)
        {
            Transform otherRoot = collision.transform != null ? collision.transform.root : null;
            Transform playerRoot = player.root;

            if (otherRoot == playerRoot) return true;
        }

        // Fallback: tag check
        if (!string.IsNullOrWhiteSpace(playerTag))
        {
            if (collision.collider != null && collision.collider.CompareTag(playerTag)) return true;
            if (collision.gameObject != null && collision.gameObject.CompareTag(playerTag)) return true;
        }

        return false;
    }
}
