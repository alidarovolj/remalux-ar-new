using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Linq;
using ML.DeepLab;

/// <summary>
/// Creates a proper WallMesh material at runtime if there are issues with the asset file
/// </summary>
public class WallMeshMaterialFixer : MonoBehaviour
{
    public Material wallMeshMaterial;
    public Color wallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
    
    void Awake()
    {
        // Create material if not assigned
        if (wallMeshMaterial == null)
        {
            Debug.Log("WallMeshMaterialFixer: Creating new wall material");
            wallMeshMaterial = new Material(Shader.Find("Standard"));
            wallMeshMaterial.name = "WallMesh_Runtime";
            SetupTransparentMaterial();
        }
        
        // Find the WallMeshRenderer and set its material
        WallMeshRenderer wallMeshRenderer = FindObjectOfType<WallMeshRenderer>();
        if (wallMeshRenderer != null)
        {
            if (wallMeshRenderer.WallMaterial == null)
            {
                wallMeshRenderer.WallMaterial = wallMeshMaterial;
                Debug.Log("WallMeshMaterialFixer: Applied material to WallMeshRenderer");
            }
            
            // Find and set EnhancedDeepLabPredictor
            EnhancedDeepLabPredictor predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            if (predictor != null)
            {
                wallMeshRenderer.Predictor = predictor;
                Debug.Log("WallMeshMaterialFixer: Set EnhancedDeepLabPredictor reference");
            }
            else
            {
                // Try to find DeepLabPredictor variants
                var deepLabComponents = FindObjectsOfType<Component>().Where(c => c.GetType().Name.Contains("DeepLab"));
                foreach (var component in deepLabComponents)
                {
                    if (component is EnhancedDeepLabPredictor enhancedPredictor)
                    {
                        wallMeshRenderer.Predictor = enhancedPredictor;
                        Debug.Log($"WallMeshMaterialFixer: Found and set {component.GetType().Name}");
                        break;
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("WallMeshMaterialFixer: WallMeshRenderer not found");
            
            // Find ARMeshManager and add WallMeshRenderer
            ARMeshManager meshManager = FindObjectOfType<ARMeshManager>();
            if (meshManager != null)
            {
                GameObject meshObj = meshManager.gameObject;
                
                // Check if mesh object already has WallMeshRenderer
                wallMeshRenderer = meshObj.GetComponent<WallMeshRenderer>();
                if (wallMeshRenderer == null)
                {
                    wallMeshRenderer = meshObj.AddComponent<WallMeshRenderer>();
                    Debug.Log("WallMeshMaterialFixer: Added WallMeshRenderer to ARMeshManager");
                }
                
                // Set the material
                wallMeshRenderer.WallMaterial = wallMeshMaterial;
                
                // Find and set ARCameraManager
                ARCameraManager cameraManager = FindObjectOfType<ARCameraManager>();
                if (cameraManager != null)
                {
                    wallMeshRenderer.ARCameraManager = cameraManager;
                }
                
                // Find and set EnhancedDeepLabPredictor
                EnhancedDeepLabPredictor predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
                if (predictor != null)
                {
                    wallMeshRenderer.Predictor = predictor;
                    Debug.Log("WallMeshMaterialFixer: Set EnhancedDeepLabPredictor reference");
                }
                else
                {
                    // Try to find DeepLabPredictor variants
                    var deepLabComponents = FindObjectsOfType<Component>().Where(c => c.GetType().Name.Contains("DeepLab"));
                    foreach (var component in deepLabComponents)
                    {
                        if (component is EnhancedDeepLabPredictor enhancedPredictor)
                        {
                            wallMeshRenderer.Predictor = enhancedPredictor;
                            Debug.Log($"WallMeshMaterialFixer: Found and set {component.GetType().Name}");
                            break;
                        }
                    }
                }
            }
        }
    }
    
    private void SetupTransparentMaterial()
    {
        // Setup wall material
        wallMeshMaterial.color = wallColor;
        
        // Make it transparent
        wallMeshMaterial.SetFloat("_Mode", 3); // Transparent mode
        wallMeshMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wallMeshMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wallMeshMaterial.SetInt("_ZWrite", 0);
        wallMeshMaterial.DisableKeyword("_ALPHATEST_ON");
        wallMeshMaterial.EnableKeyword("_ALPHABLEND_ON");
        wallMeshMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        wallMeshMaterial.renderQueue = 3000;
    }
} 