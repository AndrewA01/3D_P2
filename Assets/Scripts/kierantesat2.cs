using UnityEngine;

public class kierantesat2 : MonoBehaviour
{
    public GameObject pauseMenu; // Assign the PauseMenu UI Canvas in Inspector
    private bool isPaused = false;

    void Start()
    {
        if (pauseMenu != null)
            pauseMenu.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    public void PauseGame()
    {
        if (pauseMenu != null)
            pauseMenu.SetActive(true);

        Time.timeScale = 0f;
        isPaused = true;
    }

    public void ResumeGame()
    {
        if (pauseMenu != null)
            pauseMenu.SetActive(false);

        Time.timeScale = 1f;
        isPaused = false;
    }

    public void QuitGame()
    {
        // Note: Won't work in editor
        Application.Quit();
        Debug.Log("Quit Game"); // Just so we see something in the editor
    }
}
