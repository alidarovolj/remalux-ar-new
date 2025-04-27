using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;

[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneVisibilityEnhancer : MonoBehaviour
{
    [SerializeField]
    private Color horizontalPlaneColor = new Color(1f, 1f, 1f, 0.8f); // White with 80% opacity
    
    [SerializeField]
    private Color verticalPlaneColor = new Color(0.8f, 0.8f, 1f, 0.8f); // Light blue with 80% opacity
    
    [SerializeField]
    private bool applyMaterialImmediately = true;

    private ARPlaneManager planeManager;
    private Material horizontalPlaneMaterial;
    private Material verticalPlaneMaterial;

    void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        
        // Create materials for planes
        CreatePlaneMaterials();
    }

    void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
        
        // Apply to existing planes if there are any
        if (applyMaterialImmediately)
        {
            StartCoroutine(ApplyMaterialsAfterDelay());
        }
    }
    
    void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private IEnumerator ApplyMaterialsAfterDelay()
    {
        // Wait a frame to ensure AR setup is complete
        yield return null;
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            ApplyMaterialToPlane(plane);
        }
    }

    private void CreatePlaneMaterials()
    {
        // Create material for horizontal planes
        horizontalPlaneMaterial = new Material(Shader.Find("Standard"));
        horizontalPlaneMaterial.color = horizontalPlaneColor;
        
        // Opaque instead of transparent for better visibility
        horizontalPlaneMaterial.SetFloat("_Mode", 0); // Opaque
        horizontalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        horizontalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        horizontalPlaneMaterial.SetInt("_ZWrite", 1);
        horizontalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
        horizontalPlaneMaterial.DisableKeyword("_ALPHABLEND_ON");
        horizontalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        horizontalPlaneMaterial.renderQueue = -1;
        
        // Create material for vertical planes
        verticalPlaneMaterial = new Material(Shader.Find("Standard"));
        verticalPlaneMaterial.color = verticalPlaneColor;
        
        // Same settings for opacity
        verticalPlaneMaterial.SetFloat("_Mode", 0);
        verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        verticalPlaneMaterial.SetInt("_ZWrite", 1);
        verticalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
        verticalPlaneMaterial.DisableKeyword("_ALPHABLEND_ON");
        verticalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        verticalPlaneMaterial.renderQueue = -1;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (args.added != null)
        {
            foreach (ARPlane plane in args.added)
            {
                ApplyMaterialToPlane(plane);
            }
        }
        
        if (args.updated != null)
        {
            foreach (ARPlane plane in args.updated)
            {
                // Reapply material on updates to ensure it doesn't get overridden
                ApplyMaterialToPlane(plane);
            }
        }
    }

    private void ApplyMaterialToPlane(ARPlane plane)
    {
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            // Apply appropriate material based on plane orientation
            if (plane.alignment == PlaneAlignment.Vertical)
            {
                meshRenderer.material = verticalPlaneMaterial;
                Debug.Log($"Applied vertical plane material to plane: {plane.trackableId}");
            }
            else
            {
                meshRenderer.material = horizontalPlaneMaterial;
                Debug.Log($"Applied horizontal plane material to plane: {plane.trackableId}");
            }
            
            // Force enable the mesh renderer
            meshRenderer.enabled = true;
        }
    }
} 