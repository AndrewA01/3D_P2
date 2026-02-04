using System.Collections;
using UnityEngine;

public class TurnSignalBlinkerSimpleV2 : MonoBehaviour
{
    [Header("Blink")]
    [SerializeField] private float blinkIntervalSeconds = 0.5f;

    [Header("PRIMARY (this object + children) Materials")]
    [SerializeField] private Material primaryOffMaterial;   // e.g., AMBEROFF
    [SerializeField] private Material primaryOnMaterial;    // e.g., AMBERON

    [Header("Optional Extra Root (e.g., rear turn signal subtree)")]
    [SerializeField] private Transform extraRoot;

    [Header("EXTRA ROOT Materials")]
    [SerializeField] private Material extraOffMaterial;     // e.g., REDOFF
    [SerializeField] private Material extraOnMaterial;      // e.g., REDON

    private Renderer[] primaryRenderers;
    private Renderer[] extraRenderers;

    private Coroutine blinkRoutine;
    private bool isOn;

    private void Awake()
    {
        CacheRenderers();
        Apply(primaryOffMaterial, extraOffMaterial);
    }

    private void OnEnable()
    {
        CacheRenderers();
    }

    private void OnDisable()
    {
        OnMergeEnded();
    }

    // Called by MoveOnWaypoints
    public void OnMergeStarted()
    {
        if (blinkRoutine != null) return;

        // Primary must be set to blink primary.
        if (primaryOffMaterial == null || primaryOnMaterial == null) return;

        // Extra pair is only required if extraRoot is set.
        if (extraRoot != null && (extraOffMaterial == null || extraOnMaterial == null)) return;

        if ((primaryRenderers == null || primaryRenderers.Length == 0) ||
            (extraRoot != null && (extraRenderers == null || extraRenderers.Length == 0)))
        {
            CacheRenderers();
        }

        isOn = false;
        blinkRoutine = StartCoroutine(BlinkLoop());
    }

    // Called by MoveOnWaypoints
    public void OnMergeEnded()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }

        Apply(primaryOffMaterial, extraOffMaterial);
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            isOn = !isOn;

            Apply(
                isOn ? primaryOnMaterial : primaryOffMaterial,
                extraRoot != null ? (isOn ? extraOnMaterial : extraOffMaterial) : null
            );

            yield return new WaitForSecondsRealtime(blinkIntervalSeconds);
        }
    }

    private void CacheRenderers()
    {
        primaryRenderers = GetComponentsInChildren<Renderer>(true);

        extraRenderers = extraRoot != null
            ? extraRoot.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];
    }

    private void Apply(Material primaryMat, Material extraMat)
    {
        // Primary subtree (this object + children)
        SetAllSlots(primaryRenderers, primaryMat);

        // Extra subtree
        if (extraRoot != null)
            SetAllSlots(extraRenderers, extraMat);
    }

    private void SetAllSlots(Renderer[] renderers, Material mat)
    {
        if (mat == null || renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r) continue;

            Material[] mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
                mats[m] = mat;

            r.materials = mats;
        }
    }
}
