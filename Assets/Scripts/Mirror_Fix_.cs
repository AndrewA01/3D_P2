using UnityEngine;

/// <summary>
/// Attach to each mirror plane GameObject (MirrorL/MirrorM/MirrorR).
/// Assign the mirror camera (the camera that should render that mirror)
/// and the mirror mesh renderer (the plane's MeshRenderer).
///
/// Fixes build-only issues where a mirror camera becomes the screen camera by:
/// - Forcing mirror cameras to be Untagged and removing AudioListener
/// - Creating a UNIQUE RenderTexture per mirror at runtime
/// - Assigning RT to both the mirror camera and the mirror material (material instance)
///
/// IMPORTANT: This script does NOT modify other cameras (safer for builds).
/// </summary>
[DisallowMultipleComponent]
public class Mirror_Fix_ : MonoBehaviour
{
    [Header("Required References")]
    [Tooltip("The camera that renders THIS mirror view.")]
    public Camera mirrorCamera;

    [Tooltip("The MeshRenderer for THIS mirror plane (so we can set its material texture).")]
    public MeshRenderer mirrorRenderer;

    [Header("RenderTexture Settings")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public int depth = 24;

    [Header("Safety / Debug")]
    public bool verboseLogs = true;

    private RenderTexture runtimeRT;
    private Material runtimeMatInstance;

    void Reset()
    {
        // Best-effort auto-fill on add
        if (!mirrorRenderer) mirrorRenderer = GetComponent<MeshRenderer>();
    }

    void Awake()
    {
        if (!mirrorRenderer) mirrorRenderer = GetComponent<MeshRenderer>();

        if (mirrorCamera == null)
        {
            Debug.LogError($"[Mirror_Fix_Safe] Mirror camera not assigned on '{name}'.", this);
            enabled = false;
            return;
        }

        if (mirrorRenderer == null)
        {
            Debug.LogError($"[Mirror_Fix_Safe] Mirror renderer not found/assigned on '{name}'.", this);
            enabled = false;
            return;
        }

        // Ensure this camera can NEVER be picked up as the main view camera
        if (mirrorCamera.CompareTag("MainCamera"))
        {
            mirrorCamera.tag = "Untagged";
            if (verboseLogs) Debug.Log($"[Mirror_Fix_Safe] '{mirrorCamera.name}' was MainCamera; changed to Untagged.");
        }

        AudioListener al = mirrorCamera.GetComponent<AudioListener>();
        if (al != null)
        {
            Destroy(al);
            if (verboseLogs) Debug.Log($"[Mirror_Fix_Safe] Removed AudioListener from '{mirrorCamera.name}'.");
        }

        // Create unique RT and assign
        CreateAndAssignUniqueRT();

        // Extra safety: make sure it won't ever visually override the screen
        // (Even if targetTexture gets cleared somehow, this reduces risk.)
        mirrorCamera.depth = -100;
        mirrorCamera.clearFlags = CameraClearFlags.Depth;

        if (verboseLogs)
        {
            Debug.Log($"[Mirror_Fix_Safe] Mirror '{name}' using camera '{mirrorCamera.name}', RT '{runtimeRT.name}'.", this);
        }
    }

    void LateUpdate()
    {
        // Safety net: if something cleared the RT in build, restore it.
        if (mirrorCamera != null && (mirrorCamera.targetTexture == null || mirrorCamera.targetTexture != runtimeRT))
        {
            if (verboseLogs) Debug.LogWarning($"[Mirror_Fix_Safe] Restoring targetTexture for '{mirrorCamera.name}'.", this);
            mirrorCamera.targetTexture = runtimeRT;
        }
    }

    private void CreateAndAssignUniqueRT()
    {
        // Clean old RT if any
        if (runtimeRT != null)
        {
            if (mirrorCamera && mirrorCamera.targetTexture == runtimeRT)
                mirrorCamera.targetTexture = null;

            runtimeRT.Release();
            Destroy(runtimeRT);
            runtimeRT = null;
        }

        runtimeRT = new RenderTexture(textureWidth, textureHeight, depth)
        {
            name = $"RT_{name}_{mirrorCamera.name}_{GetInstanceID()}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            antiAliasing = QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 1
        };
        runtimeRT.Create();

        mirrorCamera.targetTexture = runtimeRT;

        // IMPORTANT: use a material instance so we don't overwrite a shared material asset
        runtimeMatInstance = mirrorRenderer.material; // this creates an instance at runtime
        runtimeMatInstance.mainTexture = runtimeRT;
    }

    void OnDestroy()
    {
        if (runtimeRT != null)
        {
            if (mirrorCamera && mirrorCamera.targetTexture == runtimeRT)
                mirrorCamera.targetTexture = null;

            runtimeRT.Release();
            Destroy(runtimeRT);
            runtimeRT = null;
        }
    }
}
