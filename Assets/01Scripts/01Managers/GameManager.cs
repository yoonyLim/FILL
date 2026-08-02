using System;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject menuCanvas;

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
        }
    }

    public void SetFullScreen(bool isFullScreen)
    {
        Screen.fullScreen = isFullScreen;
    }

    public void SetResolution(int width, int height)
    {
        Debug.Log(width + ":" + height);
        Screen.SetResolution(width, height, Screen.fullScreenMode);
    }

    public void StartGame()
    {
        mainCanvas.SetActive(true);
        menuCanvas.SetActive(false);
    }
}
