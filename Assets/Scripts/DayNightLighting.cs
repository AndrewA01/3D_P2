using UnityEngine;

public class DayNightLighting : MonoBehaviour
{
    public Light directionalLight;  // Assign your main Directional Light manually

    // DAY BOOST SETTINGS
    [Header("Day Boost Settings")]
    public float dayIntensityBoost = 1.5f;        // Multiplier on current light intensity
    public Color dayColor = new Color(1f, 0.95f, 0.85f);  // Slightly warmer daylight
    public float dayAmbientMultiplier = 1.2f;     // Boost ambient light

    // NIGHT SETTINGS
    [Header("Night Settings")]
    public Vector3 nightRotation = new Vector3(330f, -30f, 0f);
    public float nightIntensity = 0.25f;
    public Color nightLightColor = new Color(0.6f, 0.7f, 1f);
    public Color nightAmbientColor = new Color(0.1f, 0.1f, 0.2f);

    private float originalIntensity;
    private Color originalAmbient;

    private void Start()
    {
        if (ExperimentController.Instance == null)
        {
            Debug.LogWarning("ExperimentController not found. Defaulting to DAY.");
            return;
        }

        if (directionalLight == null)
        {
            Debug.LogWarning("No directional light assigned!");
            return;
        }

        // Save original values so "Day" only BUMPS brightness, not overwrite
        originalIntensity = directionalLight.intensity;
        originalAmbient = RenderSettings.ambientLight;

        // Check block (Day/Night)
        if (ExperimentController.Instance.currentBlock == BlockType.Day)
        {
            ApplyDayBoost();
        }
        else
        {
            ApplyNightLighting();
        }
    }

    private void ApplyDayBoost()
    {
        Debug.Log("Lighting: Applying DAY BOOST.");

        // Increase overall brightness but keep your day look
        directionalLight.intensity = originalIntensity * dayIntensityBoost;
        directionalLight.color = dayColor;

        // Boost ambient for overall visibility
        RenderSettings.ambientLight = originalAmbient * dayAmbientMultiplier;
    }

    private void ApplyNightLighting()
{
    Debug.Log("Lighting: Applying NIGHT MODE.");

    // Directional light
    directionalLight.transform.rotation = Quaternion.Euler(nightRotation);
    directionalLight.intensity = nightIntensity;
    directionalLight.color = nightLightColor;

    // DARK ambient lighting
    RenderSettings.ambientLight = nightAmbientColor;

    // ⭐ REMOVE BRIGHT HORIZON ⭐
    RenderSettings.skybox = null; // Removes default blue sky
    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    RenderSettings.ambientLight = new Color(0.02f, 0.02f, 0.05f); // deep night
}

}
