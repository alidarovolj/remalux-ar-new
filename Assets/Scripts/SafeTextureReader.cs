using UnityEngine;
using System.Collections;
using System;

/// <summary>
/// Provides a safe way to capture screen pixels at the right time in the rendering cycle
/// </summary>
public class SafeTextureReader : MonoBehaviour
{
    private static SafeTextureReader _instance;
    public static SafeTextureReader Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("SafeTextureReader");
                _instance = go.AddComponent<SafeTextureReader>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Safely captures screen pixels at the end of frame
    /// </summary>
    /// <param name="rect">Screen rectangle to capture</param>
    /// <param name="format">Texture format to use</param>
    /// <param name="callback">Callback to receive the captured texture</param>
    public void CaptureScreenSafe(Rect rect, TextureFormat format, Action<Texture2D> callback)
    {
        StartCoroutine(CaptureScreenCoroutine(rect, format, callback));
    }
    
    /// <summary>
    /// Waits for end of frame before capturing pixels
    /// </summary>
    private IEnumerator CaptureScreenCoroutine(Rect rect, TextureFormat format, Action<Texture2D> callback)
    {
        // Wait until end of frame to ensure rendering is complete
        yield return new WaitForEndOfFrame();
        
        try
        {
            // Now it's safe to read pixels
            Texture2D texture = new Texture2D((int)rect.width, (int)rect.height, format, false);
            texture.ReadPixels(rect, 0, 0);
            texture.Apply();
            
            // Invoke callback with the captured texture
            callback?.Invoke(texture);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error capturing screen: {e.Message}");
            callback?.Invoke(null);
        }
    }
    
    /// <summary>
    /// Safely captures the entire screen at the end of frame
    /// </summary>
    /// <param name="callback">Callback to receive the captured texture</param>
    public void CaptureFullScreenSafe(Action<Texture2D> callback)
    {
        Rect rect = new Rect(0, 0, Screen.width, Screen.height);
        CaptureScreenSafe(rect, TextureFormat.RGB24, callback);
    }
} 