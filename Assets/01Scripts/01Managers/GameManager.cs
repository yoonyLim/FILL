using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject menuCanvas;

    private SandManager _sandManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
            _sandManager = mainCanvas != null
                ? mainCanvas.GetComponent<SandManager>()
                : null;
        }
    }

    public void SetFullScreen(bool isFullScreen)
    {
        Screen.fullScreen = isFullScreen;
    }

    public void SetResolution(int width, int height)
    {
        Debug.Log(width + ":" + height);
        mainCanvas.GetComponent<SandManager>().SetResolution(width, height);
        Screen.SetResolution(width, height, Screen.fullScreenMode);
    }

    public void StartGame()
    {
        mainCanvas?.SetActive(true);
        menuCanvas?.SetActive(false);
    }

    public void StartGame(Vector2 initialPointerPosition)
    {
        StartGame();
        _sandManager?.QueueSandBurst(initialPointerPosition);
    }
}
