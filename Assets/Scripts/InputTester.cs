using UnityEngine;

// Attach to any active GameObject. Logs every P-press and GameView focus state.
public class InputTester : MonoBehaviour
{
    void OnEnable() => Debug.Log("InputTester: Enabled and running.");

    void Update()
    {
        if (!Application.isFocused)
        {
            // show occasionally so you know focus is wrong
            if (Time.frameCount % 300 == 0)
                Debug.Log("InputTester: Application not focused (click Game view).");
            return;
        }

        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("InputTester: P pressed (detected).");
        }
    }
}
