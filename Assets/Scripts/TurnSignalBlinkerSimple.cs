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
        if (renderers == null || flashesPerSecond <= 0f) return;

        float period = 1f / flashesPerSecond;
        bool on = Mathf.Repeat(Time.time, period) < period * 0.5f;

        SetState(on);
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

            // Apply to ALL material slots on this renderer
            // (so it works even if the mesh has multiple sub-materials)
            var slots = r.sharedMaterials;
            if (slots == null || slots.Length == 0) continue;

            for (int s = 0; s < slots.Length; s++)
                slots[s] = matToUse;

            r.sharedMaterials = slots;
        }
    }
}
