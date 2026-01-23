using UnityEngine;

public class TurnSignalBlinkerSimple : MonoBehaviour
{
    [Header("Drag the signal GameObject here (root of the mesh/lens/bulb parts)")]
    public GameObject signalObject;

    [Header("Materials to swap")]
    [Tooltip("Material used when the signal is ON (glowy/emissive).")]
    public Material onMaterial;

    [Tooltip("Material used when the signal is OFF (non-emissive/dim).")]
    public Material offMaterial;

    [Header("Blink rate")]
    [Tooltip("How many full flashes per second (ON + OFF = 1 flash).")]
    public float flashesPerSecond = 1.5f;

    [Header("Runtime")]
    [Tooltip("Starts false. Another script should call OnMergeStarted() / OnMergeEnded().")]
    [SerializeField] private bool isBlinking = false;

    private Renderer[] renderers;
    private bool lastOnState = false;

    void Awake()
    {
        if (signalObject == null)
        {
            Debug.LogError("TurnSignalBlinkerSimple: No signalObject assigned.", this);
            enabled = false;
            return;
        }
        if (onMaterial == null || offMaterial == null)
        {
            Debug.LogError("TurnSignalBlinkerSimple: Assign BOTH onMaterial and offMaterial.", this);
            enabled = false;
            return;
        }

        renderers = signalObject.GetComponentsInChildren<Renderer>(true);

        // start OFF
        SetState(false, force: true);
    }

    void OnEnable()
    {
        SetState(false, force: true);
    }

    void Update()
    {
        if (renderers == null) return;

        if (!isBlinking)
        {
            SetState(false);
            return;
        }

        if (flashesPerSecond <= 0f)
        {
            SetState(false);
            return;
        }

        float period = 1f / flashesPerSecond;
        bool on = Mathf.Repeat(Time.time, period) < period * 0.5f;
        SetState(on);
    }

    // Call this when merge event starts (bot begins accelerating to reach start lead)
    public void OnMergeStarted()
    {
        isBlinking = true;
        // Force immediate visible ON so you don't miss it due to timing
        SetState(true, force: true);
    }

    // Call this when merge ends (or when you want to stop blinking)
    public void OnMergeEnded()
    {
        isBlinking = false;
        SetState(false, force: true);
    }

    private void SetState(bool on, bool force = false)
    {
        if (!force && on == lastOnState) return;
        lastOnState = on;

        Material matToUse = on ? onMaterial : offMaterial;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            var slots = r.sharedMaterials;
            if (slots == null || slots.Length == 0) continue;

            for (int s = 0; s < slots.Length; s++)
                slots[s] = matToUse;

            r.sharedMaterials = slots;
        }
    }
}
