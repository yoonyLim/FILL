using System.Collections.Generic;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SandTextEffect : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerMoveHandler,
    IPointerDownHandler,
    IPointerUpHandler
{
    private const int ParticleThreadCount = 64;
    private const int TextureThreadCount = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleData
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 HomePosition;
        public Vector4 Color;
        public uint Active;
        public float Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextParticleSeed
    {
        public Vector2 HomePosition;
        public Vector2 AtlasUv;
        public Vector4 Color;
        public uint Active;
        public float Padding;
    }

    private readonly struct GlyphQuad
    {
        public readonly TMP_Vertex BottomLeft;
        public readonly TMP_Vertex TopLeft;
        public readonly TMP_Vertex TopRight;
        public readonly TMP_Vertex BottomRight;
        public readonly float CumulativeArea;

        public GlyphQuad(TMP_CharacterInfo character, float cumulativeArea)
        {
            BottomLeft = character.vertex_BL;
            TopLeft = character.vertex_TL;
            TopRight = character.vertex_TR;
            BottomRight = character.vertex_BR;
            CumulativeArea = cumulativeArea;
        }
    }

    [Header("References")]
    [SerializeField] private ComputeShader sandTextCompute;
    [Tooltip("Optional for Image and TextMeshProUGUI sources; their texture data is used automatically.")]
    [SerializeField] private Texture maskTexture;
    [SerializeField] private RawImage outputImage;
    [Tooltip("The invisible UI Graphic that receives pointer events. Defaults to this GameObject's Graphic.")]
    [SerializeField] private Graphic sourceGraphic;
    [Tooltip("Defaults to this GameObject's RectTransform.")]
    [SerializeField] private RectTransform interactionRect;

    [Header("Particle Mask")]
    [Min(256)] [SerializeField] private int particleCount = 16000;
    [Tooltip("Particle texture resolution occupied by the original logo or text, before edge padding is added.")]
    [SerializeField] private Vector2Int outputResolution = new(597, 236);
    [Tooltip("Empty space added around every edge in UI units. The output RawImage grows, while the logo or text keeps its original size.")]
    [SerializeField] private Vector2 edgePadding = new(64f, 64f);
    [SerializeField] private bool expandOutputRectForPadding = true;
    [Range(0f, 1f)] [SerializeField] private float maskAlphaThreshold = 0.08f;
    [Tooltip("TextMeshPro glyphs normally use a 0.5 signed-distance-field edge threshold.")]
    [Range(0f, 1f)] [SerializeField] private float textMaskAlphaThreshold = 0.5f;
    [SerializeField] private bool useMaskColor = true;
    [SerializeField] private Color particleTint = Color.white;
    [Range(0f, 0.5f)] [SerializeField] private float brightnessVariation = 0.08f;
    [Range(0, 3)] [SerializeField] private float grainRadius;
    [SerializeField] private int randomSeed = 1337;
    [SerializeField] private bool flipMaskVertically;
    [SerializeField] private bool hideSourceGraphic = true;

    [Header("Motion (render-texture pixels)")]
    [Min(1f)] [SerializeField] private float scatterRadius = 90f;
    [Min(0f)] [SerializeField] private float scatterStrength = 900f;
    [Min(0f)] [SerializeField] private float returnStrength = 18f;
    [Range(0f, 1f)] [SerializeField] private float hoverHomeStrength = 0.08f;
    [Min(0f)] [SerializeField] private float velocityDamping = 4f;
    [Min(0f)] [SerializeField] private float gravity = 140f;
    [Min(0f)] [SerializeField] private float turbulenceStrength = 80f;
    [Min(0.0001f)] [SerializeField] private float turbulenceScale = 0.035f;
    [Min(1f)] [SerializeField] private float maxSpeed = 700f;
    [Min(0.01f)] [SerializeField] private float hoverTransitionSpeed = 7f;

    private ComputeBuffer _particleBuffer;
    private ComputeBuffer _textSeedBuffer;
    private RenderTexture _outputTexture;
    private Texture _activeMaskTexture;
    private TMP_Text _sourceText;
    private Canvas _canvas;
    private Vector2Int _renderResolution;
    private Vector2 _contentPixelOrigin;
    private Vector2 _contentPixelSize;
    private Color _sourceGraphicColor;
    private Vector4 _maskUvRect = new(0f, 0f, 1f, 1f);
    private Vector2 _pointerTexturePosition;
    private float _hoverAmount;
    private bool _pointerInside;
    private bool _pointerPressed;
    private bool _sourceGraphicHidden;
    private bool _usesTextMeshMask;
    private bool _initialized;

    private int _initializeKernel;
    private int _initializeTextKernel;
    private int _clearKernel;
    private int _updateKernel;
    private int _drawKernel;

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int TextSeedsId = Shader.PropertyToID("_TextSeeds");
    private static readonly int MaskTextureId = Shader.PropertyToID("_MaskTexture");
    private static readonly int OutputTextureId = Shader.PropertyToID("_OutputTexture");
    private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
    private static readonly int OutputSizeId = Shader.PropertyToID("_OutputSize");
    private static readonly int MaskSizeId = Shader.PropertyToID("_MaskSize");
    private static readonly int ContentPixelRectId = Shader.PropertyToID("_ContentPixelRect");
    private static readonly int MaskUvRectId = Shader.PropertyToID("_MaskUvRect");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int MaskAlphaThresholdId = Shader.PropertyToID("_MaskAlphaThreshold");
    private static readonly int BrightnessVariationId = Shader.PropertyToID("_BrightnessVariation");
    private static readonly int RandomSeedId = Shader.PropertyToID("_RandomSeed");
    private static readonly int UseMaskColorId = Shader.PropertyToID("_UseMaskColor");
    private static readonly int FlipMaskYId = Shader.PropertyToID("_FlipMaskY");
    private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
    private static readonly int ElapsedTimeId = Shader.PropertyToID("_ElapsedTime");
    private static readonly int PointerPositionId = Shader.PropertyToID("_PointerPosition");
    private static readonly int ScatterAmountId = Shader.PropertyToID("_ScatterAmount");
    private static readonly int ScatterRadiusId = Shader.PropertyToID("_ScatterRadius");
    private static readonly int ScatterStrengthId = Shader.PropertyToID("_ScatterStrength");
    private static readonly int ReturnStrengthId = Shader.PropertyToID("_ReturnStrength");
    private static readonly int HoverHomeStrengthId = Shader.PropertyToID("_HoverHomeStrength");
    private static readonly int VelocityDampingId = Shader.PropertyToID("_VelocityDamping");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int TurbulenceStrengthId = Shader.PropertyToID("_TurbulenceStrength");
    private static readonly int TurbulenceScaleId = Shader.PropertyToID("_TurbulenceScale");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int GrainRadiusId = Shader.PropertyToID("_GrainRadius");

    private void Awake()
    {
        ResolveAutomaticReferences();
        _canvas = GetComponentInParent<Canvas>();
    }

    private void Start()
    {
        InitializeEffect();
    }

    private void Update()
    {
        if (!_initialized)
        {
            return;
        }

        float targetHoverAmount = _pointerInside || _pointerPressed ? 1f : 0f;
        _hoverAmount = Mathf.MoveTowards(
            _hoverAmount,
            targetHoverAmount,
            hoverTransitionSpeed * Time.unscaledDeltaTime);

        DispatchFrame(Time.unscaledDeltaTime);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        UpdatePointerPosition(eventData);

        // Touch begins scattering on pointer-down because a touchscreen has no
        // persistent hover state. The legacy event module uses negative mouse IDs.
        _pointerInside = eventData is ExtendedPointerEventData extendedEventData
            ? extendedEventData.pointerType != UIPointerType.Touch
            : eventData.pointerId < 0;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _pointerInside = false;
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        UpdatePointerPosition(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerPressed = true;
        UpdatePointerPosition(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _pointerPressed = false;
        UpdatePointerPosition(eventData);
    }

    [ContextMenu("Reinitialize Sand Text Effect")]
    public void ReinitializeEffect()
    {
        RestoreSourceGraphic();
        ReleaseResources();
        InitializeEffect();
    }

    private void InitializeEffect()
    {
        if (_initialized || !isActiveAndEnabled)
        {
            return;
        }

        ResolveAutomaticReferences();

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("Sand text requires compute-shader support.", this);
            return;
        }

        if (sandTextCompute == null || outputImage == null || interactionRect == null)
        {
            Debug.LogError(
                "SandTextEffect requires a ComputeShader, Output Image, and Interaction Rect.",
                this);
            return;
        }

        if (!TryResolveMaskTexture())
        {
            Debug.LogError(
                "Assign a mask texture, or attach SandTextEffect to an Image or TextMeshProUGUI component.",
                this);
            return;
        }

        outputResolution.x = Mathf.Max(TextureThreadCount, outputResolution.x);
        outputResolution.y = Mathf.Max(TextureThreadCount, outputResolution.y);
        particleCount = Mathf.Max(256, particleCount);
        ConfigurePaddedOutput();

        _initializeKernel = sandTextCompute.FindKernel("InitializeParticles");
        _initializeTextKernel = sandTextCompute.FindKernel("InitializeTextParticles");
        _clearKernel = sandTextCompute.FindKernel("ClearOutput");
        _updateKernel = sandTextCompute.FindKernel("UpdateParticles");
        _drawKernel = sandTextCompute.FindKernel("DrawParticles");

        _particleBuffer = new ComputeBuffer(
            particleCount,
            Marshal.SizeOf<ParticleData>(),
            ComputeBufferType.Structured);

        if (_usesTextMeshMask)
        {
            if (!TryBuildTextParticleSeeds(out TextParticleSeed[] textSeeds))
            {
                Debug.LogError(
                    "SandTextEffect could not find visible TextMeshPro glyphs using one font atlas.",
                    this);
                ReleaseResources();
                return;
            }

            _textSeedBuffer = new ComputeBuffer(
                particleCount,
                Marshal.SizeOf<TextParticleSeed>(),
                ComputeBufferType.Structured);
            _textSeedBuffer.SetData(textSeeds);
        }

        _outputTexture = new RenderTexture(
            _renderResolution.x,
            _renderResolution.y,
            0,
            RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = $"SandText_{name}_{_renderResolution.x}x{_renderResolution.y}"
        };

        if (!_outputTexture.Create())
        {
            Debug.LogError("Failed to create the sand-text RenderTexture.", this);
            ReleaseResources();
            return;
        }

        outputImage.texture = _outputTexture;
        outputImage.raycastTarget = false;

        BindSharedResources();
        DispatchInitialization();
        DispatchClear();
        DispatchDraw();

        if (hideSourceGraphic && sourceGraphic != null)
        {
            _sourceGraphicColor = sourceGraphic.color;
            Color hiddenColor = _sourceGraphicColor;
            hiddenColor.a = 0f;
            sourceGraphic.color = hiddenColor;
            _sourceGraphicHidden = true;
        }

        _initialized = true;
    }

    private bool TryResolveMaskTexture()
    {
        _sourceText = null;
        _usesTextMeshMask = false;
        _activeMaskTexture = maskTexture;
        _maskUvRect = new Vector4(0f, 0f, 1f, 1f);

        if (_activeMaskTexture != null)
        {
            return true;
        }

        if (sourceGraphic is TMP_Text sourceText)
        {
            sourceText.ForceMeshUpdate(true, true);
            TMP_TextInfo textInfo = sourceText.textInfo;

            for (int index = 0; index < textInfo.characterCount; index++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[index];
                if (!character.isVisible || character.material == null)
                {
                    continue;
                }

                Texture atlas = character.material.mainTexture;
                if (atlas == null)
                {
                    continue;
                }

                _sourceText = sourceText;
                _usesTextMeshMask = true;
                _activeMaskTexture = atlas;
                return true;
            }

            return false;
        }

        if (sourceGraphic is not Image sourceImage || sourceImage.sprite == null)
        {
            return false;
        }

        Sprite sprite = sourceImage.sprite;
        Texture2D spriteTexture = sprite.texture;
        // textureRect can describe a tight/trimmed area and therefore discard
        // intentional transparent borders. Unpacked sprites should sample their
        // authored rect so alpha padding remains part of the particle mask.
        Rect spriteRect = sprite.packed ? sprite.textureRect : sprite.rect;

        _activeMaskTexture = spriteTexture;
        _maskUvRect = new Vector4(
            spriteRect.x / spriteTexture.width,
            spriteRect.y / spriteTexture.height,
            spriteRect.width / spriteTexture.width,
            spriteRect.height / spriteTexture.height);
        return true;
    }

    private void ConfigurePaddedOutput()
    {
        Rect contentRect = interactionRect.rect;
        float contentWidth = Mathf.Max(1f, contentRect.width);
        float contentHeight = Mathf.Max(1f, contentRect.height);
        Vector2 safePadding = new(
            Mathf.Max(0f, edgePadding.x),
            Mathf.Max(0f, edgePadding.y));

        Vector2Int paddingPixels = new(
            Mathf.CeilToInt(safePadding.x * outputResolution.x / contentWidth),
            Mathf.CeilToInt(safePadding.y * outputResolution.y / contentHeight));

        int maximumTextureSize = Mathf.Max(TextureThreadCount, SystemInfo.maxTextureSize);
        paddingPixels.x = Mathf.Min(
            paddingPixels.x,
            Mathf.Max(0, (maximumTextureSize - outputResolution.x) / 2));
        paddingPixels.y = Mathf.Min(
            paddingPixels.y,
            Mathf.Max(0, (maximumTextureSize - outputResolution.y) / 2));

        _contentPixelOrigin = paddingPixels;
        _contentPixelSize = outputResolution;
        _renderResolution = new Vector2Int(
            outputResolution.x + paddingPixels.x * 2,
            outputResolution.y + paddingPixels.y * 2);

        if (!expandOutputRectForPadding || outputImage == null ||
            outputImage.rectTransform == interactionRect)
        {
            return;
        }

        RectTransform outputRect = outputImage.rectTransform;
        outputRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            contentWidth + safePadding.x * 2f);
        outputRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            contentHeight + safePadding.y * 2f);

        Vector3 contentWorldCenter = interactionRect.TransformPoint(contentRect.center);
        Vector3 outputWorldCenter = outputRect.TransformPoint(outputRect.rect.center);
        outputRect.position += contentWorldCenter - outputWorldCenter;
    }

    private bool TryBuildTextParticleSeeds(out TextParticleSeed[] seeds)
    {
        seeds = null;
        if (_sourceText == null || interactionRect == null || _activeMaskTexture == null)
        {
            return false;
        }

        _sourceText.ForceMeshUpdate(true, true);
        TMP_TextInfo textInfo = _sourceText.textInfo;
        List<GlyphQuad> glyphs = new(textInfo.characterCount);
        float totalArea = 0f;

        for (int index = 0; index < textInfo.characterCount; index++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[index];
            if (!character.isVisible || character.material == null ||
                character.material.mainTexture != _activeMaskTexture)
            {
                continue;
            }

            float area = CalculateQuadArea(character);
            if (area <= Mathf.Epsilon)
            {
                continue;
            }

            totalArea += area;
            glyphs.Add(new GlyphQuad(character, totalArea));
        }

        Rect rect = interactionRect.rect;
        if (glyphs.Count == 0 || totalArea <= Mathf.Epsilon ||
            rect.width <= 0f || rect.height <= 0f)
        {
            return false;
        }

        seeds = new TextParticleSeed[particleCount];
        System.Random random = new(randomSeed);

        for (int index = 0; index < particleCount; index++)
        {
            float areaChoice = (float)random.NextDouble() * totalArea;
            GlyphQuad glyph = SelectGlyph(glyphs, areaChoice);
            float horizontal = (float)random.NextDouble();
            float vertical = (float)random.NextDouble();

            Vector2 localPosition = BilinearPosition(
                glyph,
                horizontal,
                vertical);
            Vector2 atlasUv = BilinearUv(
                glyph,
                horizontal,
                vertical);
            Color vertexColor = BilinearColor(
                glyph,
                horizontal,
                vertical);

            seeds[index] = new TextParticleSeed
            {
                HomePosition = new Vector2(
                    _contentPixelOrigin.x +
                        Mathf.InverseLerp(rect.xMin, rect.xMax, localPosition.x) *
                        (_contentPixelSize.x - 1),
                    _contentPixelOrigin.y +
                        Mathf.InverseLerp(rect.yMin, rect.yMax, localPosition.y) *
                        (_contentPixelSize.y - 1)),
                AtlasUv = atlasUv,
                Color = vertexColor,
                Active = 1,
                Padding = 0f
            };
        }

        return true;
    }

    private static float CalculateQuadArea(TMP_CharacterInfo character)
    {
        Vector2 bottomLeft = character.vertex_BL.position;
        Vector2 topLeft = character.vertex_TL.position;
        Vector2 topRight = character.vertex_TR.position;
        Vector2 bottomRight = character.vertex_BR.position;
        return TriangleArea(bottomLeft, topLeft, topRight) +
            TriangleArea(bottomLeft, topRight, bottomRight);
    }

    private static float TriangleArea(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 ab = b - a;
        Vector2 ac = c - a;
        return Mathf.Abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
    }

    private static GlyphQuad SelectGlyph(List<GlyphQuad> glyphs, float areaChoice)
    {
        int low = 0;
        int high = glyphs.Count - 1;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (areaChoice <= glyphs[middle].CumulativeArea)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return glyphs[low];
    }

    private static Vector2 BilinearPosition(
        GlyphQuad glyph,
        float horizontal,
        float vertical)
    {
        Vector2 bottom = Vector2.Lerp(
            glyph.BottomLeft.position,
            glyph.BottomRight.position,
            horizontal);
        Vector2 top = Vector2.Lerp(
            glyph.TopLeft.position,
            glyph.TopRight.position,
            horizontal);
        return Vector2.Lerp(bottom, top, vertical);
    }

    private static Vector2 BilinearUv(
        GlyphQuad glyph,
        float horizontal,
        float vertical)
    {
        Vector2 bottom = Vector2.Lerp(
            glyph.BottomLeft.uv,
            glyph.BottomRight.uv,
            horizontal);
        Vector2 top = Vector2.Lerp(
            glyph.TopLeft.uv,
            glyph.TopRight.uv,
            horizontal);
        return Vector2.Lerp(bottom, top, vertical);
    }

    private static Color BilinearColor(
        GlyphQuad glyph,
        float horizontal,
        float vertical)
    {
        Color bottom = Color.Lerp(
            glyph.BottomLeft.color,
            glyph.BottomRight.color,
            horizontal);
        Color top = Color.Lerp(
            glyph.TopLeft.color,
            glyph.TopRight.color,
            horizontal);
        return Color.Lerp(bottom, top, vertical);
    }

    private void BindSharedResources()
    {
        BindFrameResources();

        sandTextCompute.SetInts(
            MaskSizeId,
            _activeMaskTexture.width,
            _activeMaskTexture.height);
        sandTextCompute.SetVector(
            ContentPixelRectId,
            new Vector4(
                _contentPixelOrigin.x,
                _contentPixelOrigin.y,
                _contentPixelSize.x,
                _contentPixelSize.y));

        int initializationKernel = _usesTextMeshMask
            ? _initializeTextKernel
            : _initializeKernel;
        sandTextCompute.SetBuffer(initializationKernel, ParticlesId, _particleBuffer);
        sandTextCompute.SetTexture(initializationKernel, MaskTextureId, _activeMaskTexture);
        if (_usesTextMeshMask)
        {
            sandTextCompute.SetBuffer(
                _initializeTextKernel,
                TextSeedsId,
                _textSeedBuffer);
        }
    }

    private void BindFrameResources()
    {
        // ComputeShader property bindings are shared by every component using
        // the same asset. Rebind per dispatch so several sand images/texts can
        // animate independently in one scene.
        sandTextCompute.SetInt(ParticleCountId, particleCount);
        sandTextCompute.SetInts(
            OutputSizeId,
            _renderResolution.x,
            _renderResolution.y);
        sandTextCompute.SetBuffer(_updateKernel, ParticlesId, _particleBuffer);
        sandTextCompute.SetBuffer(_drawKernel, ParticlesId, _particleBuffer);
        sandTextCompute.SetTexture(_clearKernel, OutputTextureId, _outputTexture);
        sandTextCompute.SetTexture(_drawKernel, OutputTextureId, _outputTexture);
    }

    private void DispatchInitialization()
    {
        sandTextCompute.SetVector(MaskUvRectId, _maskUvRect);
        sandTextCompute.SetVector(TintColorId, particleTint);
        sandTextCompute.SetFloat(
            MaskAlphaThresholdId,
            _usesTextMeshMask ? textMaskAlphaThreshold : maskAlphaThreshold);
        sandTextCompute.SetFloat(BrightnessVariationId, brightnessVariation);
        sandTextCompute.SetFloat(RandomSeedId, randomSeed);
        sandTextCompute.SetInt(UseMaskColorId, useMaskColor ? 1 : 0);
        sandTextCompute.SetInt(FlipMaskYId, flipMaskVertically ? 1 : 0);
        sandTextCompute.Dispatch(
            _usesTextMeshMask ? _initializeTextKernel : _initializeKernel,
            Mathf.CeilToInt(particleCount / (float)ParticleThreadCount),
            1,
            1);
    }

    private void DispatchFrame(float deltaTime)
    {
        BindFrameResources();

        sandTextCompute.SetFloat(DeltaTimeId, Mathf.Min(deltaTime, 0.05f));
        sandTextCompute.SetFloat(ElapsedTimeId, Time.unscaledTime);
        sandTextCompute.SetVector(PointerPositionId, _pointerTexturePosition);
        sandTextCompute.SetFloat(ScatterAmountId, _hoverAmount);
        sandTextCompute.SetFloat(ScatterRadiusId, scatterRadius);
        sandTextCompute.SetFloat(ScatterStrengthId, scatterStrength);
        sandTextCompute.SetFloat(ReturnStrengthId, returnStrength);
        sandTextCompute.SetFloat(HoverHomeStrengthId, hoverHomeStrength);
        sandTextCompute.SetFloat(VelocityDampingId, velocityDamping);
        sandTextCompute.SetFloat(GravityId, gravity);
        sandTextCompute.SetFloat(TurbulenceStrengthId, turbulenceStrength);
        sandTextCompute.SetFloat(TurbulenceScaleId, turbulenceScale);
        sandTextCompute.SetFloat(MaxSpeedId, maxSpeed);
        sandTextCompute.SetFloat(GrainRadiusId, grainRadius);

        sandTextCompute.Dispatch(
            _updateKernel,
            Mathf.CeilToInt(particleCount / (float)ParticleThreadCount),
            1,
            1);
        DispatchClear();
        DispatchDraw();
    }

    private void DispatchClear()
    {
        sandTextCompute.Dispatch(
            _clearKernel,
            Mathf.CeilToInt(_renderResolution.x / (float)TextureThreadCount),
            Mathf.CeilToInt(_renderResolution.y / (float)TextureThreadCount),
            1);
    }

    private void DispatchDraw()
    {
        sandTextCompute.Dispatch(
            _drawKernel,
            Mathf.CeilToInt(particleCount / (float)ParticleThreadCount),
            1,
            1);
    }

    private void UpdatePointerPosition(PointerEventData eventData)
    {
        if (interactionRect == null)
        {
            return;
        }

        Camera eventCamera = eventData.pressEventCamera;
        if (eventCamera == null && _canvas != null &&
            _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            eventCamera = _canvas.worldCamera;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                interactionRect,
                eventData.position,
                eventCamera,
                out Vector2 localPosition))
        {
            return;
        }

        Rect rect = interactionRect.rect;
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        Vector2 normalizedPosition = new(
            Mathf.InverseLerp(rect.xMin, rect.xMax, localPosition.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, localPosition.y));
        _pointerTexturePosition = new Vector2(
            _contentPixelOrigin.x +
                normalizedPosition.x * (_contentPixelSize.x - 1),
            _contentPixelOrigin.y +
                normalizedPosition.y * (_contentPixelSize.y - 1));
    }

    private void OnDisable()
    {
        _pointerInside = false;
        _pointerPressed = false;
        _hoverAmount = 0f;
    }

    private void OnDestroy()
    {
        RestoreSourceGraphic();
        ReleaseResources();
    }

    private void RestoreSourceGraphic()
    {
        if (!_sourceGraphicHidden || sourceGraphic == null)
        {
            return;
        }

        sourceGraphic.color = _sourceGraphicColor;
        _sourceGraphicHidden = false;
    }

    private void ReleaseResources()
    {
        _initialized = false;

        if (outputImage != null && outputImage.texture == _outputTexture)
        {
            outputImage.texture = null;
        }

        _particleBuffer?.Release();
        _particleBuffer = null;

        _textSeedBuffer?.Release();
        _textSeedBuffer = null;

        if (_outputTexture != null)
        {
            if (_outputTexture.IsCreated())
            {
                _outputTexture.Release();
            }

            Destroy(_outputTexture);
            _outputTexture = null;
        }
    }

    private void OnValidate()
    {
        particleCount = Mathf.Max(256, particleCount);
        outputResolution.x = Mathf.Max(TextureThreadCount, outputResolution.x);
        outputResolution.y = Mathf.Max(TextureThreadCount, outputResolution.y);
        edgePadding.x = Mathf.Max(0f, edgePadding.x);
        edgePadding.y = Mathf.Max(0f, edgePadding.y);
        grainRadius = Mathf.Clamp(grainRadius, 0, 3);
        scatterRadius = Mathf.Max(1f, scatterRadius);
        turbulenceScale = Mathf.Max(0.0001f, turbulenceScale);
        maxSpeed = Mathf.Max(1f, maxSpeed);
        hoverTransitionSpeed = Mathf.Max(0.01f, hoverTransitionSpeed);
    }

    private void ResolveAutomaticReferences()
    {
        if (interactionRect == null)
        {
            interactionRect = GetComponent<RectTransform>();
        }

        if (sourceGraphic == null)
        {
            sourceGraphic = GetComponent<Graphic>();
        }
    }
}
