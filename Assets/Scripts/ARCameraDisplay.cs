using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(RawImage))]
public class ARCameraDisplay : MonoBehaviour
{
    private RawImage rawImage;
    private ARCameraManager cameraManager;
    private ARCameraBackground cameraBackground;
    
    void Start()
    {
        rawImage = GetComponent<RawImage>();
        
        // Find components in scene
        cameraManager = FindObjectOfType<ARCameraManager>();
        
        if (cameraManager == null)
        {
            Debug.LogError("ARCameraDisplay: ARCameraManager not found in scene!");
            return;
        }
        
        cameraBackground = cameraManager.GetComponent<ARCameraBackground>();
        
        if (cameraBackground == null)
        {
            Debug.LogError("ARCameraDisplay: ARCameraBackground not found on ARCameraManager!");
            return;
        }
        
        Debug.Log("ARCameraDisplay: Successfully found all AR components");
    }
    
    void Update()
    {
        if (cameraBackground != null && cameraBackground.material != null)
        {
            Texture texture = null;
            
            // Try to get the texture from the material's mainTexture
            if (cameraBackground.material.mainTexture != null)
            {
                texture = cameraBackground.material.mainTexture;
            }
            // Try to get it from _MainTex property
            else if (cameraBackground.material.HasProperty("_MainTex"))
            {
                texture = cameraBackground.material.GetTexture("_MainTex");
            }
            // Try to get it from _CameraColorTexture property (used in some ARCore implementations)
            else if (cameraBackground.material.HasProperty("_CameraColorTexture"))
            {
                texture = cameraBackground.material.GetTexture("_CameraColorTexture");
            }
            
            // Try additional known property names if needed
            if (texture == null && cameraBackground.material.HasProperty("_BackgroundTexture"))
            {
                texture = cameraBackground.material.GetTexture("_BackgroundTexture");
            }
            
            if (texture != null && rawImage.texture != texture)
            {
                rawImage.texture = texture;
                Debug.Log("ARCameraDisplay: Updated RawImage texture");
            }
        }
    }
} 