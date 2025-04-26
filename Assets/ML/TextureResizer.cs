using UnityEngine;

/// <summary>
/// Utility class for resizing textures for AI models
/// </summary>
public static class TextureResizer
{
    /// <summary>
    /// Resize a texture to the specified dimensions
    /// </summary>
    /// <param name="source">Source texture</param>
    /// <param name="width">Target width</param>
    /// <param name="height">Target height</param>
    /// <returns>Resized texture</returns>
    public static Texture2D ResizeTexture(Texture2D source, int width, int height)
    {
        // Create a temporary RenderTexture with the target dimensions
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        
        // Remember the active render texture
        RenderTexture prevRT = RenderTexture.active;
        
        // Blit the source texture to the temporary render texture
        Graphics.Blit(source, rt);
        
        // Set the temporary RT as active
        RenderTexture.active = rt;
        
        // Create a new texture and read pixels from the active RT
        #if UNITY_2022_1_OR_NEWER
        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        #else
        Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
        #endif
        resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        resized.Apply();
        
        // Restore the active render texture
        RenderTexture.active = prevRT;
        
        // Release the temporary RT
        RenderTexture.ReleaseTemporary(rt);
        
        return resized;
    }
    
    /// <summary>
    /// Resize a RenderTexture to the specified dimensions
    /// </summary>
    /// <param name="source">Source RenderTexture</param>
    /// <param name="width">Target width</param>
    /// <param name="height">Target height</param>
    /// <returns>Resized RenderTexture</returns>
    public static RenderTexture ResizeRenderTexture(RenderTexture source, int width, int height)
    {
        // Create a new RenderTexture with the target dimensions
        RenderTexture rt = new RenderTexture(width, height, 0, source.format);
        rt.enableRandomWrite = true;
        rt.Create();
        
        // Blit the source to the new render texture
        Graphics.Blit(source, rt);
        
        return rt;
    }
    
    /// <summary>
    /// Resize camera texture to the specified dimensions for AI processing
    /// </summary>
    /// <param name="cameraTexture">Source camera texture</param>
    /// <param name="targetWidth">Target width (e.g. 512 for DeepLab)</param>
    /// <param name="targetHeight">Target height (e.g. 512 for DeepLab)</param>
    /// <returns>Resized texture ready for processing</returns>
    public static Texture2D ResizeCameraTexture(Texture cameraTexture, int targetWidth, int targetHeight)
    {
        // Create a temporary render texture for blitting
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
        
        // Remember the active render texture
        RenderTexture prevRT = RenderTexture.active;
        
        // Blit the camera texture to the temporary RT
        Graphics.Blit(cameraTexture, rt);
        
        // Set the temporary RT as active
        RenderTexture.active = rt;
        
        // Create a new texture and read pixels
        #if UNITY_2022_1_OR_NEWER
        Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false, false);
        #else
        Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        #endif
        resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        resized.Apply();
        
        // Restore the active render texture
        RenderTexture.active = prevRT;
        
        // Release the temporary RT
        RenderTexture.ReleaseTemporary(rt);
        
        return resized;
    }
} 