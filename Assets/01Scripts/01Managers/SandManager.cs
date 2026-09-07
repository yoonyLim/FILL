using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

public class SandManager : MonoBehaviour
{
    public ComputeShader sandCompute;
    public RawImage displayImage; 
    public float brushSize = 10f;
    [Min(1f)] public float simulationStepsPerSecond = 60f;
    [Min(1)] public int maxSimulationStepsPerFrame = 8;
    [Min(1)] [SerializeField] private int sandFlowMoundingEventThreshold = 1;
    [Min(0.01f)] [SerializeField] private float sandFlowAudioGraceTime = 0.16f;

    [Header("Sand Appearance")]
    [SerializeField] private Color sandColor = new(0.8f, 0.7f, 0.4f, 1f); // rgb: 204, 178.5, 102
    [SerializeField] private bool randomSandSaturation;
    [SerializeField] private bool randomSandColor;
    [FormerlySerializedAs("randomColorCycleDuration")]
    [Min(0.5f)] [SerializeField] private float randomColorTransitionDuration = 4f;

    private RenderTexture _sandTexture;
    private ComputeBuffer _audioEventBuffer;
    private readonly uint[] _audioEventCounts = new uint[1];
    private Texture2D _debugFillTexture;
    private int _kernelIndex;
    private int _audioEventCountsId;
    private int _width;
    private int _height;
    private float _simulationAccumulator;
    private Vector2 _pointerTexturePosition;
    private Vector2 _pendingBurstPosition;
    private bool _pointerPressed;
    private bool _pendingBurst;
    private bool _inputEnabled = true;
    private bool _sandFlowPlaying;
    private bool _audioReadbackPending;
    private float _lastMoundingAudioTime = -999f;
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
        _audioEventCountsId = Shader.PropertyToID("AudioEventCounts");
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
#if UNITY_EDITOR
        CaptureDebugInput();
#endif

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

        UpdateSandFlowAudio();
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

#if UNITY_EDITOR
    private void CaptureDebugInput()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.digit1Key.wasPressedThisFrame ||
            Keyboard.current.numpad1Key.wasPressedThisFrame)
        {
            DebugFillSand(0.25f);
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame ||
                 Keyboard.current.numpad2Key.wasPressedThisFrame)
        {
            DebugFillSand(0.5f);
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame ||
                 Keyboard.current.numpad3Key.wasPressedThisFrame)
        {
            DebugFillSand(0.75f);
        }
        else if (Keyboard.current.digit4Key.wasPressedThisFrame ||
                 Keyboard.current.numpad4Key.wasPressedThisFrame)
        {
            DebugFillSand(0.995f);
        }
    }
#endif

    private void DispatchSimulation(Vector2 inputPosition, bool isBurst, bool isDragging)
    {
        if (isBurst || isDragging)
        {
            _spawnRandomSeed++;
            AudioManager.Instance?.NotifySandProduced();
        }

        // Pass data to GPU
        _audioEventCounts[0] = 0;
        _audioEventBuffer.SetData(_audioEventCounts);
        sandCompute.SetTexture(_kernelIndex, "Result", _sandTexture);
        sandCompute.SetBuffer(_kernelIndex, _audioEventCountsId, _audioEventBuffer);
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
        RequestAudioEventReadback();
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
            StopSandFlowAudio();
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
        StopSandFlowAudio();

        if (_sandTexture == null || !_sandTexture.IsCreated())
        {
            return;
        }

        ClearTexture();
    }

    public void DebugFillSand(float normalizedFill)
    {
#if UNITY_EDITOR
        SetDebugSandFill(normalizedFill);
#endif
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
        StopSandFlowAudio();

        ReleaseSandTexture();
        ReleaseAudioEventBuffer();

        _audioEventBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);

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

    private void SetDebugSandFill(float normalizedFill)
    {
        if (_sandTexture == null || !_sandTexture.IsCreated())
        {
            return;
        }

        normalizedFill = Mathf.Clamp01(normalizedFill);
        int filledRows = Mathf.RoundToInt(_height * normalizedFill);

        if (_debugFillTexture == null ||
            _debugFillTexture.width != _width ||
            _debugFillTexture.height != _height)
        {
            if (_debugFillTexture != null)
            {
                Destroy(_debugFillTexture);
            }

            _debugFillTexture = new Texture2D(
                _width,
                _height,
                TextureFormat.RGBA32,
                false);
        }

        Color[] pixels = new Color[_width * _height];
        Color fillColor = sandColor;
        fillColor.a = 1f;

        for (int y = 0; y < filledRows; y++)
        {
            int rowStart = y * _width;
            for (int x = 0; x < _width; x++)
            {
                pixels[rowStart + x] = fillColor;
            }
        }

        _debugFillTexture.SetPixels(pixels);
        _debugFillTexture.Apply(false);

        Graphics.Blit(_debugFillTexture, _sandTexture);
        _simulationAccumulator = 0f;
        _pendingBurst = false;
        _pointerPressed = false;
        StopSandFlowAudio();
        AudioManager.Instance?.RegisterFillPercentage(normalizedFill * 100f);
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

    private void ReleaseAudioEventBuffer()
    {
        _audioReadbackPending = false;
        _audioEventBuffer?.Release();
        _audioEventBuffer = null;
    }

    private void OnDestroy()
    {
        StopSandFlowAudio();
        ReleaseAudioEventBuffer();
        ReleaseSandTexture();

        if (_debugFillTexture != null)
        {
            Destroy(_debugFillTexture);
            _debugFillTexture = null;
        }
    }

    private void OnDisable()
    {
        StopSandFlowAudio();
    }

    private void OnValidate()
    {
        sandFlowMoundingEventThreshold = Mathf.Max(1, sandFlowMoundingEventThreshold);
        sandFlowAudioGraceTime = Mathf.Max(0.01f, sandFlowAudioGraceTime);
    }

    private void UpdateSandFlowAudio()
    {
        bool shouldPlay = Time.unscaledTime - _lastMoundingAudioTime <= sandFlowAudioGraceTime;

        if (shouldPlay)
        {
            if (!_sandFlowPlaying && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySandFlow();
                _sandFlowPlaying = true;
            }

            return;
        }

        StopSandFlowAudio();
    }

    private void StopSandFlowAudio()
    {
        if (!_sandFlowPlaying)
        {
            return;
        }

        AudioManager.Instance?.StopSandFlow();
        _sandFlowPlaying = false;
    }

    private void RequestAudioEventReadback()
    {
        if (_audioReadbackPending || _audioEventBuffer == null)
        {
            return;
        }

        _audioReadbackPending = true;
        AsyncGPUReadback.Request(_audioEventBuffer, OnAudioEventReadback);
    }

    private void OnAudioEventReadback(AsyncGPUReadbackRequest request)
    {
        _audioReadbackPending = false;

        if (request.hasError || _audioEventBuffer == null)
        {
            return;
        }

        uint moundingEvents = request.GetData<uint>()[0];
        if (moundingEvents >= sandFlowMoundingEventThreshold)
        {
            _lastMoundingAudioTime = Time.unscaledTime;
        }
    }
}
