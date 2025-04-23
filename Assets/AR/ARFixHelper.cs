using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Reflection;
using ML.DeepLab;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Helper component to automatically fix AR components and ensure they're correctly set up for wall detection
/// </summary>
public class ARFixHelper : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Fix AR components automatically on start")]
    public bool fixOnStart = true;
    
    [Tooltip("Configure AR for wall detection")]
    public bool configureForWalls = true;
    
    private ARPlaneManager _planeManager;
    private ARMeshManager _meshManager;
    private GameObject _deepLabPredictor;
    private Component _wallRenderer;
    
    private void Start()
    {
        if (fixOnStart)
        {
            FixARComponents();
        }
    }
    
    /// <summary>
    /// Fix AR components to ensure proper functionality
    /// </summary>
    public void FixARComponents()
    {
        Debug.Log("[ARFixHelper] Starting AR component fixes...");
        
        // Find ARPlaneManager or add it if missing
        EnsureARPlaneManager();
        
        // Find ARMeshManager or add it if missing
        EnsureARMeshManager();
        
        // Find DeepLabPredictor
        FindDeepLabPredictor();
        
        // Connect AR components
        ConnectComponents();
        
        // Configure AR components for walls if needed
        if (configureForWalls)
        {
            ConfigureForWallDetection();
        }
        
        Debug.Log("[ARFixHelper] AR component fixes completed.");
    }
    
    private void EnsureARPlaneManager()
    {
        _planeManager = FindObjectOfType<ARPlaneManager>();
        
        if (_planeManager == null)
        {
            Debug.Log("[ARFixHelper] ARPlaneManager not found, adding one...");
            GameObject arFeatures = GameObject.Find("AR Features");
            
            if (arFeatures == null)
            {
                arFeatures = new GameObject("AR Features");
                Debug.Log("[ARFixHelper] Created AR Features GameObject");
            }
            
            _planeManager = arFeatures.AddComponent<ARPlaneManager>();
            
            // Connect to AR Session Origin if available
            ARSessionOrigin sessionOrigin = FindObjectOfType<ARSessionOrigin>();
            if (sessionOrigin != null)
            {
                Debug.Log("[ARFixHelper] Connected ARPlaneManager to ARSessionOrigin");
            }
            else
            {
                Debug.LogWarning("[ARFixHelper] ARSessionOrigin not found, ARPlaneManager might not function correctly");
            }
            
            // Setup plane detection mode
            _planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            Debug.Log("[ARFixHelper] Set plane detection mode to Vertical for wall detection");
        }
        else
        {
            Debug.Log("[ARFixHelper] ARPlaneManager found, checking configuration...");
            
            // Update plane detection mode for walls if needed
            if (configureForWalls && _planeManager.requestedDetectionMode != UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical)
            {
                _planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
                Debug.Log("[ARFixHelper] Updated plane detection mode to Vertical for wall detection");
            }
        }
    }
    
    private void EnsureARMeshManager()
    {
        _meshManager = FindObjectOfType<ARMeshManager>();
        
        if (_meshManager == null)
        {
            Debug.Log("[ARFixHelper] ARMeshManager not found, looking for alternatives...");
            
            // Look for components with similar names
            Component[] allComponents = FindObjectsOfType<Component>();
            foreach (Component comp in allComponents)
            {
                if (comp.GetType().Name.Contains("Mesh") && comp.GetType().Name.Contains("AR"))
                {
                    Debug.Log($"[ARFixHelper] Found potential AR mesh component: {comp.GetType().Name}");
                    _meshManager = comp as ARMeshManager;
                    if (_meshManager != null) break;
                }
            }
            
            if (_meshManager == null)
            {
                Debug.LogWarning("[ARFixHelper] Could not find ARMeshManager, walls may not be rendered correctly");
            }
        }
        else
        {
            Debug.Log("[ARFixHelper] ARMeshManager found");
        }
    }
    
    private void FindDeepLabPredictor()
    {
        // Try to find DeepLabPredictor or EnhancedDeepLabPredictor
        _deepLabPredictor = GameObject.Find("DeepLabPredictor");
        
        if (_deepLabPredictor == null)
        {
            // Look for objects with DeepLab in their name
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.Contains("DeepLab"))
                {
                    _deepLabPredictor = obj;
                    Debug.Log($"[ARFixHelper] Found potential DeepLabPredictor: {obj.name}");
                    break;
                }
            }
            
            if (_deepLabPredictor == null)
            {
                // Look for components with DeepLab in their type name
                Component[] allComponents = FindObjectsOfType<Component>();
                foreach (Component comp in allComponents)
                {
                    if (comp.GetType().Name.Contains("DeepLab"))
                    {
                        _deepLabPredictor = comp.gameObject;
                        Debug.Log($"[ARFixHelper] Found potential DeepLabPredictor component: {comp.GetType().Name}");
                        break;
                    }
                }
            }
            
            if (_deepLabPredictor == null)
            {
                Debug.LogWarning("[ARFixHelper] Could not find DeepLabPredictor, wall segmentation may not work");
            }
        }
        else
        {
            Debug.Log("[ARFixHelper] DeepLabPredictor found");
        }
        
        // Find wall renderer
        _wallRenderer = FindObjectOfType<MonoBehaviour>(true, comp => comp.GetType().Name.Contains("WallMesh"));
        
        if (_wallRenderer == null)
        {
            Debug.LogWarning("[ARFixHelper] Could not find WallMeshRenderer component");
        }
        else
        {
            Debug.Log($"[ARFixHelper] Found WallMeshRenderer: {_wallRenderer.GetType().Name}");
        }
    }
    
    private MonoBehaviour FindObjectOfType<T>(bool includeInactive, Func<MonoBehaviour, bool> predicate) where T : MonoBehaviour
    {
        MonoBehaviour[] components = FindObjectsOfType<MonoBehaviour>(includeInactive);
        foreach (MonoBehaviour comp in components)
        {
            if (predicate(comp))
            {
                return comp;
            }
        }
        return null;
    }
    
    private void ConnectComponents()
    {
        if (_deepLabPredictor == null || _wallRenderer == null) 
        {
            return;
        }
        
        // Check if _wallRenderer is a WallMeshRenderer
        WallMeshRenderer wallMeshRenderer = _wallRenderer as WallMeshRenderer;
        if (wallMeshRenderer != null)
        {
            Debug.Log("[ARFixHelper] Found WallMeshRenderer, attempting to connect using public property");
            
            // Find the DeepLabPredictor component
            EnhancedDeepLabPredictor enhancedPredictor = null;
            
            // Find components on the deepLabPredictor GameObject
            Component[] components = _deepLabPredictor.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp is EnhancedDeepLabPredictor predictor)
                {
                    enhancedPredictor = predictor;
                    break;
                }
            }
            
            if (enhancedPredictor != null)
            {
                try 
                {
                    // Use the public property
                    wallMeshRenderer.Predictor = enhancedPredictor;
                    Debug.Log("[ARFixHelper] Successfully connected EnhancedDeepLabPredictor to WallMeshRenderer");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ARFixHelper] Error setting Predictor property: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[ARFixHelper] Could not find EnhancedDeepLabPredictor component");
            }
        }
        else
        {
            Debug.LogWarning("[ARFixHelper] _wallRenderer is not a WallMeshRenderer, using reflection");
            
            // Try using reflection for custom wall renderer implementations
            try
            {
                Type wallRendererType = _wallRenderer.GetType();
                
                // Try different field names that might exist
                FieldInfo predictorField = wallRendererType.GetField("_predictor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (predictorField == null)
                    predictorField = wallRendererType.GetField("_wallPredictor", BindingFlags.Instance | BindingFlags.NonPublic);
                if (predictorField == null)
                    predictorField = wallRendererType.GetField("predictor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (predictorField != null)
                {
                    // Find the appropriate DeepLab component
                    Component deepLabComponent = null;
                    Component[] components = _deepLabPredictor.GetComponents<Component>();
                    foreach (Component comp in components)
                    {
                        if (comp.GetType().Name.Contains("DeepLab"))
                        {
                            deepLabComponent = comp;
                            break;
                        }
                    }
                    
                    if (deepLabComponent != null && predictorField.FieldType.IsAssignableFrom(deepLabComponent.GetType()))
                    {
                        predictorField.SetValue(_wallRenderer, deepLabComponent);
                        Debug.Log("[ARFixHelper] Connected DeepLabPredictor to custom wall renderer using reflection");
                    }
                    else
                    {
                        Debug.LogWarning("[ARFixHelper] Could not find compatible DeepLabPredictor component");
                    }
                }
                else
                {
                    Debug.LogWarning("[ARFixHelper] Could not find predictor field in wall renderer");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ARFixHelper] Error connecting components via reflection: {e.Message}");
            }
        }
        
        // Sync wall class ID if needed
        SyncWallClassId();
    }
    
    private void SyncWallClassId()
    {
        if (_deepLabPredictor == null || _wallRenderer == null) return;
        
        Component deepLabComponent = null;
        
        // Find the DeepLabPredictor component
        Component[] components = _deepLabPredictor.GetComponents<Component>();
        foreach (Component comp in components)
        {
            if (comp.GetType().Name.Contains("DeepLab"))
            {
                deepLabComponent = comp;
                break;
            }
        }
        
        if (deepLabComponent == null) return;
        
        // Get wall class ID from DeepLabPredictor
        PropertyInfo wallClassIdProperty = deepLabComponent.GetType().GetProperty("WallClassId") ?? 
                                          deepLabComponent.GetType().GetProperty("wallClassId");
        
        if (wallClassIdProperty != null)
        {
            int wallClassId = (int)wallClassIdProperty.GetValue(deepLabComponent);
            
            // Set wall class ID in WallMeshRenderer
            Type wallRendererType = _wallRenderer.GetType();
            FieldInfo wallClassIdField = wallRendererType.GetField("_wallClassId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                       wallRendererType.GetField("wallClassId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (wallClassIdField != null)
            {
                wallClassIdField.SetValue(_wallRenderer, wallClassId);
                Debug.Log($"[ARFixHelper] Synchronized wall class ID to {wallClassId}");
            }
        }
    }
    
    private void ConfigureForWallDetection()
    {
        if (_planeManager != null)
        {
            // Configure plane detection mode for walls
            _planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            
            // Adjust tracking mode for better wall detection
            Type planeManagerType = _planeManager.GetType();
            PropertyInfo trackingModeProperty = planeManagerType.GetProperty("trackingMode");
            
            if (trackingModeProperty != null)
            {
                // Try to set tracking mode to best option for wall detection
                try
                {
                    trackingModeProperty.SetValue(_planeManager, 2); // Typically 2 is best for walls
                    Debug.Log("[ARFixHelper] Set plane tracking mode for better wall detection");
                }
                catch (Exception)
                {
                    Debug.LogWarning("[ARFixHelper] Could not set plane tracking mode");
                }
            }
            
            Debug.Log("[ARFixHelper] Configured ARPlaneManager for wall detection");
        }
        
        if (_meshManager != null)
        {
            // Configure mesh update interval for better performance
            Type meshManagerType = _meshManager.GetType();
            FieldInfo updateIntervalField = meshManagerType.GetField("meshUpdateInterval", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (updateIntervalField != null && updateIntervalField.FieldType == typeof(float))
            {
                try
                {
                    updateIntervalField.SetValue(_meshManager, 0.5f); // 0.5 seconds is a good balance
                    Debug.Log("[ARFixHelper] Set mesh update interval to 0.5 seconds");
                }
                catch (Exception)
                {
                    Debug.LogWarning("[ARFixHelper] Could not set mesh update interval");
                }
            }
            
            Debug.Log("[ARFixHelper] Configured ARMeshManager for wall detection");
        }
        
        // Configure DeepLabPredictor for wall detection
        if (_deepLabPredictor != null)
        {
            Component deepLabComponent = null;
            
            // Find the DeepLabPredictor component
            Component[] components = _deepLabPredictor.GetComponents<Component>();
            foreach (Component comp in components)
            {
                if (comp.GetType().Name.Contains("DeepLab"))
                {
                    deepLabComponent = comp;
                    break;
                }
            }
            
            if (deepLabComponent != null)
            {
                Type deepLabType = deepLabComponent.GetType();
                
                // Set classification threshold
                PropertyInfo thresholdProperty = deepLabType.GetProperty("ClassificationThreshold") ?? 
                                               deepLabType.GetProperty("classificationThreshold");
                
                if (thresholdProperty != null && thresholdProperty.PropertyType == typeof(float))
                {
                    try
                    {
                        float currentThreshold = (float)thresholdProperty.GetValue(deepLabComponent);
                        
                        // Only lower the threshold if it's higher than a good value for walls
                        if (currentThreshold > 0.3f)
                        {
                            thresholdProperty.SetValue(deepLabComponent, 0.3f);
                            Debug.Log("[ARFixHelper] Set DeepLabPredictor classification threshold to 0.3");
                        }
                    }
                    catch (Exception)
                    {
                        Debug.LogWarning("[ARFixHelper] Could not set classification threshold");
                    }
                }
                
                Debug.Log("[ARFixHelper] Configured DeepLabPredictor for wall detection");
            }
        }
    }
} 