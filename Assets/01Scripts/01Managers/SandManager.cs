using UnityEngine;
using UnityEngine.UI;

public class SandManager : MonoBehaviour
{
    public ComputeShader sandCompute;
    public RawImage displayImage; // Assign a UI RawImage to see the result
    public int resolution = 2560;
    public float brushSize = 1f;

    private RenderTexture _sandTexture;
    private int _kernelIndex;

    void Start()
    {
        // 1. Setup RenderTexture
        _sandTexture = new RenderTexture(2560, 1440, 0);
        _sandTexture.enableRandomWrite = true;
        _sandTexture.filterMode = FilterMode.Point; // Keep pixels sharp
        _sandTexture.Create();

        // 2. Link to UI
        displayImage.texture = _sandTexture;
        _kernelIndex = sandCompute.FindKernel("Update");
    }

    void Update()
    {
        // Convert Mouse Position to Texture Space
        Vector2 localCursor;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            displayImage.rectTransform, Input.mousePosition, null, out localCursor);

        // Normalize cursor to 0-1 and scale to resolution
        float x = (localCursor.x / displayImage.rectTransform.rect.width + 0.5f) * resolution;
        float y = (localCursor.y / displayImage.rectTransform.rect.height + 0.5f) * resolution;

        // Pass data to GPU
        sandCompute.SetTexture(_kernelIndex, "Result", _sandTexture);
        sandCompute.SetInt("Width", resolution);
        sandCompute.SetInt("Height", resolution);
        sandCompute.SetVector("MousePos", new Vector2(x, y));
        sandCompute.SetBool("IsMouseDown", Input.GetMouseButton(0));
        sandCompute.SetFloat("BrushSize", brushSize);

        // Dispatch (Execute the shader)
        // Groups of 8x8 as defined in the [numthreads] section of the shader
        int groups = Mathf.CeilToInt(resolution / 8f);
        sandCompute.Dispatch(_kernelIndex, groups, groups, 1);
    }
}