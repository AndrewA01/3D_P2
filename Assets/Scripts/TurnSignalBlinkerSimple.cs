using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn signal blinker that requires NO inspector setup.
/// It will:
///  - Search child Renderers whose GameObject names look like signals ("turn", "signal", "indicator", "blinker", "blink") OR
///  - Fall back to Renderers whose materials have _EmissioSnColor (so it can blink by emission).
/// It blinks by toggling emission between original and black.
/// </summary>
public class TurnSignalBlinkerSimple : MonoBehaviour
{
    [Header("Blink Settings")]
    [SerializeField] private float blinkIntervalSeconds = 0.5f;

    private Coroutine blinkRoutine;
    private bool blinking;
    private bool on;

    private readonly List<Renderer> targets = new List<Renderer>();
    private readonly Dictionary<Material, Color> originalEmission = new Dictionary<Material, Color>();

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        AutoDiscoverTargets();
        CacheOriginalEmission();
        ApplyState(false);
    }

    private void OnDisable()
    {
        StopBlinking();
    }

    /// <summary>Call this when the merge cue begins (signal should start blinking).</summary>
    public void OnMergeStarted()
    {
        StartBlinking();
    }

    /// <summary>Call this when the merge cue ends (signal should stop blinking).</summary>
    public void OnMergeEnded()
    {
        StopBlinking();
    }

    public void StartBlinking()
    {
        if (blinking) return;

        // In case this component was added/instantiated and Awake didn't find anything yet
        if (targets.Count == 0)
        {
            AutoDiscoverTargets();
            CacheOriginalEmission();
        }

        blinking = true;
        on = false;
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

        ApplyState(false);
    }

    private IEnumerator BlinkLoop()
    {
        while (blinking)
        {
            on = !on;
            ApplyState(on);
            yield return new WaitForSeconds(blinkIntervalSeconds);
        }
    }

    private void ApplyState(bool turnOn)
    {
        if (targets.Count == 0) return;

        for (int r = 0; r < targets.Count; r++)
        {
            var rend = targets[r];
            if (!rend) continue;

            // Use instance materials so we don't edit shared assets across scenes/prefabs.
            var mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;
                if (!m.HasProperty(EmissionColorId)) continue;

                m.EnableKeyword("_EMISSION");

                if (turnOn)
                {
                    if (originalEmission.TryGetValue(m, out var col))
                        m.SetColor(EmissionColorId, col);
                }
                else
                {
                    m.SetColor(EmissionColorId, Color.black);
                }
            }
        }
    }

    private void CacheOriginalEmission()
    {
        originalEmission.Clear();

        for (int r = 0; r < targets.Count; r++)
        {
            var rend = targets[r];
            if (!rend) continue;

            var mats = rend.materials;
            foreach (var m in mats)
            {
                if (!m) continue;
                if (!m.HasProperty(EmissionColorId)) continue;
                if (originalEmission.ContainsKey(m)) continue;

                originalEmission[m] = m.GetColor(EmissionColorId);
            }
        }
    }

    private void AutoDiscoverTargets()
    {
        targets.Clear();

        // First pass: name-based (most deterministic if your signal objects are named sensibly)
        var all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var rend = all[i];
            if (!rend) continue;

            string n = rend.gameObject.name.ToLowerInvariant();
            bool looksLikeSignal =
                n.Contains("turn") ||
                n.Contains("signal") ||
                n.Contains("indicator") ||
                n.Contains("blinker") ||
                n.Contains("blink");

            if (looksLikeSignal)
                targets.Add(rend);
        }

        // Fallback: anything with an emission property (so blinking still works even if names aren't helpful)
        if (targets.Count == 0)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var rend = all[i];
                if (!rend) continue;

                var mats = rend.sharedMaterials;
                if (mats == null) continue;

                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat != null && mat.HasProperty(EmissionColorId))
                    {
                        targets.Add(rend);
                        break;
                    }
                }
            }
        }
    }
}
