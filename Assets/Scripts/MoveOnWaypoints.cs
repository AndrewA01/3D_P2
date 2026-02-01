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

    [Header("Phase 2: Pre-merge (combined)")]
    public float adjacentLaneX = 5.27f;
    public float lateralSpeed = 3f;
    public float gapBehindMeters = 7f;
    public float minGapMeters = 4f;

    [Header("Speed / Gap Control (used in Phase 2)")]
    public float gapKp = 0.8f;
    public float accel = 3f;
    public float maxSpeedDeltaFromPlayer = 10f;
    public float maxSpeed = 75f;

    [Header("Phase 3: Merge Event (feel is controlled ONLY by mergeLateralSpeed)")]
    public float mergeTargetX = 0f;
    public float mergeLateralSpeed = 1f;
    public float mergeLeadMeters = 10f;        // kept for inspector compatibility
    public float mergeStartLeadMeters = 10f;

    [Header("Turn Signal (optional)")]
    public TurnSignalBlinkerSimpleV2 turnSignal;

    // -------- internal --------
    private Rigidbody rb;
    private bool spawned;
    private float spawnTimer;
    private float holdTimer;

    // Random merge trigger (15–45s after spawn becomes visible)
    private float mergeTriggerTime;
    private bool mergeTriggered;

    private float currentSpeed;

    // Hide until spawn
    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;

    // Player speed fallback
    private Vector3 lastPlayerPos;
    private bool hasLastPlayerPos;

    private enum Phase
    {
        HiddenFollow,        // invisible but following
        HoldAtSpawn,         // visible hold at phase1SpawnX
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

        // While hidden, we move it kinematically (no physics)
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);

        SetVisible(false);
        SetCollidersEnabled(false);

        ResolvePlayer();
        if (player != null)
        {
            lastPlayerPos = player.position;
            hasLastPlayerPos = true;
        }
    }

    void FixedUpdate()
    {
        ResolvePlayer();
        float dt = Time.fixedDeltaTime;

        // Phase: invisible but actively following at the correct gap
        if (!spawned)
        {
            HiddenFollowUpdate(dt);

            spawnTimer += dt;
            if (spawnTimer >= spawnDelaySeconds)
            {
                SpawnVisibleNow();   // just “reveals” it where it already is
            }
            return;
        }

        // After spawn: normal phase machine
        Vector3 pos = rb.position;

        switch (phase)
        {
            case Phase.HoldAtSpawn:
            {
                holdTimer += dt;

                // hold X (you wanted this phase)
                pos.x = phase1SpawnX;
                rb.MovePosition(pos);

                if (holdTimer >= phase1HoldSeconds)
                    phase = Phase.FollowAdjacentBehind;

                break;
            }

            case Phase.FollowAdjacentBehind:
            {
                if (!player) break;

                pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

                float playerZ = player.position.z;
                float gap = playerZ - pos.z;

                float error = gap - gapBehindMeters;
                if (gap < minGapMeters) error = gap - minGapMeters;

                float playerSpeed = GetPlayerForwardSpeed(dt);

                float targetSpeed = playerSpeed + error * gapKp;
                targetSpeed = Mathf.Clamp(
                    targetSpeed,
                    playerSpeed - maxSpeedDeltaFromPlayer,
                    playerSpeed + maxSpeedDeltaFromPlayer
                );
                targetSpeed = Mathf.Clamp(targetSpeed, 0f, maxSpeed);

                currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, accel * dt);

                pos.z += currentSpeed * dt;
                rb.MovePosition(pos);

                // random merge trigger 15–45s
                if (!mergeTriggered && Time.time >= mergeTriggerTime)
                {
                    mergeTriggered = true;
                    phase = Phase.PreMergeGetLead;
                    if (turnSignal) turnSignal.OnMergeStarted();
                }
                break;
            }

            case Phase.PreMergeGetLead:
            {
                if (!player) break;

                // stay adjacent while building lead
                pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

                float playerZ = player.position.z;
                float lead = pos.z - playerZ;

                float playerSpeed = GetPlayerForwardSpeed(dt);
                float desiredSpeed = Mathf.Min(maxSpeed, playerSpeed + maxSpeedDeltaFromPlayer);

                if (lead >= mergeStartLeadMeters)
                {
                    phase = Phase.MergeLateral;
                }
                else
                {
                    currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, accel * dt);
                }

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
                    if (turnSignal) turnSignal.OnMergeEnded();
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

    // --------- KEY: hidden follow logic ----------
    private void HiddenFollowUpdate(float dt)
    {
        if (!player) return;

        // While hidden: directly place bot to be exactly gapBehindMeters behind player.
        Vector3 pos = rb.position;

        // Follow adjacent lane X smoothly (so left/right variants still work)
        pos.x = Mathf.MoveTowards(pos.x, adjacentLaneX, lateralSpeed * dt);

        float desiredZ = player.position.z - gapBehindMeters;
        pos.z = desiredZ; // hard set so it NEVER falls behind while hidden

        // Keep current Y as-is (prevents “in-ground” from physics since kinematic).
        rb.MovePosition(pos);

        // Track speed so visible phase starts matching player (no catch-up sprint)
        float playerSpeed = GetPlayerForwardSpeed(dt);
        currentSpeed = Mathf.Clamp(playerSpeed, 0f, maxSpeed);
    }

    private void SpawnVisibleNow()
    {
        spawned = true;

        // Reveal visuals/colliders at the current (already-correct) position
        SetVisible(true);
        SetCollidersEnabled(true);

        // Keep kinematic ON (we are controlling with MovePosition anyway)
        rb.isKinematic = true;

        rb.MoveRotation(Quaternion.Euler(0f, spawnYawDegrees, 0f));

        // Start hold phase (your phase 1)
        holdTimer = 0f;
        phase = Phase.HoldAtSpawn;

        // Start random merge timer now (15–45 seconds after visible spawn)
        mergeTriggerTime = Time.time + Random.Range(15f, 45f);
        mergeTriggered = false;
    }

    private void ResolvePlayer()
    {
        if (player != null) return;
        if (!autoFindPlayerByTag) return;

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
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
            if (cachedRenderers[i] != null) cachedRenderers[i].enabled = visible;
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (cachedColliders == null) return;
        for (int i = 0; i < cachedColliders.Length; i++)
            if (cachedColliders[i] != null) cachedColliders[i].enabled = enabled;
    }
}
