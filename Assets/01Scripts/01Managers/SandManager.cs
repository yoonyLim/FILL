using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class SandManager : MonoBehaviour
{
    public ComputeShader sandCompute;
    public RawImage displayImage; 
    public float brushSize = 10f;

    private RenderTexture _sandTexture;
    private int _kernelIndex;
    private int _width;
    private int _height;

    void Start()
    {
        // 1. Match Resolution to Screen
        _width = Screen.width;
        _height = Screen.height;

        // 2. Setup RenderTexture to match display exactly
        _sandTexture = new RenderTexture(_width, _height, 0);
        _sandTexture.enableRandomWrite = true;
        _sandTexture.filterMode = FilterMode.Point;
        _sandTexture.Create();

        // 3. Link to UI
        displayImage.texture = _sandTexture;
        _kernelIndex = sandCompute.FindKernel("Update");
        
        // Clear the texture to black/transparent initially
        ClearTexture();
    }

    void Update()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        
        // DRIZZLE LOGIC: Only true on the frame the button is pressed
        bool isBurstFrame = Mouse.current.leftButton.wasPressedThisFrame;
        bool isDragging = Mouse.current.leftButton.isPressed && !isBurstFrame;

        // Convert screen space to texture space
        float x = mousePos.x;
        float y = mousePos.y;

        // Safety check: Ensure mouse is actually within the window bounds
        if (x < 0 || x >= _width || y < 0 || y >= _height) return;

        // Pass data to GPU
        sandCompute.SetTexture(_kernelIndex, "Result", _sandTexture);
        sandCompute.SetVector("MousePos", new Vector2(x, y));
        sandCompute.SetFloat("BrushSize", brushSize);
        
        sandCompute.SetInt("Width", _width);
        sandCompute.SetInt("Height", _height);
        
        sandCompute.SetBool("IsBurst", isBurstFrame);
        sandCompute.SetBool("IsDragging", isDragging);

        int threadGroupX = Mathf.CeilToInt(_width / 8f);
        int threadGroupY = Mathf.CeilToInt(_height / 8f);
        sandCompute.Dispatch(_kernelIndex, threadGroupX, threadGroupY, 1);
    }

    void ClearTexture()
    {
        // Optional: Initialize texture with empty pixels
        RenderTexture.active = _sandTexture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
    }
}