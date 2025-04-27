using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneVisibilityEnhancer : MonoBehaviour
{
    [SerializeField]
    private Material planeMaterial;
    
    [SerializeField]
    private Color horizontalPlaneColor = new Color(0.2f, 0.6f, 0.9f, 0.7f);
    
    [SerializeField]
    private Color verticalPlaneColor = new Color(0.9f, 0.3f, 0.8f, 0.7f);
    
    [SerializeField]
    private bool enhanceExistingPlanes = true;
    
    private ARPlaneManager planeManager;
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        
        // Try to load the material if not assigned
        if (planeMaterial == null)
        {
            planeMaterial = Resources.Load<Material>("ARPlaneMaterial");
        }
        
        if (planeMaterial == null)
        {
            planeMaterial = Resources.Load<Material>("Materials/ARPlaneMaterial");
        }
        
        if (planeMaterial == null)
        {
            Debug.LogWarning("Plane material not assigned or found. AR planes might not be visible enough.");
        }
    }
    
    private void OnEnable()
    {
        planeManager.planesChanged += OnPlanesChanged;
        
        // Process existing planes if any
        if (enhanceExistingPlanes)
        {
            EnhanceExistingPlanes();
        }
    }
    
    private void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
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
        // Skip if no valid material or plane is not valid
        if (planeMaterial == null || plane == null) return;
        
        // Get the mesh renderer component on the plane
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            // Assign the enhanced material
            meshRenderer.material = new Material(planeMaterial);
            
            // Set color based on plane alignment
            if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
            {
                meshRenderer.material.color = verticalPlaneColor;
                Debug.Log("Enhanced vertical plane visibility: " + plane.trackableId);
            }
            else
            {
                meshRenderer.material.color = horizontalPlaneColor;
                Debug.Log("Enhanced horizontal plane visibility: " + plane.trackableId);
            }
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