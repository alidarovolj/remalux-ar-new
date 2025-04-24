using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class ARComponentFixer : MonoBehaviour
{
    [Header("References")]
    public ARSessionOrigin arSessionOrigin;
    public Material wallMaterial;

    [Header("AR Components")]
    public bool ensureARCameraManager = true;
    public bool ensureARPlaneManager = true;
    public bool ensureARMeshManager = true;
    public bool ensureARRaycastManager = true;
    public bool ensureARPointCloudManager = false; // Optional

    [Header("Wall Detection")]
    public bool configureWallDetection = true;
    public bool ensureWallMeshRenderer = true;
    public bool configureSegmentation = true;
    public int wallClassId = 9; // Updated to ADE20K wall class ID (9)
    public float classificationThreshold = 0.05f;
    public float verticalThreshold = 0.6f;
    public float wallConfidenceThreshold = 0.05f;

    [Header("Mesh Settings")]
    public float meshDensity = 0.5f;
    public float meshUpdateInterval = 0.2f;

    [Header("Debug")]
    public bool debugMode = true;
    public bool showAllMeshes = true;
    public bool showDebugVisualizer = true;
    public Color debugMeshColor = new Color(1.0f, 0.0f, 1.0f, 0.7f);
    public bool verbose = true;

    private ARCameraManager _cameraManager;
    private ARPlaneManager _planeManager;
    private ARMeshManager _meshManager;
    private ARRaycastManager _raycastManager;
    private ARPointCloudManager _pointCloudManager;
    private EnhancedDeepLabPredictor _predictor;
    private WallMeshRenderer _wallMeshRenderer;

    public void FixAllComponents()
    {
        if (arSessionOrigin == null)
        {
            arSessionOrigin = FindObjectOfType<ARSessionOrigin>();
            if (arSessionOrigin == null)
            {
                Debug.LogError("ARComponentFixer: ARSessionOrigin not found in scene.");
                return;
            }
        }

        Log("Starting AR Component configuration...");

        // Ensure all required AR components exist and are configured
        EnsureARComponents();

        // Configure wall detection if needed
        if (configureWallDetection)
        {
            ConfigureWallDetection();
        }

        // Ensure we have a WallMeshMaterialFixer
        EnsureWallMeshMaterialFixer();

        Log("AR Component configuration complete.");
    }

    private void EnsureARComponents()
    {
        GameObject arCamera = arSessionOrigin.camera.gameObject;

        // Ensure AR Camera Manager
        if (ensureARCameraManager)
        {
            _cameraManager = arCamera.GetComponent<ARCameraManager>();
            if (_cameraManager == null)
            {
                _cameraManager = arCamera.AddComponent<ARCameraManager>();
                Log("Added ARCameraManager to AR Camera");
            }
            else
            {
                Log("ARCameraManager already exists");
            }
        }

        // Ensure AR Plane Manager
        if (ensureARPlaneManager)
        {
            _planeManager = arSessionOrigin.gameObject.GetComponent<ARPlaneManager>();
            if (_planeManager == null)
            {
                _planeManager = arSessionOrigin.gameObject.AddComponent<ARPlaneManager>();
                Log("Added ARPlaneManager to AR Session Origin");
            }
            
            // Configure the plane manager for vertical surfaces
            _planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
            Log("Configured ARPlaneManager for vertical surfaces");
        }

        // Ensure AR Mesh Manager
        if (ensureARMeshManager)
        {
            _meshManager = arSessionOrigin.gameObject.GetComponent<ARMeshManager>();
            if (_meshManager == null)
            {
                _meshManager = arSessionOrigin.gameObject.AddComponent<ARMeshManager>();
                Log("Added ARMeshManager to AR Session Origin");
            }
            
            // Configure mesh manager
            _meshManager.density = meshDensity;
            _meshManager.normals = true;
            _meshManager.tangents = false;
            _meshManager.textureCoordinates = false;
            _meshManager.colors = false;
            _meshManager.concurrentQueueSize = 4;
            
            // By default, we'll show all meshes to assist with debugging
            if (_meshManager.meshPrefab != null)
            {
                var meshRenderer = _meshManager.meshPrefab.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = showAllMeshes;
                    
                    // Create a new material if not already assigned
                    if (meshRenderer.sharedMaterial == null)
                    {
                        meshRenderer.sharedMaterial = new Material(Shader.Find("Unlit/Color"));
                        meshRenderer.sharedMaterial.color = debugMeshColor;
                    }
                }
            }
            
            Log("Configured ARMeshManager with density: " + meshDensity);
        }

        // Ensure AR Raycast Manager
        if (ensureARRaycastManager)
        {
            _raycastManager = arSessionOrigin.gameObject.GetComponent<ARRaycastManager>();
            if (_raycastManager == null)
            {
                _raycastManager = arSessionOrigin.gameObject.AddComponent<ARRaycastManager>();
                Log("Added ARRaycastManager to AR Session Origin");
            }
            else
            {
                Log("ARRaycastManager already exists");
            }
        }

        // Ensure AR Point Cloud Manager (Optional)
        if (ensureARPointCloudManager)
        {
            _pointCloudManager = arSessionOrigin.gameObject.GetComponent<ARPointCloudManager>();
            if (_pointCloudManager == null)
            {
                _pointCloudManager = arSessionOrigin.gameObject.AddComponent<ARPointCloudManager>();
                Log("Added ARPointCloudManager to AR Session Origin");
            }
            else
            {
                Log("ARPointCloudManager already exists");
            }
        }
    }

    /// <summary>
    /// Configure wall detection components
    /// </summary>
    private void ConfigureWallDetection()
    {
        // Find or add EnhancedDeepLabPredictor
        EnhancedDeepLabPredictor predictor = GetComponent<EnhancedDeepLabPredictor>();
        if (predictor == null)
        {
            predictor = gameObject.AddComponent<EnhancedDeepLabPredictor>();
            Log("Added EnhancedDeepLabPredictor component");
        }

        // Configure enhanced predictor
        if (predictor != null)
        {
            // Cast wallClassId to byte explicitly
            predictor.WallClassId = (byte)wallClassId;
            
            // Using reflection to set properties/fields that might be private
            var classThresholdField = predictor.GetType().GetField("ClassificationThreshold", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (classThresholdField != null)
                classThresholdField.SetValue(predictor, classificationThreshold);
                
            var debugModeField = predictor.GetType().GetField("debugMode", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (debugModeField != null)
                debugModeField.SetValue(predictor, debugMode);
                
            var verboseField = predictor.GetType().GetField("verbose", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (verboseField != null)
                verboseField.SetValue(predictor, verbose);
                
            Log($"Configured EnhancedDeepLabPredictor: WallClassId={predictor.WallClassId}, Threshold={classificationThreshold:F2}");
        }

        // Find or add WallMeshRenderer
        WallMeshRenderer wallRenderer = GetComponent<WallMeshRenderer>();
        if (wallRenderer == null)
        {
            wallRenderer = gameObject.AddComponent<WallMeshRenderer>();
            Log("Added WallMeshRenderer component");
        }

        // Configure wall renderer
        if (wallRenderer != null)
        {
            wallRenderer.ShowDebugInfo = debugMode;
            wallRenderer.ShowAllMeshes = showAllMeshes;
            wallRenderer.VerticalThreshold = verticalThreshold;
            wallRenderer.WallConfidenceThreshold = wallConfidenceThreshold;
            Log($"Configured WallMeshRenderer: VerticalThreshold={wallRenderer.VerticalThreshold:F2}, ConfidenceThreshold={wallRenderer.WallConfidenceThreshold:F2}");
        }
    }

    private void EnsureWallMeshMaterialFixer()
    {
        // Check if WallMeshMaterialFixer exists
        WallMeshMaterialFixer materialFixer = FindObjectOfType<WallMeshMaterialFixer>();
        if (materialFixer == null)
        {
            GameObject materialFixerObj = new GameObject("Wall Material Fixer");
            materialFixer = materialFixerObj.AddComponent<WallMeshMaterialFixer>();
            
            // Set the material if available
            if (wallMaterial != null)
            {
                materialFixer.wallMeshMaterial = wallMaterial;
            }
            
            // Set the color
            materialFixer.wallColor = debugMeshColor;
            
            Log("Added WallMeshMaterialFixer to ensure proper wall material creation");
        }
        else
        {
            // Update existing material fixer
            if (wallMaterial != null)
            {
                materialFixer.wallMeshMaterial = wallMaterial;
            }
            materialFixer.wallColor = debugMeshColor;
            
            Log("Updated existing WallMeshMaterialFixer");
        }
    }

    private void Log(string message)
    {
        if (verbose)
        {
            Debug.Log($"[ARComponentFixer] {message}");
        }
    }

    private void Start()
    {
        // Find and fix XR Origin setup
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin != null)
        {
            // Ensure camera offset is set correctly
            if (xrOrigin.CameraFloorOffsetObject == null)
            {
                GameObject cameraOffset = new GameObject("Camera Offset");
                cameraOffset.transform.SetParent(xrOrigin.transform, false);
                xrOrigin.CameraFloorOffsetObject = cameraOffset;
            }

            // Ensure camera reference is set
            if (xrOrigin.Camera == null)
            {
                Camera arCamera = FindObjectOfType<Camera>();
                if (arCamera != null)
                {
                    xrOrigin.Camera = arCamera;
                }
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ARComponentFixer))]
    public class ARComponentFixerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ARComponentFixer fixer = (ARComponentFixer)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Fix All AR Components"))
            {
                fixer.FixAllComponents();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Component Status", EditorStyles.boldLabel);
            
            // Check component statuses
            ARSessionOrigin origin = fixer.arSessionOrigin != null ? fixer.arSessionOrigin : FindObjectOfType<ARSessionOrigin>();
            if (origin == null)
            {
                EditorGUILayout.HelpBox("No ARSessionOrigin found in scene", MessageType.Error);
                return;
            }

            GameObject arCamera = origin.camera.gameObject;
            EditorGUILayout.LabelField("AR Camera: ", arCamera != null ? "Found" : "Missing");
            
            ARCameraManager cameraManager = arCamera.GetComponent<ARCameraManager>();
            EditorGUILayout.LabelField("AR Camera Manager: ", cameraManager != null ? "Found" : "Missing");
            
            ARPlaneManager planeManager = origin.gameObject.GetComponent<ARPlaneManager>();
            EditorGUILayout.LabelField("AR Plane Manager: ", planeManager != null ? "Found" : "Missing");
            
            ARMeshManager meshManager = origin.gameObject.GetComponent<ARMeshManager>();
            EditorGUILayout.LabelField("AR Mesh Manager: ", meshManager != null ? "Found" : "Missing");
            
            ARRaycastManager raycastManager = origin.gameObject.GetComponent<ARRaycastManager>();
            EditorGUILayout.LabelField("AR Raycast Manager: ", raycastManager != null ? "Found" : "Missing");
            
            EnhancedDeepLabPredictor predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            EditorGUILayout.LabelField("Enhanced DeepLab Predictor: ", predictor != null ? "Found" : "Missing");
            
            WallMeshRenderer wallRenderer = origin.gameObject.GetComponent<WallMeshRenderer>();
            EditorGUILayout.LabelField("Wall Mesh Renderer: ", wallRenderer != null ? "Found" : "Missing");

            // Add buttons to toggle visualization settings
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Visualization Tools", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Toggle Show All Meshes"))
            {
                fixer.showAllMeshes = !fixer.showAllMeshes;
                
                // Update WallMeshRenderer if it exists
                WallMeshRenderer wallMeshRenderer = FindObjectOfType<WallMeshRenderer>();
                if (wallMeshRenderer != null)
                {
                    wallMeshRenderer.ShowAllMeshes = fixer.showAllMeshes;
                    wallMeshRenderer.ToggleShowAllMeshes();
                    EditorUtility.SetDirty(fixer);
                }
            }
            
            if (GUILayout.Button("Toggle Debug Visualizer"))
            {
                fixer.showDebugVisualizer = !fixer.showDebugVisualizer;
                
                // Update WallMeshRenderer if it exists
                WallMeshRenderer wallMeshRenderer = FindObjectOfType<WallMeshRenderer>();
                if (wallMeshRenderer != null)
                {
                    wallMeshRenderer.ToggleDebugVisualizer();
                    EditorUtility.SetDirty(fixer);
                }
            }
        }
    }
#endif
} 