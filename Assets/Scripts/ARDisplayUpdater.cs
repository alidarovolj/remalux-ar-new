using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(RawImage))]
public class ARDisplayUpdater : MonoBehaviour
{
    [SerializeField] private ARCameraManager arCameraManager;
    
    private RawImage rawImage;
    
    private void Awake()
    {
        rawImage = GetComponent<RawImage>();
        
        // If AR Camera Manager is not assigned, try to find it in the scene
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager == null)
            {
                Debug.LogError("ARDisplayUpdater: ARCameraManager not found in scene!");
            }
        }
    }
    
    private void OnEnable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnFrameReceived;
        }
    }
    
    private void OnDisable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnFrameReceived;
        }
    }
    
    private void OnFrameReceived(ARCameraFrameEventArgs args)
    {
        if (rawImage != null && arCameraManager.subsystem != null)
        {
            // Get the camera texture and assign it to the RawImage
            if (arCameraManager.TryAcquireLatestCpuImage(out var image))
            {
                // Handle CPU image if needed
                image.Dispose();
            }
            
            // Use the ARCameraBackground component to get the texture
            var bgRenderer = arCameraManager.GetComponent<ARCameraBackground>();
            Texture texture = null;
            
            if (bgRenderer != null && bgRenderer.material != null)
            {
                texture = bgRenderer.material.mainTexture;
                
                if (texture == null && bgRenderer.material.HasProperty("_MainTex"))
                {
                    texture = bgRenderer.material.GetTexture("_MainTex");
                }
            }
            
            // Fallback to the texture provided in the frame
            if (texture == null && args.textures.Count > 0)
            {
                texture = args.textures[0];
            }
            
            if (texture != null)
            {
                rawImage.texture = texture;
                Debug.Log("ARDisplayUpdater: Updated RawImage texture");
            }
        }
    }
} 