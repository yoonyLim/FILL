using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class SandManager : MonoBehaviour
{
    public ComputeShader sandCompute;
    public RawImage displayImage; 
    public float brushSize = 10f;
    [Min(1f)] public float simulationStepsPerSecond = 60f;
    [Min(1)] public int maxSimulationStepsPerFrame = 8;

    [Header("Sand Appearance")]
    [SerializeField] private Color sandColor = new(0.8f, 0.7f, 0.4f, 1f); // rgb: 204, 178.5, 102
    [SerializeField] private bool randomSandSaturation;
    [SerializeField] private bool randomSandColor;
    [FormerlySerializedAs("randomColorCycleDuration")]
    [Min(0.5f)] [SerializeField] private float randomColorTransitionDuration = 4f;

    private RenderTexture _sandTexture;
    private int _kernelIndex;
    private int _width;
    private int _height;
    private float _simulationAccumulator;
    private Vector2 _pointerTexturePosition;
    private Vector2 _pendingBurstPosition;
    private bool _pointerPressed;
    private bool _pendingBurst;
    private bool _inputEnabled = true;
    private uint _spawnRandomSeed;
    private float _currentRandomColorHue;
    private float _randomColorStartHue;
    private float _randomColorTargetHue;
    private float _randomColorTransitionElapsed;

    public Color SandColor => sandColor;
    public bool RandomSandSaturation => randomSandSaturation;
    public bool RandomSandColor => randomSandColor;

    void Awake()
    {
        _kernelIndex = sandCompute.FindKernel("Update");
        _currentRandomColorHue = Random.value;
        _randomColorStartHue = _currentRandomColorHue;
        _randomColorTargetHue = ChooseNextRandomHue(_currentRandomColorHue);

        // Fall back to the current screen size if no resolution button was used.
        if (_width <= 0 || _height <= 0)
        {
            _width = Screen.width;
            _height = Screen.height;
        }

        CreateSandTexture();
    }

    void Update()
    {
        if (_sandTexture == null || !_sandTexture.IsCreated()) return;

        UpdateRandomColorHue();
        CapturePointerInput();

        float stepDuration = 1f / Mathf.Max(1f, simulationStepsPerSecond);
        int maxSteps = Mathf.Max(1, maxSimulationStepsPerFrame);

        // Limit accumulated time so a long stall cannot cause an unbounded catch-up loop.
        _simulationAccumulator = Mathf.Min(
            _simulationAccumulator + Time.deltaTime,
            stepDuration * maxSteps);

        int completedSteps = 0;
        while (_simulationAccumulator >= stepDuration && completedSteps < maxSteps)
        {
            bool isBurst = _pendingBurst;
            bool isDragging = !isBurst && _pointerPressed;
            Vector2 inputPosition = isBurst
                ? _pendingBurstPosition
                : _pointerTexturePosition;

            DispatchSimulation(inputPosition, isBurst, isDragging);

            _pendingBurst = false;
            _simulationAccumulator -= stepDuration;
            completedSteps++;
        }
    }

    private void CapturePointerInput()
    {
        _pointerPressed = false;
        if (!_inputEnabled) return;

        Pointer pointer = Pointer.current;
        if (pointer == null) return;

        Vector2 pointerPosition = pointer.position.ReadValue();
        if (!TryConvertToTexturePosition(pointerPosition, out _pointerTexturePosition)) return;

        // A mouse click, primary touch, or pen press all use the same pointer controls.
        _pointerPressed = pointer.press.isPressed;

        if (pointer.press.wasPressedThisFrame)
        {
            _pendingBurst = true;
            _pendingBurstPosition = _pointerTexturePosition;
        }
    }

    private void DispatchSimulation(Vector2 inputPosition, bool isBurst, bool isDragging)
    {
        if (isBurst || isDragging)
        {
            _spawnRandomSeed++;
        }

        // Pass data to GPU
        sandCompute.SetTexture(_kernelIndex, "Result", _sandTexture);
        sandCompute.SetVector("MousePos", inputPosition);
        sandCompute.SetFloat("BrushSize", brushSize);
        sandCompute.SetVector("SandColor", sandColor);
        sandCompute.SetBool("RandomSandSaturation", randomSandSaturation);
        sandCompute.SetBool("RandomSandColor", randomSandColor);
        sandCompute.SetFloat("RandomSeed", _spawnRandomSeed);
        sandCompute.SetFloat("RandomColorHue", _currentRandomColorHue);
        
        sandCompute.SetInt("Width", _width);
        sandCompute.SetInt("Height", _height);
        
        sandCompute.SetBool("IsBurst", isBurst);
        sandCompute.SetBool("IsDragging", isDragging);

        int threadGroupX = Mathf.CeilToInt(_width / 8f);
        int threadGroupY = Mathf.CeilToInt(_height / 8f);
        sandCompute.Dispatch(_kernelIndex, threadGroupX, threadGroupY, 1);
    }

    private void UpdateRandomColorHue()
    {
        float duration = Mathf.Max(0.5f, randomColorTransitionDuration);
        _randomColorTransitionElapsed += Time.deltaTime;

        while (_randomColorTransitionElapsed >= duration)
        {
            _randomColorTransitionElapsed -= duration;
            _randomColorStartHue = _randomColorTargetHue;
            _randomColorTargetHue = ChooseNextRandomHue(_randomColorStartHue);
        }

        float progress = Mathf.SmoothStep(
            0f,
            1f,
            _randomColorTransitionElapsed / duration);
        float shortestHueDistance = Mathf.DeltaAngle(
            _randomColorStartHue * 360f,
            _randomColorTargetHue * 360f) / 360f;

        _currentRandomColorHue = Mathf.Repeat(
            _randomColorStartHue + shortestHueDistance * progress,
            1f);
    }

    private static float ChooseNextRandomHue(float currentHue)
    {
        const float minimumHueDistance = 0.15f;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            float candidate = Random.value;
            float distance = Mathf.Abs(Mathf.DeltaAngle(
                currentHue * 360f,
                candidate * 360f) / 360f);

            if (distance >= minimumHueDistance)
            {
                return candidate;
            }
        }

        // Guarantee a visible change even if all random attempts were too close.
        return Mathf.Repeat(currentHue + Random.Range(0.25f, 0.75f), 1f);
    }

    public void SetRandomSandSaturation(bool enabled)
    {
        randomSandSaturation = enabled;
    }

    public void SetRandomSandColor(bool enabled)
    {
        randomSandColor = enabled;
    }

    public void SetSandColor(Color color)
    {
        sandColor = new Color(
            Mathf.Clamp01(color.r),
            Mathf.Clamp01(color.g),
            Mathf.Clamp01(color.b),
            1f);
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;

        if (!enabled)
        {
            _pointerPressed = false;
            _pendingBurst = false;
        }
    }

    public void QueueSandBurst(Vector2 screenPosition)
    {
        if (!_inputEnabled ||
            !TryConvertToTexturePosition(screenPosition, out Vector2 texturePosition))
        {
            return;
        }

        _pointerTexturePosition = texturePosition;
        _pendingBurstPosition = texturePosition;
        _pendingBurst = true;
    }

    private bool TryConvertToTexturePosition(
        Vector2 screenPosition,
        out Vector2 texturePosition)
    {
        texturePosition = default;

        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        if (screenWidth <= 0 || screenHeight <= 0 || _width <= 0 || _height <= 0)
        {
            return false;
        }

        if (screenPosition.x < 0 || screenPosition.x >= screenWidth ||
            screenPosition.y < 0 || screenPosition.y >= screenHeight)
        {
            return false;
        }

        // The Editor can scale the Game view independently from the render texture.
        // Normalize from Game-view coordinates into sand-texture coordinates.
        texturePosition = new Vector2(
            screenPosition.x * _width / screenWidth,
            screenPosition.y * _height / screenHeight);
        return true;
    }

    public void ClearSand()
    {
        _simulationAccumulator = 0f;
        _pointerPressed = false;
        _pendingBurst = false;

        if (_sandTexture == null || !_sandTexture.IsCreated())
        {
            return;
        }

        ClearTexture();
    }

    public void SetResolution(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"Invalid sand resolution: {width}x{height}.");
            return;
        }

        if (_width == width && _height == height)
        {
            return;
        }

        _width = width;
        _height = height;

        // Before Start, only cache the dimensions. If already running, apply them now.
        if (_sandTexture != null)
        {
            CreateSandTexture();
        }
    }

    private bool CreateSandTexture()
    {
        _simulationAccumulator = 0f;
        _pointerPressed = false;
        _pendingBurst = false;

        ReleaseSandTexture();

        _sandTexture = new RenderTexture(_width, _height, 0)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            name = $"SandTexture_{_width}x{_height}"
        };

        if (!_sandTexture.Create())
        {
            Debug.LogError($"Failed to create sand RenderTexture at {_width}x{_height}.");
            ReleaseSandTexture();
            return false;
        }

        displayImage.texture = _sandTexture;
        ClearTexture();
        return true;
    }

    void ClearTexture()
    {
        RenderTexture previousTexture = RenderTexture.active;
        RenderTexture.active = _sandTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = previousTexture;
    }

    private void ReleaseSandTexture()
    {
        if (_sandTexture == null) return;

        if (displayImage != null && displayImage.texture == _sandTexture)
        {
            displayImage.texture = null;
        }

        if (_sandTexture.IsCreated())
        {
            _sandTexture.Release();
        }

        Destroy(_sandTexture);
        _sandTexture = null;
    }

    private void OnDestroy()
    {
        ReleaseSandTexture();
    }
}
