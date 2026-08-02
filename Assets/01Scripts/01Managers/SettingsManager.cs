using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(GraphicRaycaster))]
public class SettingsManager : MonoBehaviour
{
    private const string PreferencesPrefix = "FILL.Settings.";
    private const string RandomSaturationKey = PreferencesPrefix + "RandomSaturation";
    private const string RandomColorKey = PreferencesPrefix + "RandomColor";
    private const string SandColorRedKey = PreferencesPrefix + "SandColorRed";
    private const string SandColorGreenKey = PreferencesPrefix + "SandColorGreen";
    private const string SandColorBlueKey = PreferencesPrefix + "SandColorBlue";
    private const string ResolutionWidthKey = PreferencesPrefix + "ResolutionWidth";
    private const string ResolutionHeightKey = PreferencesPrefix + "ResolutionHeight";
    private const string FullScreenKey = PreferencesPrefix + "FullScreen";

    [Header("Overlay")]
    [SerializeField] private bool startClosed = true;

    [Header("Sand")]
    [SerializeField] private SandManager sandManager;
    [SerializeField] private Toggle randomSandSaturationToggle;
    [SerializeField] private Toggle randomSandColorToggle;
    [Tooltip("RGB sliders should each use a range from 0 to 1.")]
    [SerializeField] private Slider sandColorRedSlider;
    [SerializeField] private Slider sandColorGreenSlider;
    [SerializeField] private Slider sandColorBlueSlider;
    [SerializeField] private Image sandColorPreview;

    [Header("Display")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullScreenToggle;

    private readonly List<Vector2Int> _availableResolutions = new();
    private Canvas _settingsOverlayCanvas;
    private GraphicRaycaster _settingsOverlayRaycaster;
    private bool _isOpen;

    public bool IsOpen => _isOpen;

    private void Awake()
    {
        _settingsOverlayCanvas = GetComponent<Canvas>();
        _settingsOverlayRaycaster = GetComponent<GraphicRaycaster>();

        if (sandManager == null)
        {
            sandManager = FindFirstObjectByType<SandManager>(FindObjectsInactive.Include);
        }

        ConfigureColorSlider(sandColorRedSlider);
        ConfigureColorSlider(sandColorGreenSlider);
        ConfigureColorSlider(sandColorBlueSlider);
    }

    private void Start()
    {
        PopulateResolutionDropdown();
        LoadAndApplySettings();
        RegisterUiCallbacks();
        SetSettingsOpen(!startClosed);
    }

    private void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleSettings();
        }
    }

    public void ToggleSettings()
    {
        if (_isOpen)
        {
            CloseSettings();
        }
        else
        {
            OpenSettings();
        }
    }

    public void OpenSettings()
    {
        SetSettingsOpen(true);
    }

    public void CloseSettings()
    {
        SetSettingsOpen(false);
        PlayerPrefs.Save();
    }

    public void SetRandomSandSaturation(bool enabled)
    {
        sandManager?.SetRandomSandSaturation(enabled);
        randomSandSaturationToggle?.SetIsOnWithoutNotify(enabled);
        PlayerPrefs.SetInt(RandomSaturationKey, enabled ? 1 : 0);
    }

    public void SetRandomSandColor(bool enabled)
    {
        sandManager?.SetRandomSandColor(enabled);
        randomSandColorToggle?.SetIsOnWithoutNotify(enabled);
        PlayerPrefs.SetInt(RandomColorKey, enabled ? 1 : 0);
    }

    public void SetSandColor(Color color)
    {
        color.a = 1f;
        sandManager?.SetSandColor(color);
        UpdateColorControls(color);

        PlayerPrefs.SetFloat(SandColorRedKey, color.r);
        PlayerPrefs.SetFloat(SandColorGreenKey, color.g);
        PlayerPrefs.SetFloat(SandColorBlueKey, color.b);
    }

    public void SetResolutionByIndex(int index)
    {
        if (index < 0 || index >= _availableResolutions.Count) return;

        Vector2Int resolution = _availableResolutions[index];

        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetResolution(resolution.x, resolution.y);
        }
        else
        {
            sandManager?.SetResolution(resolution.x, resolution.y);
            Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
        }

        PlayerPrefs.SetInt(ResolutionWidthKey, resolution.x);
        PlayerPrefs.SetInt(ResolutionHeightKey, resolution.y);
    }

    public void SetFullScreen(bool enabled)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SetFullScreen(enabled);
        }
        else
        {
            Screen.fullScreen = enabled;
        }

        fullScreenToggle?.SetIsOnWithoutNotify(enabled);
        PlayerPrefs.SetInt(FullScreenKey, enabled ? 1 : 0);
    }

    private void SetSettingsOpen(bool open)
    {
        _isOpen = open;

        // Keep this GameObject active so Update can detect Escape/Back while closed.
        _settingsOverlayCanvas.enabled = open;
        _settingsOverlayRaycaster.enabled = open;

        // UI interaction should not spawn sand behind the settings overlay.
        sandManager?.SetInputEnabled(!open);
    }

    private void LoadAndApplySettings()
    {
        bool randomSaturation = PlayerPrefs.GetInt(
            RandomSaturationKey,
            sandManager != null && sandManager.RandomSandSaturation ? 1 : 0) == 1;
        bool randomColor = PlayerPrefs.GetInt(
            RandomColorKey,
            sandManager != null && sandManager.RandomSandColor ? 1 : 0) == 1;

        Color defaultColor = sandManager != null
            ? sandManager.SandColor
            : new Color(0.8f, 0.7f, 0.4f, 1f);
        Color savedColor = new(
            PlayerPrefs.GetFloat(SandColorRedKey, defaultColor.r),
            PlayerPrefs.GetFloat(SandColorGreenKey, defaultColor.g),
            PlayerPrefs.GetFloat(SandColorBlueKey, defaultColor.b),
            1f);

        sandManager?.SetRandomSandSaturation(randomSaturation);
        sandManager?.SetRandomSandColor(randomColor);
        sandManager?.SetSandColor(savedColor);

        randomSandSaturationToggle?.SetIsOnWithoutNotify(randomSaturation);
        randomSandColorToggle?.SetIsOnWithoutNotify(randomColor);
        UpdateColorControls(savedColor);

        bool fullScreen = PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) == 1;
        fullScreenToggle?.SetIsOnWithoutNotify(fullScreen);

        if (PlayerPrefs.HasKey(FullScreenKey))
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetFullScreen(fullScreen);
            }
            else
            {
                Screen.fullScreen = fullScreen;
            }
        }

        int savedWidth = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width);
        int savedHeight = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height);
        int resolutionIndex = FindResolutionIndex(savedWidth, savedHeight);

        if (resolutionDropdown != null && resolutionIndex >= 0)
        {
            resolutionDropdown.SetValueWithoutNotify(resolutionIndex);
            resolutionDropdown.RefreshShownValue();
        }

        if (PlayerPrefs.HasKey(ResolutionWidthKey) && resolutionIndex >= 0)
        {
            Vector2Int resolution = _availableResolutions[resolutionIndex];

            if (GameManager.Instance != null)
            {
                GameManager.Instance.SetResolution(resolution.x, resolution.y);
            }
            else
            {
                sandManager?.SetResolution(resolution.x, resolution.y);
                Screen.SetResolution(resolution.x, resolution.y, Screen.fullScreenMode);
            }
        }
    }

    private void PopulateResolutionDropdown()
    {
        _availableResolutions.Clear();

        foreach (Resolution resolution in Screen.resolutions)
        {
            Vector2Int size = new(resolution.width, resolution.height);
            if (!_availableResolutions.Contains(size))
            {
                _availableResolutions.Add(size);
            }
        }

        Vector2Int currentSize = new(Screen.width, Screen.height);
        if (!_availableResolutions.Contains(currentSize))
        {
            _availableResolutions.Add(currentSize);
        }

        _availableResolutions.Sort((left, right) =>
        {
            int widthComparison = left.x.CompareTo(right.x);
            return widthComparison != 0 ? widthComparison : left.y.CompareTo(right.y);
        });

        if (resolutionDropdown == null) return;

        List<string> options = new(_availableResolutions.Count);
        foreach (Vector2Int resolution in _availableResolutions)
        {
            options.Add($"{resolution.x} x {resolution.y}");
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(options);
        resolutionDropdown.interactable = _availableResolutions.Count > 1;
    }

    private int FindResolutionIndex(int width, int height)
    {
        for (int index = 0; index < _availableResolutions.Count; index++)
        {
            Vector2Int resolution = _availableResolutions[index];
            if (resolution.x == width && resolution.y == height)
            {
                return index;
            }
        }

        return _availableResolutions.Count > 0
            ? FindResolutionIndex(Screen.width, Screen.height)
            : -1;
    }

    private void RegisterUiCallbacks()
    {
        randomSandSaturationToggle?.onValueChanged.AddListener(SetRandomSandSaturation);
        randomSandColorToggle?.onValueChanged.AddListener(SetRandomSandColor);
        sandColorRedSlider?.onValueChanged.AddListener(OnColorSliderChanged);
        sandColorGreenSlider?.onValueChanged.AddListener(OnColorSliderChanged);
        sandColorBlueSlider?.onValueChanged.AddListener(OnColorSliderChanged);
        resolutionDropdown?.onValueChanged.AddListener(SetResolutionByIndex);
        fullScreenToggle?.onValueChanged.AddListener(SetFullScreen);
    }

    private void OnColorSliderChanged(float unusedValue)
    {
        Color currentColor = sandManager != null
            ? sandManager.SandColor
            : Color.white;
        Color selectedColor = new(
            sandColorRedSlider != null ? sandColorRedSlider.value : currentColor.r,
            sandColorGreenSlider != null ? sandColorGreenSlider.value : currentColor.g,
            sandColorBlueSlider != null ? sandColorBlueSlider.value : currentColor.b,
            1f);

        SetSandColor(selectedColor);
    }

    private void UpdateColorControls(Color color)
    {
        sandColorRedSlider?.SetValueWithoutNotify(color.r);
        sandColorGreenSlider?.SetValueWithoutNotify(color.g);
        sandColorBlueSlider?.SetValueWithoutNotify(color.b);

        if (sandColorPreview != null)
        {
            sandColorPreview.color = color;
        }
    }

    private static void ConfigureColorSlider(Slider slider)
    {
        if (slider == null) return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
    }

    private void OnDestroy()
    {
        PlayerPrefs.Save();

        randomSandSaturationToggle?.onValueChanged.RemoveListener(SetRandomSandSaturation);
        randomSandColorToggle?.onValueChanged.RemoveListener(SetRandomSandColor);
        sandColorRedSlider?.onValueChanged.RemoveListener(OnColorSliderChanged);
        sandColorGreenSlider?.onValueChanged.RemoveListener(OnColorSliderChanged);
        sandColorBlueSlider?.onValueChanged.RemoveListener(OnColorSliderChanged);
        resolutionDropdown?.onValueChanged.RemoveListener(SetResolutionByIndex);
        fullScreenToggle?.onValueChanged.RemoveListener(SetFullScreen);
    }
}
