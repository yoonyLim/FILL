using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SandFillCapturer : MonoBehaviour
{
    public RawImage sandRawImage;
    public float fillPercentage;

    private Texture2D tempTex;
    
    void Start()
    {
        StartCoroutine(FillCheckRoutine());
    }
    
    IEnumerator FillCheckRoutine()
    {
        while (true) // Run forever while the script is active
        {
            CalculateFill();
            yield return new WaitForSeconds(0.2f); // Wait 0.2 seconds before next check
        }
    }

    public void CalculateFill()
    {
        RenderTexture rt = sandRawImage.texture as RenderTexture;
        
        if (rt == null)
        {
            Debug.LogWarning("RawImage does not have a RenderTexture assigned!");
            return;
        }
        
        if (tempTex == null || tempTex.width != rt.width || tempTex.height != rt.height)
        {
            tempTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        }
        
        RenderTexture.active = rt;
        tempTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tempTex.Apply();
        RenderTexture.active = null;

        Color[] pixels = tempTex.GetPixels();
        int filledCount = 0;
        
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].grayscale > 0.05f) 
            {
                filledCount++;
            }
        }

        fillPercentage = (float)filledCount / pixels.Length * 100f;
        Debug.Log($"Screen is {fillPercentage}% full of sand!");
    }
}
