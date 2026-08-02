using UnityEngine;

public class MainMenuManager : MonoBehaviour
{
    public void SetHDResolution()
    {
        Debug.Log(Screen.width + ":" + Screen.height);
        GameManager.Instance.SetResolution(1280, 720);
    }
    
    public void SetFHDResolution()
    {
        Debug.Log(Screen.width + ":" + Screen.height);
        GameManager.Instance.SetResolution(1920, 1080);
    }

    public void SetQHDResolution()
    {
        Debug.Log(Screen.width + ":" + Screen.height);
        GameManager.Instance.SetResolution(2560, 1440);
    }
    
    public void Set4kResolution()
    {
        Debug.Log(Screen.width + ":" + Screen.height);
        GameManager.Instance.SetResolution(3480, 2160);
    }
}
