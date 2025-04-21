using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;

/// <summary>
/// Processor for wall segmentation masks using OpenCV morphological operations
/// </summary>
public static class WallOpenCVProcessor
{
    /// <summary>
    /// Check if OpenCV is available in the project
    /// </summary>
    /// <returns>True if OpenCV is available, false otherwise</returns>
    public static bool IsOpenCVAvailable()
    {
        try
        {
            // Try to create a Mat object to see if OpenCV is available
            using (Mat testMat = new Mat(1, 1, CvType.CV_8UC1))
            {
                return true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"OpenCV is not available: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Apply morphological operations to wall mask to improve detection quality
    /// </summary>
    /// <param name="wallTexture">Input wall mask texture</param>
    /// <returns>Enhanced wall mask texture</returns>
    public static Texture2D EnhanceWallMask(Texture2D wallTexture)
    {
        if (wallTexture == null) return null;
        
        // Convert Texture2D to Mat for OpenCV processing
        Mat wallMat = new Mat(wallTexture.height, wallTexture.width, CvType.CV_8UC4);
        Utils.texture2DToMat(wallTexture, wallMat);
        
        // Convert to grayscale for morphological operations
        Mat grayMat = new Mat();
        Imgproc.cvtColor(wallMat, grayMat, Imgproc.COLOR_RGBA2GRAY);
        wallMat.release(); // Release original mat
        
        // Apply threshold to create binary mask
        Mat binaryMat = new Mat();
        Imgproc.threshold(grayMat, binaryMat, 127, 255, Imgproc.THRESH_BINARY);
        grayMat.release(); // Release grayscale mat
        
        // Create kernel for morphological operations
        Mat kernel = Imgproc.getStructuringElement(Imgproc.MORPH_RECT, new Size(3, 3));
        
        // Apply erosion to remove small noise
        Mat erodedMat = new Mat();
        Imgproc.erode(binaryMat, erodedMat, kernel, new Point(-1, -1), 1);
        binaryMat.release();
        
        // Apply dilation to restore wall size and close gaps
        Mat dilatedMat = new Mat();
        Imgproc.dilate(erodedMat, dilatedMat, kernel, new Point(-1, -1), 2);
        erodedMat.release();
        kernel.release();
        
        // Convert back to RGBA for Unity
        Mat resultMat = new Mat();
        Imgproc.cvtColor(dilatedMat, resultMat, Imgproc.COLOR_GRAY2RGBA);
        dilatedMat.release();
        
        // Convert back to Texture2D
        Texture2D resultTexture = new Texture2D(wallTexture.width, wallTexture.height, TextureFormat.RGBA32, false);
        Utils.matToTexture2D(resultMat, resultTexture);
        resultMat.release();
        
        return resultTexture;
    }
    
    /// <summary>
    /// Process a RenderTexture containing wall segmentation data
    /// </summary>
    /// <param name="wallRT">Input RenderTexture</param>
    /// <returns>Processed RenderTexture</returns>
    public static RenderTexture ProcessWallMask(RenderTexture wallRT)
    {
        if (wallRT == null) return null;
        
        // Convert RenderTexture to Texture2D
        Texture2D tempTexture = RenderTextureToTexture2D(wallRT);
        
        // Process the texture
        Texture2D processedTexture = EnhanceWallMask(tempTexture);
        Object.Destroy(tempTexture); // Clean up temp texture
        
        // Convert back to RenderTexture
        RenderTexture resultRT = new RenderTexture(wallRT.width, wallRT.height, 0, wallRT.format);
        Graphics.Blit(processedTexture, resultRT);
        Object.Destroy(processedTexture); // Clean up processed texture
        
        return resultRT;
    }
    
    /// <summary>
    /// Helper method to convert RenderTexture to Texture2D
    /// </summary>
    private static Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        // Store active render texture
        RenderTexture currentRT = RenderTexture.active;
        
        // Set the provided renderTexture as active
        RenderTexture.active = renderTexture;
        
        // Create a new Texture2D and read pixels from the active render texture
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new UnityEngine.Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        
        // Restore previous active render texture
        RenderTexture.active = currentRT;
        
        return texture;
    }
} 