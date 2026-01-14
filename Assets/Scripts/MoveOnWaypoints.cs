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

    [Header("Pre-merge: Z gap behind player (until merge triggers)")]
    public float preMergeGapBehindMeters = 30f;
    public float minGapMeters = 15f;

    [Tooltip("How strongly the bot corrects gap error (pre-merge).")]
    public float gapKp = 0.8f;

    [Tooltip("How fast the bot can change speed (m/s^2) pre-merge.")]
    public float accel = 8.0f;

    [Tooltip("Clamp how much faster/slower than player the bot is allowed to go while correcting gap.")]
    public float maxSpeedDeltaFromPlayer = 10f;

    [Tooltip("Absolute cap for bot speed pre-merge.")]
    public float maxSpeed = 40f;

    [Header("Phase 2: Move to X (while holding Z gap)")]
    public float phase2TargetX = -3.5f;
    public float phase2LateralSpeed = 2.0f;
    public float phase2FinishTolX = 0.05f;

    [Header("Phase 3: Merge Event (always random 15–45s after spawn)")]
    public float mergeTargetX = 0f;
    public float mergeLateralSpeed = 2.0f;

    public float mergeLeadMeters = 12f;
    public float mergeMaxSpeed = 45f;
    public float mergeAccel = 10.0f;

    public float mergeFinishTolX = 0.1f;
    public float mergeFinishTolZ = 1.0f;

    [Header("After merge")]
    public bool keepLeadAfterMerge = true;

    [Header("Runtime Status (watch these at runtime)")]
    [SerializeField] private bool spawned;
    [SerializeField] private bool mergeTriggered;

    // =========================
    // INTERNAL
    // =========================
    private enum BotPhase
    {
        NotSpawnedYet,
        Phase1_HoldAtSpawnX,   // EXACTLY match player speed; no gap controller
        Phase2_MovingToX,      // gap controller on
        WaitingForMerge,       // gap controller on
        Merging,
        PostMergeLead
    }

    private BotPhase phase = BotPhase.NotSpawnedYet;

    private float currentForwardSpeed = 0f;
    private float phase1HoldUntilTime = 0f;

    private Coroutine spawnRoutine;
    private Coroutine mergeRoutine;

    private Renderer[] cachedRenderers;
    private Collider[] cachedColliders;
    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            Debug.LogError($"{nameof(MoveOnWaypoints)} requires a Rigidbody on the same GameObject.");

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        cachedColliders = GetComponentsInChildren<Collider>(true);

        if (rb != null)
        {
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.useGravity = false;
            rb.isKinematic = true; // scripted motion
        }
    }

    void Start()
    {
        spawned = false;
        mergeTriggered = false;
        phase = BotPhase.NotSpawnedYet;

        ResolvePlayer();

        // Keep object ACTIVE so coroutines run, but hide it and disable collisions.
        SetVisibleAndCollidable(false);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

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

        float playerSpeed = GetPlayerForwardSpeedAbs();

        switch (phase)
        {
            case BotPhase.Phase1_HoldAtSpawnX:
            {
                // Force Phase 1 X and force exact speed match (no gap correction yet)
                pos.x = phase1SpawnX;

                currentForwardSpeed = playerSpeed; // lock speed to player
                pos.z += currentForwardSpeed * dt;

                if (Time.time >= phase1HoldUntilTime)
                    phase = BotPhase.Phase2_MovingToX;

                break;
            }

            case BotPhase.Phase2_MovingToX:
            {
                pos.x = Mathf.MoveTowards(pos.x, phase2TargetX, phase2LateralSpeed * dt);
                pos = ApplyPreMergeFollowZ(pos, playerSpeed, dt);

                if (Mathf.Abs(pos.x - phase2TargetX) <= phase2FinishTolX)
                {
                    pos.x = phase2TargetX;
                    phase = BotPhase.WaitingForMerge;
                }
                break;
            }

            case BotPhase.WaitingForMerge:
            {
                pos.x = phase2TargetX;
                pos = ApplyPreMergeFollowZ(pos, playerSpeed, dt);
                break;
            }

            case BotPhase.Merging:
            {
                pos.x = Mathf.MoveTowards(pos.x, mergeTargetX, mergeLateralSpeed * dt);

                float targetZ = player.position.z + mergeLeadMeters;
                float zError = targetZ - pos.z;

                float desiredSpeed = Mathf.Clamp(zError * 0.9f, 0f, mergeMaxSpeed);
                currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, mergeAccel * dt);
                pos.z += currentForwardSpeed * dt;

                bool doneX = Mathf.Abs(pos.x - mergeTargetX) <= mergeFinishTolX;
                bool doneZ = (pos.z >= targetZ) || Mathf.Abs(pos.z - targetZ) <= mergeFinishTolZ;

                if (doneX && doneZ)
                {
                    pos.x = mergeTargetX;
                    if (pos.z < targetZ) pos.z = targetZ;

                    phase = keepLeadAfterMerge ? BotPhase.PostMergeLead : BotPhase.WaitingForMerge;
                }
                break;
            }

            case BotPhase.PostMergeLead:
            {
                pos.x = mergeTargetX;

                float targetZ = player.position.z + mergeLeadMeters;
                float zError = targetZ - pos.z;

                float desiredSpeed = Mathf.Clamp(zError * 0.9f, 0f, mergeMaxSpeed);
                currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, mergeAccel * dt);
                pos.z += currentForwardSpeed * dt;

                break;
            }
        }

        rb.MovePosition(pos);
    }

    // =========================
    // SPAWN
    // =========================
    private IEnumerator DelayedSpawnRoutine()
    {
        phase = BotPhase.NotSpawnedYet;
        yield return new WaitForSeconds(spawnDelaySeconds);

        // Wait briefly for player to exist (tag-based)
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

        Vector3 pos = rb.position;
        pos.x = phase1SpawnX;

        if (player != null)
            pos.z = player.position.z - preMergeGapBehindMeters;

        float playerSpeed = GetPlayerForwardSpeedAbs();
        currentForwardSpeed = playerSpeed;

        rb.MoveRotation(Quaternion.Euler(0f, spawnYawDegrees, 0f));

        // Instant snap to position + match speed
        rb.position = pos;
        rb.velocity = new Vector3(0f, 0f, currentForwardSpeed);
        rb.angularVelocity = Vector3.zero;

        SetVisibleAndCollidable(true);

        spawned = true;
        mergeTriggered = false;

        phase = BotPhase.Phase1_HoldAtSpawnX;
        phase1HoldUntilTime = Time.time + Mathf.Max(0f, phase1HoldSeconds);

        BeginMergeTimerAlways();
    }

    // =========================
    // PRE-MERGE Z FOLLOW (player speed baseline + gap correction)
    // =========================
    private Vector3 ApplyPreMergeFollowZ(Vector3 pos, float playerSpeed, float dt)
    {
        float targetZ = player.position.z - preMergeGapBehindMeters;
        float zError = targetZ - pos.z;

        float correction = Mathf.Clamp(zError * gapKp, -maxSpeedDeltaFromPlayer, maxSpeedDeltaFromPlayer);
        float desiredSpeed = playerSpeed + correction;

        float actualGap = player.position.z - pos.z;
        if (actualGap < minGapMeters)
            desiredSpeed = 0f;

        desiredSpeed = Mathf.Clamp(desiredSpeed, 0f, maxSpeed);

        currentForwardSpeed = Mathf.MoveTowards(currentForwardSpeed, desiredSpeed, accel * dt);
        pos.z += currentForwardSpeed * dt;

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

        mergeTriggered = true; // ✅ runtime checkmark
        phase = BotPhase.Merging;
    }

    // =========================
    // PLAYER SPEED
    // =========================
    private float GetPlayerForwardSpeedAbs()
    {
        if (player == null) return 0f;

        Rigidbody prb = player.GetComponent<Rigidbody>();
        if (prb == null) return 0f;

        // Road is straight in world Z, so just use absolute Z speed
        return Mathf.Abs(prb.velocity.z);
    }

    // =========================
    // VISIBILITY / COLLISION
    // =========================
    private void SetVisibleAndCollidable(bool enabled)
    {
        if (cachedRenderers != null)
            for (int i = 0; i < cachedRenderers.Length; i++)
                cachedRenderers[i].enabled = enabled;

        if (cachedColliders != null)
            for (int i = 0; i < cachedColliders.Length; i++)
                cachedColliders[i].enabled = enabled;
    }

    private void ResolvePlayer()
    {
        if (player != null) return;
        if (!autoFindPlayerByTag) return;

        GameObject p = GameObject.FindGameObjectWithTag(playerTag);
        if (p != null) player = p.transform;
    }
}
