using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlaneManager))]
public class CreateARPlaneMaterialOnStart : MonoBehaviour
{
    [SerializeField]
    private Color verticalPlaneColor = new Color(0.9f, 0.3f, 0.8f, 0.8f);
    
    [SerializeField]
    private Color horizontalPlaneColor = new Color(0.2f, 0.6f, 0.9f, 0.8f);
    
    [SerializeField]
    private bool enhanceExistingPlanes = true;
    
    private ARPlaneManager planeManager;
    private Material planeMaterial;
    
    private void Awake()
    {
        // Get the ARPlaneManager
        planeManager = GetComponent<ARPlaneManager>();
        
        // Create plane material
        CreatePlaneMaterial();
    }
    
    private void CreatePlaneMaterial()
    {
        // Create new material using Unlit/Transparent shader
        planeMaterial = new Material(Shader.Find("Unlit/Transparent"));
        
        // Set base color
        planeMaterial.color = horizontalPlaneColor;
        
        // Save the material as a field
        if (planeMaterial != null)
        {
            Debug.Log("AR Plane material created successfully");
        }
        else
        {
            Debug.LogError("Failed to create AR Plane material");
        }
    }
    
    private void OnEnable()
    {
        // Subscribe to planes changed event
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
            
            // Process existing planes
            if (enhanceExistingPlanes)
            {
                EnhanceExistingPlanes();
            }
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe from planes changed event
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Process added planes
        foreach (ARPlane plane in args.added)
        {
            EnhancePlaneVisibility(plane);
        }
        
        // Process updated planes
        foreach (ARPlane plane in args.updated)
        {
            EnhancePlaneVisibility(plane);
        }
    }
    
    private void EnhancePlaneVisibility(ARPlane plane)
    {
        if (plane == null || planeMaterial == null) return;
        
        // Get mesh renderer component
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            // Create an instance of the material to avoid affecting other planes
            Material planeMaterialInstance = new Material(planeMaterial);
            
            // Set color based on plane alignment
            if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
            {
                planeMaterialInstance.color = verticalPlaneColor;
                Debug.Log("Enhanced vertical plane visibility");
            }
            else
            {
                planeMaterialInstance.color = horizontalPlaneColor;
                Debug.Log("Enhanced horizontal plane visibility");
            }
            
            // Assign material
            meshRenderer.material = planeMaterialInstance;
        }
    }
    
    private void EnhanceExistingPlanes()
    {
        if (planeManager == null) return;
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            EnhancePlaneVisibility(plane);
        }
    }
    
    [ContextMenu("Enhance All Planes Now")]
    public void EnhanceAllPlanesNow()
    {
        EnhanceExistingPlanes();
    }
} 