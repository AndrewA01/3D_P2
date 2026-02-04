using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TurnSignalBlinkerSimpleV2 (Robust Material Swap)
///
/// Fixes common Unity hierarchy cases:
/// - Signal objects named LTURN/RTURN are parents, while MeshRenderer is on children with different names.
/// - This script assigns renderers if THEY OR ANY PARENT in their transform chain contains "LTURN" or "RTURN".
///
/// Blinking behavior:
/// - Only swaps materials in slots that already use either offMaterial or onMaterial (so it won't repaint the whole car).
/// - Starts/stops via OnMergeStarted / OnMergeEnded (called by MoveOnWaypoints).
/// </summary>
public class TurnSignalBlinkerSimpleV2 : MonoBehaviour
{
    [Header("Blink settings")]
    [SerializeField] private float blinkIntervalSeconds = 0.5f;

    [Header("Materials (required)")]
    [SerializeField] private Material offMaterial; // redoff / amboff
    [SerializeField] private Material onMaterial;  // redon  / ambon

    // Renderers whose transform chain contains LTURN or RTURN
    private readonly List<Renderer> signalRenderers = new List<Renderer>();

    // For each renderer, which material indices should be swapped (only those matching off/on)
    private readonly Dictionary<Renderer, int[]> swappableSlots = new Dictionary<Renderer, int[]>();

    private Coroutine blinkRoutine;
    private bool blinking;
    private bool stateOn;

    private void Awake()
    {
        CacheSignalRenderers();
        ForceOff();
    }

    private void OnDisable()
    {
        StopBlinking();
    }

    // Called by MoveOnWaypoints
    public void OnMergeStarted() => StartBlinking();
    public void OnMergeEnded() => StopBlinking();

    public void StartBlinking()
    {
        if (blinking) return;

        if (offMaterial == null || onMaterial == null)
            return;

        // If something changed in prefab/hierarchy, refresh once at start
        if (signalRenderers.Count == 0 || swappableSlots.Count == 0)
            CacheSignalRenderers();

        blinking = true;
        stateOn = false;

        if (blinkRoutine != null)
            StopCoroutine(blinkRoutine);

        blinkRoutine = StartCoroutine(BlinkLoop());
    }

    public void StopBlinking()
    {
        blinking = false;

        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }

        ForceOff();
    }

    private IEnumerator BlinkLoop()
    {
        while (blinking)
        {
            stateOn = !stateOn;
            Apply(stateOn ? onMaterial : offMaterial);
            yield return new WaitForSecondsRealtime(blinkIntervalSeconds);
        }
    }

    private void ForceOff()
    {
        if (offMaterial == null) return;
        Apply(offMaterial);
    }

    private void Apply(Material target)
    {
        if (target == null) return;

        for (int i = 0; i < signalRenderers.Count; i++)
        {
            Renderer r = signalRenderers[i];
            if (!r) continue;

            if (!swappableSlots.TryGetValue(r, out int[] slots) || slots == null || slots.Length == 0)
                continue;

            // Use instanced materials so we don't mutate shared assets globally
            Material[] mats = r.materials;
            bool changed = false;

            for (int s = 0; s < slots.Length; s++)
            {
                int idx = slots[s];
                if (idx < 0 || idx >= mats.Length) continue;
                if (mats[idx] == target) continue;

                mats[idx] = target;
                changed = true;
            }

            if (changed)
                r.materials = mats;
        }
    }

    private void CacheSignalRenderers()
    {
        signalRenderers.Clear();
        swappableSlots.Clear();

        // Find all renderers under this component
        Renderer[] all = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (!r) continue;

            // Only consider renderers that are under LTURN or RTURN (parent chain match)
            if (!IsUnderNamedChain(r.transform, "lturn") && !IsUnderNamedChain(r.transform, "rturn"))
                continue;

            // Determine which material slots are intended to be toggled:
            // Only slots currently using offMaterial or onMaterial.
            Material[] shared = r.sharedMaterials;
            if (shared == null || shared.Length == 0) continue;

            List<int> slots = new List<int>();
            for (int m = 0; m < shared.Length; m++)
            {
                if (shared[m] == offMaterial || shared[m] == onMaterial)
                    slots.Add(m);
            }

            // If no slots match, we don't touch this renderer (prevents repainting whole car)
            if (slots.Count == 0) continue;

            signalRenderers.Add(r);
            swappableSlots[r] = slots.ToArray();
        }
    }

    private bool IsUnderNamedChain(Transform t, string needleLower)
    {
        Transform cur = t;
        while (cur != null)
        {
            if (cur.name != null && cur.name.ToLowerInvariant().Contains(needleLower))
                return true;

            if (cur == transform) break;
            cur = cur.parent;
        }
        return false;
    }
}
