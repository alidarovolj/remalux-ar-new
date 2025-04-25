using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Improved component to render AR camera feed to a RawImage
/// </summary>
[RequireComponent(typeof(RawImage))]
public class ARBackgroundToRawImage : MonoBehaviour
{
    [SerializeField] private Camera arCamera;
    [SerializeField] private ARCameraManager cameraManager;
    
    private RawImage rawImage;
    private ARCameraBackground cameraBackground;
    private Material arBackgroundMaterial;
    
    void Start()
    {
        rawImage = GetComponent<RawImage>();
        
        // Find components if not assigned
        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
            if (cameraManager != null)
            {
                arCamera = cameraManager.GetComponent<Camera>();
                cameraBackground = cameraManager.GetComponent<ARCameraBackground>();
                Debug.Log("ARBackgroundToRawImage: Found ARCameraManager and components");
            }
            else
            {
                // Try to find camera from XROrigin
                var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
                if (xrOrigin != null && xrOrigin.Camera != null)
                {
                    arCamera = xrOrigin.Camera;
                    cameraManager = arCamera.GetComponent<ARCameraManager>();
                    cameraBackground = arCamera.GetComponent<ARCameraBackground>();
                    Debug.Log("ARBackgroundToRawImage: Found AR Camera from XROrigin");
                }
            }
        }
        else if (arCamera == null && cameraManager != null)
        {
            arCamera = cameraManager.GetComponent<Camera>();
            cameraBackground = cameraManager.GetComponent<ARCameraBackground>();
        }
        
        if (arCamera == null)
        {
            Debug.LogError("ARBackgroundToRawImage: Could not find AR Camera");
            return;
        }
        
        if (cameraBackground == null && arCamera != null)
        {
            cameraBackground = arCamera.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                Debug.LogWarning("ARBackgroundToRawImage: No ARCameraBackground found on AR Camera");
            }
        }
        
        // Subscribe to frame event if available
        if (cameraManager != null)
        {
            cameraManager.frameReceived += OnFrameReceived;
        }
        
        Debug.Log("ARBackgroundToRawImage: Initialization complete");
    }
    
    void OnDisable()
    {
        if (cameraManager != null)
        {
            cameraManager.frameReceived -= OnFrameReceived;
        }
    }
    
    private void OnFrameReceived(ARCameraFrameEventArgs args)
    {
        UpdateRawImage();
    }
    
    void Update()
    {
        // Update every frame even if there's no callback
        UpdateRawImage();
    }
    
    void UpdateRawImage()
    {
        if (rawImage == null) return;
        
        Texture texture = null;
        
        // Get texture from ARCameraBackground if available
        if (cameraBackground != null && cameraBackground.material != null)
        {
            // Most reliable path - directly get the material's texture
            if (cameraBackground.material.mainTexture != null)
            {
                texture = cameraBackground.material.mainTexture;
            }
            // Try common property names
            else if (cameraBackground.material.HasProperty("_MainTex"))
            {
                texture = cameraBackground.material.GetTexture("_MainTex");
            }
            else if (cameraBackground.material.HasProperty("_CameraColorTexture"))
            {
                texture = cameraBackground.material.GetTexture("_CameraColorTexture");
            }
            else if (cameraBackground.material.HasProperty("_BackgroundTexture"))
            {
                texture = cameraBackground.material.GetTexture("_BackgroundTexture");
            }
            
            // Store material reference for debug purposes
            arBackgroundMaterial = cameraBackground.material;
            
            // Log all properties in debug mode
            #if UNITY_EDITOR
            if (texture == null)
            {
                Debug.Log("ARBackgroundToRawImage: Trying to read material properties:");
                Shader shader = cameraBackground.material.shader;
                if (shader != null)
                {
                    int count = shader.GetPropertyCount();
                    for (int i = 0; i < count; i++)
                    {
                        string name = shader.GetPropertyName(i);
                        Debug.Log($"Property: {name}, Type: {shader.GetPropertyType(i)}");
                    }
                }
            }
            #endif
        }
        
        // If still no texture, try get one from the frame
        if (texture == null && cameraManager != null)
        {
            // Try to get the texture from XR subsystem
            if (cameraManager.subsystem != null && cameraManager.subsystem.running)
            {
                Debug.Log("ARBackgroundToRawImage: Trying to get texture from subsystem");
            }
        }
        
        // If we found a texture, use it
        if (texture != null && rawImage.texture != texture)
        {
            rawImage.texture = texture;
            Debug.Log($"ARBackgroundToRawImage: Updated texture - {texture.width}x{texture.height}");
        }
        else if (rawImage.texture == null)
        {
            // Set a fallback texture if nothing is available
            rawImage.texture = Texture2D.blackTexture;
            Debug.LogWarning("ARBackgroundToRawImage: Using black texture fallback");
        }
    }
    
    void OnGUI()
    {
        // Show debug info in editor
        #if UNITY_EDITOR
        if (!Application.isPlaying) return;
        
        GUILayout.BeginArea(new Rect(10, 40, 300, 200));
        GUILayout.Label("AR Background Debugger");
        GUILayout.Label($"Camera: {(arCamera != null ? arCamera.name : "None")}");
        GUILayout.Label($"Background: {(cameraBackground != null ? "Found" : "Missing")}");
        GUILayout.Label($"Material: {(arBackgroundMaterial != null ? arBackgroundMaterial.name : "None")}");
        GUILayout.Label($"RawImage texture: {(rawImage.texture != null ? rawImage.texture.width + "x" + rawImage.texture.height : "None")}");
        GUILayout.EndArea();
        #endif
    }
} 