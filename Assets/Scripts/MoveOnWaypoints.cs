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

    private enum Phase
    {
        HiddenFollow,
        HoldAtSpawn,
        FollowAdjacentBehind,
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
                // KEY: during the hold, keep X at spawn, but ALSO keep moving forward at player speed.
                // This avoids hard-snapping Z (reduces WheelCollider jitter) while still speed-matching.
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

                    // Optional: hard-set once at hold end so Phase 2 starts nicely speed-matched.
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

                pos.z += currentSpeed * dt;
                rb.MovePosition(pos);

                if (!mergeTriggered && Time.time >= mergeTriggerTime)
                {
                    mergeTriggered = true;
                    phase = Phase.PreMergeGetLead;
                    if (turnSignal != null) turnSignal.OnMergeStarted();
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
                    phase = Phase.MergeLateral;
                else
                    currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, accel * dt);

                pos.z += currentSpeed * dt;
                rb.MovePosition(pos);
                break;
            }

            case Phase.MergeLateral:
            {
                pos.x = Mathf.MoveTowards(pos.x, mergeTargetX, mergeLateralSpeed * dt);
                pos.z += currentSpeed * dt;
                rb.MovePosition(pos);

                if (Mathf.Abs(pos.x - mergeTargetX) < 0.01f)
                {
                    phase = Phase.PostMerge;
                    if (turnSignal != null) turnSignal.OnMergeEnded();
                }
                break;
            }

            case Phase.PostMerge:
            {
                pos.z += currentSpeed * dt;
                rb.MovePosition(pos);
                break;
            }
        }
    }

    private void HiddenFollowUpdate(float dt)
    {
        if (!player) return;

        // Hidden: park exactly at spawn pose (prevents flash), wheel colliders OFF.
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

        // Keep wheel colliders OFF briefly so suspension doesn't oscillate on spawn.
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
}
