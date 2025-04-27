using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ML.DeepLab;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Класс для соединения привязок стен и обработки данных сегментации
/// </summary>
public class WallAnchorConnector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DeepLabPredictor _deepLabPredictor;
    [SerializeField] private MonoBehaviour _wallSetup; // Can be either RemaluxARWallSetup or RemaluxWallDetectionSystem
    [SerializeField] private ARWallAnchor _wallAnchor;
    [SerializeField] private ARCameraManager _cameraManager;
    [SerializeField] private EnhancedDeepLabPredictor _predictor;
    [SerializeField] private Camera _arCamera;
    [SerializeField] private ARPlaneManager _arPlaneManager;
    [SerializeField] private ARAnchorManager _anchorManager;
    
    [Header("Settings")]
    [SerializeField] private float _connectionThreshold = 0.5f;
    [SerializeField] private float _wallAnalysisInterval = 1.0f;
    [SerializeField] private bool _debugMode = true;
    
    [Header("Visualization")]
    [SerializeField] private bool _showConnections = true;
    [SerializeField] private Material _connectionMaterial;
    [SerializeField] private float _connectionWidth = 0.02f;
    
    // Properties to allow external access and setting
    public ARCameraManager CameraManager {
        get { return _cameraManager; }
        set { _cameraManager = value; }
    }
    
    public EnhancedDeepLabPredictor Predictor {
        get { return _predictor; }
        set { _predictor = value; }
    }
    
    public ARWallAnchor WallAnchor {
        get { return _wallAnchor; }
        set { _wallAnchor = value; }
    }
    
    public Camera ARCamera {
        get { return _arCamera; }
        set { _arCamera = value; }
    }
    
    public ARPlaneManager ARPlaneManager {
        get { return _arPlaneManager; }
        set { _arPlaneManager = value; }
    }
    
    public ARAnchorManager AnchorManager {
        get { return _anchorManager; }
        set { _anchorManager = value; }
    }
    
    private float _lastAnalysisTime = 0f;
    private List<LineRenderer> _connectionRenderers = new List<LineRenderer>();
    
    // Properties to access wall setup methods regardless of type
    private List<ARWallAnchor> WallAnchors 
    {
        get 
        {
            if (_wallSetup is RemaluxWallDetectionSystem wallSystem)
                return wallSystem.GetWallAnchors();
            else if (_wallSetup is RemaluxARWallSetup arSetup)
                return arSetup.GetWallAnchors();
            return new List<ARWallAnchor>();
        }
    }
    
    private void Awake()
    {
        // Find required components if not assigned
        if (_deepLabPredictor == null)
            _deepLabPredictor = FindObjectOfType<DeepLabPredictor>();
            
        if (_wallSetup == null)
        {
            _wallSetup = FindObjectOfType<RemaluxWallDetectionSystem>();
            if (_wallSetup == null)
            {
                _wallSetup = FindObjectOfType<RemaluxARWallSetup>();
            }
        }
            
        if (_connectionMaterial == null)
        {
            _connectionMaterial = Resources.Load<Material>("Materials/ConnectionMaterial");
            if (_connectionMaterial == null)
            {
                // Create a default material
                _connectionMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _connectionMaterial.color = new Color(0.0f, 0.7f, 1.0f, 0.5f);
            }
        }
        
        if (_cameraManager == null)
            _cameraManager = FindObjectOfType<ARCameraManager>();
            
        if (_predictor == null)
            _predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
        if (_arCamera == null && _cameraManager != null)
            _arCamera = _cameraManager.GetComponent<Camera>();
            
        if (_arPlaneManager == null)
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            
        if (_anchorManager == null)
            _anchorManager = FindObjectOfType<ARAnchorManager>();
            
        if (_wallAnchor == null)
            _wallAnchor = FindObjectOfType<ARWallAnchor>();
    }
    
    private void Start()
    {
        // Validate required components
        if (_deepLabPredictor == null)
        {
            Debug.LogWarning("WallAnchorConnector: DeepLabPredictor not found, functionality will be limited");
        }
        
        if (_wallSetup == null)
        {
            Debug.LogWarning("WallAnchorConnector: Wall detection system not found, functionality will be limited");
        }
    }
    
    /// <summary>
    /// Обрабатывает данные сегментации для привязок стен
    /// </summary>
    public void ProcessSegmentationForAnchors()
    {
        // Only process at certain intervals
        if (Time.time - _lastAnalysisTime < _wallAnalysisInterval)
            return;
            
        _lastAnalysisTime = Time.time;
        
        if (_wallSetup == null)
            return;
            
        // Get wall anchors
        List<ARWallAnchor> wallAnchors = WallAnchors;
        
        if (wallAnchors.Count < 2)
            return;
            
        // Analyze relations between walls
        AnalyzeWallRelations(wallAnchors);
        
        // Visualize connections if needed
        if (_showConnections)
            VisualizeWallConnections(wallAnchors);
            
        if (_debugMode)
            Debug.Log($"WallAnchorConnector: Processed {wallAnchors.Count} wall anchors");
    }
    
    /// <summary>
    /// Анализирует отношения между стенами
    /// </summary>
    private void AnalyzeWallRelations(List<ARWallAnchor> wallAnchors)
    {
        // Clear existing connections
        ClearConnectionRenderers();
        
        // Check all pairs of walls
        for (int i = 0; i < wallAnchors.Count; i++)
        {
            for (int j = i + 1; j < wallAnchors.Count; j++)
            {
                ARWallAnchor wall1 = wallAnchors[i];
                ARWallAnchor wall2 = wallAnchors[j];
                
                if (wall1 == null || wall2 == null)
                    continue;
                    
                // Check if walls are connected
                if (AreWallsConnected(wall1, wall2))
                {
                    // Create connection between walls
                    if (_showConnections)
                        CreateWallConnection(wall1, wall2);
                }
            }
        }
    }
    
    /// <summary>
    /// Определяет, соединены ли две стены
    /// </summary>
    private bool AreWallsConnected(ARWallAnchor wall1, ARWallAnchor wall2)
    {
        // Get walls' transforms
        Transform t1 = wall1.transform;
        Transform t2 = wall2.transform;
        
        // Calculate distance between wall centers
        float distance = Vector3.Distance(t1.position, t2.position);
        
        // Check if walls are close enough
        if (distance > 2.0f)
            return false;
            
        // Check if walls are perpendicular (approximately 90 degrees)
        float dotProduct = Vector3.Dot(t1.forward, t2.forward);
        
        // If dot product is close to 0, walls are perpendicular
        if (Mathf.Abs(dotProduct) < _connectionThreshold)
        {
            // Check if one wall's end is close to the other wall
            float t1Width = wall1.WallWidth;
            float t2Width = wall2.WallWidth;
            
            Vector3 t1Right = t1.position + (t1.right * (t1Width / 2));
            Vector3 t1Left = t1.position - (t1.right * (t1Width / 2));
            
            Vector3 t2Right = t2.position + (t2.right * (t2Width / 2));
            Vector3 t2Left = t2.position - (t2.right * (t2Width / 2));
            
            // Check all four possible connections
            float d1 = Vector3.Distance(t1Right, t2Left);
            float d2 = Vector3.Distance(t1Right, t2Right);
            float d3 = Vector3.Distance(t1Left, t2Left);
            float d4 = Vector3.Distance(t1Left, t2Right);
            
            float minDistance = Mathf.Min(d1, d2, d3, d4);
            
            // If minimum distance is less than threshold, walls are connected
            return minDistance < _connectionThreshold;
        }
        
        return false;
    }
    
    /// <summary>
    /// Создает визуальное соединение между стенами
    /// </summary>
    private void CreateWallConnection(ARWallAnchor wall1, ARWallAnchor wall2)
    {
        // Create line renderer for connection
        GameObject connectionObject = new GameObject($"Connection_{wall1.gameObject.name}_{wall2.gameObject.name}");
        connectionObject.transform.SetParent(transform);
        
        LineRenderer lineRenderer = connectionObject.AddComponent<LineRenderer>();
        lineRenderer.material = _connectionMaterial;
        lineRenderer.startWidth = _connectionWidth;
        lineRenderer.endWidth = _connectionWidth;
        lineRenderer.positionCount = 2;
        
        // Set line positions
        lineRenderer.SetPosition(0, wall1.transform.position);
        lineRenderer.SetPosition(1, wall2.transform.position);
        
        // Add to list for cleanup
        _connectionRenderers.Add(lineRenderer);
    }
    
    /// <summary>
    /// Визуализирует соединения между стенами
    /// </summary>
    private void VisualizeWallConnections(List<ARWallAnchor> wallAnchors)
    {
        // Update existing connections
        foreach (LineRenderer lineRenderer in _connectionRenderers)
        {
            if (lineRenderer == null)
                continue;
                
            // Get wall names from connection name
            string connectionName = lineRenderer.gameObject.name;
            string[] parts = connectionName.Split('_');
            
            if (parts.Length < 3)
                continue;
                
            // Find walls by name
            ARWallAnchor wall1 = null;
            ARWallAnchor wall2 = null;
            
            foreach (ARWallAnchor wall in wallAnchors)
            {
                if (wall == null)
                    continue;
                    
                if (wall.gameObject.name == parts[1])
                    wall1 = wall;
                    
                if (wall.gameObject.name == parts[2])
                    wall2 = wall;
            }
            
            // Update line positions
            if (wall1 != null && wall2 != null)
            {
                lineRenderer.SetPosition(0, wall1.transform.position);
                lineRenderer.SetPosition(1, wall2.transform.position);
            }
        }
    }
    
    /// <summary>
    /// Очищает все визуализации соединений
    /// </summary>
    private void ClearConnectionRenderers()
    {
        // Remove all connection renderers
        foreach (LineRenderer lineRenderer in _connectionRenderers)
        {
            if (lineRenderer != null)
            {
                Destroy(lineRenderer.gameObject);
            }
        }
        
        _connectionRenderers.Clear();
    }
    
    /// <summary>
    /// Применяет сегментацию к существующим стенам
    /// </summary>
    public void ApplySegmentationToWalls()
    {
        if (_wallSetup == null)
            return;
            
        // Get wall anchors
        List<ARWallAnchor> wallAnchors = WallAnchors;
        
        if (wallAnchors.Count == 0)
            return;

        // First try to get segmentation texture from EnhancedDeepLabPredictor
        Texture2D segmentationTexture = null;
        
        if (_predictor != null)
        {
            // Use the EnhancedDeepLabPredictor's method
            segmentationTexture = TryGetSegmentationTexture(_predictor);
        }
        else if (_deepLabPredictor != null)
        {
            // Try to use reflection to get the segmentation texture
            segmentationTexture = TryGetSegmentationTextureViaReflection(_deepLabPredictor);
        }
        
        if (segmentationTexture == null)
        {
            if (_debugMode)
                Debug.LogWarning("WallAnchorConnector: Could not get segmentation texture");
            return;
        }
            
        // Apply segmentation to each wall
        foreach (ARWallAnchor wallAnchor in wallAnchors)
        {
            if (wallAnchor != null)
            {
                // Project wall to screen space
                Camera mainCamera = _arCamera != null ? _arCamera : Camera.main;
                if (mainCamera == null)
                    continue;
                    
                Vector3 screenPoint = mainCamera.WorldToScreenPoint(wallAnchor.transform.position);
                
                // If wall is visible on screen
                if (screenPoint.z > 0 && 
                    screenPoint.x >= 0 && screenPoint.x < Screen.width && 
                    screenPoint.y >= 0 && screenPoint.y < Screen.height)
                {
                    // Get normalized screen coordinates
                    float normalizedX = screenPoint.x / Screen.width;
                    float normalizedY = screenPoint.y / Screen.height;
                    
                    // Sample segmentation texture
                    int texX = Mathf.FloorToInt(normalizedX * segmentationTexture.width);
                    int texY = Mathf.FloorToInt(normalizedY * segmentationTexture.height);
                    
                    // Get segmentation color
                    Color color = segmentationTexture.GetPixel(texX, texY);
                    
                    // If pixel corresponds to wall (assuming wall is labeled with a specific color)
                    if (IsWallPixel(color))
                    {
                        // Highlight wall or perform any other action
                        // For now, just log it
                        if (_debugMode)
                            Debug.Log($"WallAnchorConnector: Wall at {wallAnchor.name} verified by segmentation");
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Tries to get segmentation texture from EnhancedDeepLabPredictor
    /// </summary>
    private Texture2D TryGetSegmentationTexture(EnhancedDeepLabPredictor predictor)
    {
        if (predictor == null)
            return null;
            
        // Try to use the method directly if it exists
        System.Type predictorType = predictor.GetType();
        var method = predictorType.GetMethod("GetSegmentationTexture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        if (method != null)
        {
            return method.Invoke(predictor, null) as Texture2D;
        }
        
        return null;
    }
    
    /// <summary>
    /// Tries to get segmentation texture from DeepLabPredictor using reflection
    /// </summary>
    private Texture2D TryGetSegmentationTextureViaReflection(DeepLabPredictor predictor)
    {
        if (predictor == null)
            return null;
            
        // Try to use reflection to get segmentation texture
        System.Type predictorType = predictor.GetType();
        
        // Try to get via field
        var field = predictorType.GetField("_segmentationTexture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue(predictor) as Texture2D;
        }
        
        // Try to get via property
        var prop = predictorType.GetProperty("SegmentationTexture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop != null)
        {
            return prop.GetValue(predictor) as Texture2D;
        }
        
        // Try to find a relevant method
        var methods = predictorType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var method in methods)
        {
            if (method.Name.Contains("GetSegmentation") && method.ReturnType == typeof(Texture2D))
            {
                try
                {
                    return method.Invoke(predictor, null) as Texture2D;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"WallAnchorConnector: Error invoking method: {e.Message}");
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Определяет, является ли пиксель частью стены (по цвету сегментации)
    /// </summary>
    private bool IsWallPixel(Color color)
    {
        // Check if color corresponds to wall class in segmentation
        // This depends on how DeepLabPredictor classifies walls
        
        // Example: if walls are labeled with red-ish color
        return color.r > 0.7f && color.g < 0.3f && color.b < 0.3f;
    }
    
    private void OnDestroy()
    {
        // Clean up connections
        ClearConnectionRenderers();
    }
} 