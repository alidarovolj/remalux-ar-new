using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using ML.DeepLab; // Add DeepLab namespace
using System.Collections;

/// <summary>
/// Представляет собой привязку стены в AR-пространстве с данными о её размерах и свойствах
/// </summary>
public class ARWallAnchor : MonoBehaviour
{
    [Header("Wall Properties")]
    [SerializeField] private float _wallWidth = 1.0f;
    [SerializeField] private float _wallHeight = 2.4f;
    [SerializeField] private bool _isValid = false;
    [SerializeField] private GameObject _wall;
    [SerializeField] private bool _debugMode = false;
    
    [Header("AR References")]
    [SerializeField] private ARAnchor _arAnchor;
    [SerializeField] private ARPlane _arPlane;
    
    [Header("Visualization")]
    [SerializeField] private MeshRenderer _wallMeshRenderer;
    [SerializeField] private MeshFilter _wallMeshFilter;
    [SerializeField] private Material _wallMaterial;
    [SerializeField] private bool _visualizeWall = true;
    
    // Wall state
    private float _confidence = 0.0f;
    private float _lastUpdateTime = 0f;
    private Vector3 _wallNormal;
    
    // Add field for ARPlaneManager
    private ARPlaneManager _arPlaneManager;
    
    // Public properties
    public float WallWidth { 
        get { return _wallWidth; }
        set { _wallWidth = value; UpdateWallMesh(); }
    }
    
    public float WallHeight {
        get { return _wallHeight; }
        set { _wallHeight = value; UpdateWallMesh(); }
    }
    
    public bool IsValid {
        get { return _isValid; }
        set { _isValid = value; }
    }
    
    public float Confidence {
        get { return _confidence; }
        set { _confidence = Mathf.Clamp01(value); }
    }
    
    public Vector3 WallNormal {
        get { return _wallNormal; }
    }
    
    public ARAnchor ARAnchor {
        get { return _arAnchor; }
        set { _arAnchor = value; }
    }
    
    public ARPlane ARPlane {
        get { return _arPlane; }
        set { _arPlane = value; }
    }
    
    public GameObject Wall {
        get { return _wall; }
        set { _wall = value; }
    }
    
    // Add fields for AR component IDs
    [SerializeField] private string _anchorID;
    [SerializeField] private string _planeID;
    
    // Добавим публичное свойство для _debugMode
    public bool DebugMode
    {
        get { return _debugMode; }
        set { _debugMode = value; }
    }
    
    private void Awake()
    {
        // Enable debug mode by default in development
        #if UNITY_EDITOR
        _debugMode = true;
        #endif
        
        if (_debugMode)
        {
            Debug.Log($"ARWallAnchor: Initializing {gameObject.name}");
        }
        
        // Find AR references if not already set
        if (_arAnchor == null)
            _arAnchor = GetComponent<ARAnchor>();
            
        if (_arPlane == null && _arAnchor != null)
            _arPlane = _arAnchor.GetComponent<ARPlane>();
            
        // Initialize wall normal to forward direction
        _wallNormal = transform.forward;
        
        // Create mesh renderer and filter if needed
        if (_visualizeWall)
        {
            EnsureMeshComponents();
        }
        
        // Create the wall object if not set
        if (_wall == null)
        {
            CreateWallObject();
        }
    }
    
    private void Start()
    {
        // Initialize wall mesh
        if (_visualizeWall)
        {
            UpdateWallMesh();
        }
        
        // Subscribe to AR Plane updated event if available
        if (_arPlane != null)
        {
            _arPlane.boundaryChanged += OnPlaneBoundaryChanged;
        }
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (_arPlane != null)
        {
            _arPlane.boundaryChanged -= OnPlaneBoundaryChanged;
        }
    }
    
    /// <summary>
    /// Обрабатывает изменение границ AR-плоскости
    /// </summary>
    private void OnPlaneBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
    {
        if (args.plane != _arPlane)
            return;
            
        // Update wall dimensions based on AR plane
        Bounds planeBounds = args.plane.GetComponent<MeshRenderer>().bounds;
        _wallWidth = Mathf.Max(planeBounds.size.x, planeBounds.size.z);
        
        // Update wall normal from plane
        _wallNormal = args.plane.normal;
        
        // Update the mesh
        UpdateWallMesh();
    }
    
    /// <summary>
    /// Создает и обновляет настраиваемую сетку для визуализации стены
    /// </summary>
    private void UpdateWallMesh()
    {
        if (!_visualizeWall)
            return;
            
        EnsureMeshComponents();
        
        // Create a simple quad mesh for the wall
        Mesh wallMesh = new Mesh();
        
        // Vertices for a quad
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-_wallWidth/2, 0, 0),              // Bottom-left
            new Vector3(_wallWidth/2, 0, 0),               // Bottom-right
            new Vector3(-_wallWidth/2, _wallHeight, 0),    // Top-left
            new Vector3(_wallWidth/2, _wallHeight, 0)      // Top-right
        };
        
        // Triangles (2 triangles for a quad)
        int[] triangles = new int[6]
        {
            0, 2, 1,    // First triangle
            1, 2, 3     // Second triangle
        };
        
        // UVs
        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0, 0),   // Bottom-left
            new Vector2(1, 0),   // Bottom-right
            new Vector2(0, 1),   // Top-left
            new Vector2(1, 1)    // Top-right
        };
        
        // Assign to mesh
        wallMesh.vertices = vertices;
        wallMesh.triangles = triangles;
        wallMesh.uv = uvs;
        
        // Recalculate bounds and normals
        wallMesh.RecalculateBounds();
        wallMesh.RecalculateNormals();
        
        // Apply to mesh filter
        _wallMeshFilter.mesh = wallMesh;
        
        // Apply material
        if (_wallMaterial != null && _wallMeshRenderer != null)
        {
            _wallMeshRenderer.material = _wallMaterial;
        }
        else
        {
            // Create default material if none provided
            if (_wallMaterial == null)
            {
                _wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _wallMaterial.color = new Color(0.8f, 0.8f, 0.8f, 0.6f); // Semi-transparent white
                
                if (_wallMeshRenderer != null)
                {
                    _wallMeshRenderer.material = _wallMaterial;
                    
                    // Set to transparent rendering
                    _wallMeshRenderer.material.SetFloat("_Surface", 1); // Transparent
                    _wallMeshRenderer.material.SetFloat("_Blend", 0);  // Alpha blend
                    _wallMeshRenderer.material.renderQueue = 3000;     // Transparent queue
                }
            }
        }
    }
    
    /// <summary>
    /// Убеждается, что компоненты MeshRenderer и MeshFilter существуют
    /// </summary>
    private void EnsureMeshComponents()
    {
        if (_wallMeshRenderer == null)
        {
            _wallMeshRenderer = GetComponent<MeshRenderer>();
            if (_wallMeshRenderer == null)
                _wallMeshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
        
        if (_wallMeshFilter == null)
        {
            _wallMeshFilter = GetComponent<MeshFilter>();
            if (_wallMeshFilter == null)
                _wallMeshFilter = gameObject.AddComponent<MeshFilter>();
        }
    }
    
    /// <summary>
    /// Обновляет положение и состояние привязки стены
    /// </summary>
    public void UpdateWallAnchor(Pose anchorPose, float confidence)
    {
        transform.position = anchorPose.position;
        transform.rotation = anchorPose.rotation;
        
        _confidence = confidence;
        _isValid = confidence > 0.5f;
        _lastUpdateTime = Time.time;
        
        // Update wall normal from rotation
        _wallNormal = transform.forward;
    }
    
    /// <summary>
    /// Обновляет размеры стены
    /// </summary>
    public void SetWallDimensions(float width, float height)
    {
        _wallWidth = width;
        _wallHeight = height;
        
        if (_wall == null)
        {
            if (_debugMode) Debug.LogWarning("Cannot set wall dimensions: wall object is null");
            return;
        }
        
        // Apply dimensions by scaling the wall
        _wall.transform.localScale = new Vector3(width, height, 0.1f); // Assuming wall is a simple cube with 0.1f depth
        
        // Update mesh collider if present
        MeshCollider meshCollider = _wall.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.enabled = false;
            meshCollider.enabled = true; // Refresh the collider
        }
        
        if (_debugMode)
        {
            Debug.Log($"Wall dimensions set - Width: {width}, Height: {height}");
        }
    }
    
    /// <summary>
    /// Изменяет цвет материала стены
    /// </summary>
    public void SetWallColor(Color color)
    {
        if (_wallMaterial != null)
        {
            _wallMaterial.color = color;
        }
        else if (_wallMeshRenderer != null && _wallMeshRenderer.material != null)
        {
            _wallMeshRenderer.material.color = color;
        }
    }
    
    /// <summary>
    /// Устанавливает кастомный материал для стены
    /// </summary>
    public void SetWallMaterial(Material material)
    {
        if (material != null)
        {
            _wallMaterial = material;
            
            if (_wallMeshRenderer != null)
            {
                _wallMeshRenderer.material = _wallMaterial;
            }
        }
    }
    
    /// <summary>
    /// Включает или выключает визуализацию стены
    /// </summary>
    public void SetVisualization(bool enabled)
    {
        _visualizeWall = enabled;
        
        if (_wallMeshRenderer != null)
        {
            _wallMeshRenderer.enabled = enabled;
        }
    }
    
    /// <summary>
    /// Проверяет, является ли эта стена частью той же плоскости, что и другая стена
    /// </summary>
    public bool IsSamePlaneAs(ARWallAnchor otherWall)
    {
        if (otherWall == null || _arPlane == null || otherWall.ARPlane == null)
            return false;
            
        return _arPlane.trackableId == otherWall.ARPlane.trackableId;
    }
    
    /// <summary>
    /// Anchors a wall to the specified AR plane
    /// </summary>
    public void AnchorWallPlane(TrackableId planeId)
    {
        if (_arPlaneManager == null)
        {
            _arPlaneManager = FindObjectOfType<ARPlaneManager>();
            if (_arPlaneManager == null)
            {
                Debug.LogError("ARWallAnchor: Cannot anchor wall - ARPlaneManager not found");
                return;
            }
        }
        
        // Find the AR plane with the given ID
        foreach (ARPlane plane in _arPlaneManager.trackables)
        {
            if (plane.trackableId == planeId)
            {
                // Set the AR plane reference
                _arPlane = plane;
                
                // Create or get ARAnchor component
                if (_arAnchor == null)
                {
                    _arAnchor = GetComponent<ARAnchor>();
                    if (_arAnchor == null)
                    {
                        _arAnchor = gameObject.AddComponent<ARAnchor>();
                    }
                }
                
                // Update wall dimensions based on plane
                Bounds planeBounds = plane.GetComponent<MeshRenderer>()?.bounds ?? new Bounds(plane.center, plane.size);
                _wallWidth = Mathf.Max(planeBounds.size.x, planeBounds.size.z);
                _wallHeight = Mathf.Max(2.4f, planeBounds.size.y * 1.5f); // Use a reasonable minimum height
                
                // Update wall normal
                _wallNormal = plane.normal;
                
                // Update the mesh
                UpdateWallMesh();
                
                Debug.Log($"ARWallAnchor: Successfully anchored wall to plane {planeId}");
                return;
            }
        }
        
        Debug.LogWarning($"ARWallAnchor: Could not find plane with ID {planeId}");
    }

    /// <summary>
    /// Updates the wall position and rotation based on the anchor position and plane normal
    /// </summary>
    private void UpdateWall()
    {
        // Убедимся, что стена существует
        if (_wall == null)
        {
            CreateWallObject();
            if (_debugMode) Debug.Log("Created wall object in UpdateWall");
        }

        // Даже если AR Anchor и AR Plane не назначены, 
        // мы все равно можем отобразить стену в текущей позиции
        
        // 1. Установим позицию стены равной позиции этого объекта
        _wall.transform.position = transform.position;
        
        // 2. Установим вращение стены
        // Если мы не можем использовать нормаль плоскости, используем текущее вращение объекта
        _wall.transform.rotation = transform.rotation;
        
        // 3. Установим размеры стены
        if (!Mathf.Approximately(_wallWidth, 0) && !Mathf.Approximately(_wallHeight, 0))
        {
            // Применяем локальный масштаб к стене
            _wall.transform.localScale = new Vector3(_wallWidth, _wallHeight, 0.1f);
        }
        else
        {
            // Используем значения по умолчанию
            _wall.transform.localScale = new Vector3(1.0f, 2.4f, 0.1f);
        }
        
        // 4. Обновим свойство IsValid - стена действительна, даже если компоненты AR не назначены
        _isValid = true;
        
        if (_debugMode)
        {
            Debug.Log($"Wall updated - Position: {_wall.transform.position}, Rotation: {_wall.transform.rotation.eulerAngles}");
        }
    }

    /// <summary>
    /// Creates a wall object if it doesn't exist
    /// </summary>
    private void CreateWallObject()
    {
        if (_wall != null) return;
        
        // Create a new wall object
        _wall = new GameObject("Wall");
        _wall.transform.parent = transform;
        _wall.transform.localPosition = Vector3.zero;
        _wall.transform.localRotation = Quaternion.identity;
        
        // Add mesh components for visualization
        MeshFilter meshFilter = _wall.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = _wall.AddComponent<MeshRenderer>();
        
        // Add a mesh collider
        MeshCollider meshCollider = _wall.AddComponent<MeshCollider>();
        
        // Store references to the mesh components
        _wallMeshFilter = meshFilter;
        _wallMeshRenderer = meshRenderer;
        
        // Create a simple quad mesh
        Mesh wallMesh = new Mesh();
        
        // Define vertices for a quad
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-0.5f, 0, 0),      // Bottom-left
            new Vector3(0.5f, 0, 0),       // Bottom-right
            new Vector3(-0.5f, 1, 0),      // Top-left
            new Vector3(0.5f, 1, 0)        // Top-right
        };
        
        // Define triangles
        int[] triangles = new int[6]
        {
            0, 2, 1,    // First triangle
            1, 2, 3     // Second triangle
        };
        
        // Define UVs
        Vector2[] uvs = new Vector2[4]
        {
            new Vector2(0, 0),   // Bottom-left
            new Vector2(1, 0),   // Bottom-right
            new Vector2(0, 1),   // Top-left
            new Vector2(1, 1)    // Top-right
        };
        
        // Set mesh data
        wallMesh.vertices = vertices;
        wallMesh.triangles = triangles;
        wallMesh.uv = uvs;
        
        // Recalculate normals and bounds
        wallMesh.RecalculateNormals();
        wallMesh.RecalculateBounds();
        
        // Assign mesh to mesh filter
        meshFilter.mesh = wallMesh;
        
        // Create a more noticeable material for the wall
        if (_wallMaterial == null)
        {
            _wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            
            // Используем яркий голубой цвет с прозрачностью
            _wallMaterial.color = new Color(0.2f, 0.7f, 1.0f, 0.7f);
            
            // Настраиваем материал для прозрачности
            _wallMaterial.SetFloat("_Surface", 1); // 1 = прозрачный
            _wallMaterial.SetFloat("_Blend", 0);   // 0 = альфа-смешивание
            _wallMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            _wallMaterial.renderQueue = 3000;      // Прозрачная очередь рендеринга
        }
        
        // Assign material to mesh renderer
        meshRenderer.material = _wallMaterial;
        
        // Применяем правильные размеры стены
        if (!Mathf.Approximately(_wallWidth, 0) && !Mathf.Approximately(_wallHeight, 0))
        {
            _wall.transform.localScale = new Vector3(_wallWidth, _wallHeight, 0.1f);
        }
        else
        {
            _wall.transform.localScale = new Vector3(1.0f, 2.4f, 0.1f);
        }
        
        if (_debugMode)
        {
            Debug.Log($"Created wall object: {_wall.name} with size {_wall.transform.localScale}");
        }
    }

    /// <summary>
    /// Explicitly sets the AR Anchor component and stores it
    /// </summary>
    public void SetARAnchor(ARAnchor anchor)
    {
        if (anchor == null)
        {
            if (_debugMode) Debug.LogWarning("Cannot set null AR Anchor");
            return;
        }
        
        _arAnchor = anchor;
        
        // Store anchor ID for future lookups
        _anchorID = anchor.trackableId.ToString();
        
        if (_debugMode)
        {
            Debug.Log($"AR Anchor explicitly set: {anchor.name}, ID: {_anchorID}");
        }
    }

    /// <summary>
    /// Explicitly sets the AR Plane component and stores it
    /// </summary>
    public void SetARPlane(ARPlane plane)
    {
        if (plane == null)
        {
            if (_debugMode) Debug.LogWarning("Cannot set null AR Plane");
            return;
        }
        
        _arPlane = plane;
        
        // Store plane ID for future lookups
        _planeID = plane.trackableId.ToString();
        
        if (_debugMode)
        {
            Debug.Log($"AR Plane explicitly set: {plane.trackableId}, Size: {plane.size}");
        }
    }

    /// <summary>
    /// Tries to restore AR components by saved IDs
    /// </summary>
    public void TryRestoreARComponents()
    {
        if (_debugMode)
        {
            Debug.Log($"Trying to restore AR components using IDs - Anchor: {_anchorID}, Plane: {_planeID}");
        }
        
        // Try to restore AR Anchor if needed
        if (_arAnchor == null && !string.IsNullOrEmpty(_anchorID))
        {
            // In case anchor is in parent
            if (transform.parent != null)
            {
                _arAnchor = transform.parent.GetComponent<ARAnchor>();
                if (_arAnchor != null && _debugMode)
                    Debug.Log($"Restored AR Anchor from parent: {transform.parent.name}");
            }
        }
        
        // Try to restore AR Plane if needed
        if (_arPlane == null && !string.IsNullOrEmpty(_planeID))
        {
            var arPlaneManager = FindObjectOfType<ARPlaneManager>();
            if (arPlaneManager != null)
            {
                foreach (var plane in arPlaneManager.trackables)
                {
                    if (plane.trackableId.ToString() == _planeID)
                    {
                        _arPlane = plane;
                        if (_debugMode)
                            Debug.Log($"Restored AR Plane with ID: {_planeID}");
                        break;
                    }
                }
            }
        }
    }

    private void Update()
    {
        // Try to restore AR components if needed
        if ((_arAnchor == null || _arPlane == null) && (Time.frameCount % 30 == 0)) // Check every 30 frames
        {
            TryRestoreARComponents();
        }
        
        // Create wall if not set
        if (_wall == null)
        {
            CreateWallObject();
        }
        
        // Update wall position and dimensions
        UpdateWall();
    }

    private void OnEnable()
    {
        // Пытаемся восстановить компоненты при активации объекта
        if (_arAnchor == null || _arPlane == null)
        {
            if (_debugMode) Debug.Log($"OnEnable: Trying to restore components");
            TryRestoreARComponents();
        }
        
        // Убеждаемся, что стена создана
        if (_wall == null)
        {
            if (_debugMode) Debug.Log($"OnEnable: Creating wall object");
            CreateWallObject();
        }
    }
} 
