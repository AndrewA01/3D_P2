using UnityEngine;

public class TurnSignalBlinkerSimpleV2 : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private GameObject targetObject;

    [Header("Materials")]
    [SerializeField] private Material materialOn;
    [SerializeField] private Material materialOff;

    [Header("Blinking")]
    [SerializeField] private float blinkIntervalSeconds = 0.5f;
    [SerializeField] private bool startBlinkingOnStart = false;

    private Renderer[] renderers;
    private bool blinking;
    private bool isOn;
    private float nextToggle;

    private void Awake()
    {
        if (targetObject == null) return;
        renderers = targetObject.GetComponentsInChildren<Renderer>(true);
        ApplyOff();
    }

    private void Start()
    {
        if (startBlinkingOnStart)
            StartBlinking();
    }

    private void Update()
    {
        if (!blinking) return;

        if (Time.time >= nextToggle)
        {
            nextToggle = Time.time + blinkIntervalSeconds;
            if (isOn) ApplyOff();
            else ApplyOn();
        }
    }

    public void OnMergeStarted() => StartBlinking();
    public void OnMergeEnded() => StopBlinking();

    public void StartBlinking()
    {
        blinking = true;
        isOn = false;
        nextToggle = Time.time;
    }

    public void StopBlinking()
    {
        blinking = false;
        ApplyOff();
    }

    private void ApplyOn()
    {
        isOn = true;
        ApplyMaterial(materialOn);
    }

    private void ApplyOff()
    {
        isOn = false;
        ApplyMaterial(materialOff);
    }

    private void ApplyMaterial(Material m)
    {
        if (m == null || renderers == null) return;
        foreach (var r in renderers)
            r.material = m;
    }
}
