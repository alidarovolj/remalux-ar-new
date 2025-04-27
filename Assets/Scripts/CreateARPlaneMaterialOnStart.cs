using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlaneManager))]
public class CreateARPlaneMaterialOnStart : MonoBehaviour
{
    [SerializeField]
    private Color planeColor = new Color(1f, 1f, 1f, 0.7f); // White, semi-transparent
    
    [SerializeField]
    private string shaderName = "Standard";
    
    [SerializeField]
    private bool useUnlitShader = true;
    
    private ARPlaneManager planeManager;
    private Material planeMaterial;
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        
        // Create material at runtime
        CreatePlaneMaterial();
        
        // Apply to plane prefab if possible
        ApplyMaterialToPlanePrefab();
    }
    
    private void CreatePlaneMaterial()
    {
        // Try to find appropriate shader
        Shader shader = null;
        
        if (useUnlitShader)
        {
            // Try multiple unlit shader variants for compatibility
            shader = Shader.Find("Unlit/Color");
            
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            
            if (shader == null)
                shader = Shader.Find("Legacy Shaders/Unlit/Color");
        }
        
        // Fall back to specified shader or Standard if unlit not found
        if (shader == null)
        {
            shader = Shader.Find(shaderName);
            
            if (shader == null)
                shader = Shader.Find("Standard");
        }
        
        // Create the material
        planeMaterial = new Material(shader);
        planeMaterial.color = planeColor;
        
        // For standard shader setup transparency
        if (shader.name.Contains("Standard"))
        {
            // Set to transparent mode
            planeMaterial.SetFloat("_Mode", 3); // Transparent
            planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            planeMaterial.SetInt("_ZWrite", 0);
            planeMaterial.DisableKeyword("_ALPHATEST_ON");
            planeMaterial.EnableKeyword("_ALPHABLEND_ON");
            planeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            planeMaterial.renderQueue = 3000;
        }
        
        Debug.Log($"Created AR Plane material with shader: {shader.name}");
    }
    
    private void ApplyMaterialToPlanePrefab()
    {
        if (planeManager == null || planeMaterial == null)
            return;
            
        // If plane prefab exists, update its material
        if (planeManager.planePrefab != null)
        {
            MeshRenderer meshRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = planeMaterial;
                Debug.Log("Applied material to plane prefab");
            }
        }
        else
        {
            Debug.LogWarning("No plane prefab assigned to ARPlaneManager");
            
            // Create a new plane prefab if none is assigned
            GameObject newPlanePrefab = new GameObject("RuntimeARPlanePrefab");
            newPlanePrefab.AddComponent<ARPlaneMeshVisualizer>();
            newPlanePrefab.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = newPlanePrefab.AddComponent<MeshRenderer>();
            meshRenderer.material = planeMaterial;
            
            // Don't assign it directly as it would require prefab creation
            // Just log the warning - user needs to assign a prefab in inspector
            Debug.LogWarning("Created runtime plane prefab but it needs to be manually assigned in inspector");
        }
    }
} 