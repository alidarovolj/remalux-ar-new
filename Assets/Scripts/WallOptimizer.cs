using System.Collections.Generic;
using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Linq;
using OpenCvSharpPoint = OpenCvSharp.Point;
using OpenCvSharpPoint2f = OpenCvSharp.Point2f;
using OpenCvSharpRect = OpenCvSharp.Rect;
using OpenCvSharpMat = OpenCvSharp.Mat;
using OpenCvSharpSize = OpenCvSharp.Size;
using OpenCvSharpMoments = OpenCvSharp.Moments;
using OpenCvSharpHierarchyIndex = OpenCvSharp.HierarchyIndex;

// Add standard warning disables
#pragma warning disable 0169, 0649
#pragma warning disable IDE0051, IDE0044
#pragma warning disable CS0414
#pragma warning disable IDE0090
#pragma warning disable CS0169
#pragma warning disable IDE1006

// If OpenCvSharp is not available, define dummy types for compilation
#if !USING_OPENCVSHARP
namespace OpenCvSharp
{
    // Create dummy types to allow compilation
    public struct Point { }
    public struct Point2f 
    { 
        public float X;
        public float Y;
    }
    public struct Moments 
    { 
        public double M00;
        public double M10;
        public double M01;
    }
    public struct Rect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }
    public enum MorphShapes { Rect }
    public enum MorphTypes { Close, Open }
    public enum RetrievalModes { External }
    public enum ContourApproximationModes { ApproxSimple }
    public struct HierarchyIndex { }
    public enum MatType { CV_8UC1, CV_32FC1 }
    public static class Cv2
    {
        public static Moments Moments(Point[] contour) { return new Moments(); }
        public static Point[][] FindContours(Mat src, out Point[][] contours, out HierarchyIndex[] hierarchy, 
            RetrievalModes mode, ContourApproximationModes method)
        {
            contours = new Point[0][];
            hierarchy = new HierarchyIndex[0];
            return contours;
        }
        public static Mat GetStructuringElement(MorphShapes shape, Size size) { return default(Mat); }
        public static void MorphologyEx(Mat src, Mat dst, MorphTypes op, Mat kernel, int iterations = 1) { }
        public static double ContourArea(Point[] contour) { return 0; }
        public static Rect BoundingRect(Point[] contour) { return new Rect(); }
    }
    public struct Size
    {
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }
        public int Width;
        public int Height;
    }
    public struct Mat : System.IDisposable
    {
        // Remove parameterless constructor for C# 9.0 compatibility
        // In C# 9.0, structs can't have parameterless constructors
        
        // Constructor with parameters is allowed in C# 9.0
        public Mat(int rows, int cols, MatType type) 
        {
            // Initialize as needed
            DataPointer = null;
        }
        
        public unsafe byte* DataPointer;
        public void Dispose() { }
    }
}
#endif

/// <summary>
/// Улучшенный оптимизатор обнаружения стен с дополнительной фильтрацией
/// для устранения ложных срабатываний
/// </summary>
public unsafe class WallOptimizer : MonoBehaviour
{
    [Header("Параметры сегментации")]
    [Tooltip("ID класса стены в сегментационной модели")]
    public byte wallClassId = 9; // класс "wall" в модели ADE20K
    
    [Tooltip("Минимальный порог уверенности для пикселей стены (0-1)")]
    [Range(0.1f, 1.0f)]
    public float confidenceThreshold = 0.5f;
    
    [Header("Фильтры контуров")]
    [Tooltip("Минимальная площадь контура в пикселях")]
    public float minContourArea = 3000f; // минимальный размер области
    
    [Tooltip("Минимальное соотношение сторон (width/height)")]
    public float minAspectRatio = 0.3f;
    
    [Tooltip("Максимальное соотношение сторон (width/height)")]
    public float maxAspectRatio = 4.0f;
    
    [Header("Морфологические операции")]
    [Tooltip("Применять морфологические операции для сглаживания")]
    public bool useMorphology = true;
    
    [Tooltip("Размер ядра для операций морфологии")]
    [Range(1, 10)]
    public int morphKernelSize = 3;
    
    [Header("3D-параметры")]
    [Tooltip("Минимальная площадь стены в кв. метрах")]
    public float minWallArea = 1.5f; // минимальный размер для создания стены
    
    [Tooltip("Максимальное расстояние для объединения близких стен")]
    public float wallMergeDistance = 0.5f;
    
    [Header("Отладка")]
    public bool showDebugInfo = true;
    
    [Header("Performance")]
    [Tooltip("How often to update wall detection (in seconds)")]
    [Range(0.1f, 2.0f)]
    public float updateInterval = 0.5f;
    
    [Tooltip("Maximum number of walls to create per frame")]
    [Range(1, 10)]
    public int maxWallsPerFrame = 5;
    
    [Tooltip("Minimum width of a contour in pixels")]
    public float minContourWidth = 50f;
    
    [Tooltip("Minimum height of a contour in pixels")]
    public float minContourHeight = 50f;
    
    [Tooltip("Scale to convert from pixels to world units")]
    public float worldUnitScale = 0.01f;
    
    [Tooltip("Thickness of created wall meshes")]
    public float wallThickness = 0.1f;
    
    [Tooltip("Distance from camera for placing walls when no surface is detected")]
    public float wallDistanceFromCamera = 2.0f;
    
    [Tooltip("Layer mask for wall detection raycasts")]
    public LayerMask wallLayerMask = 1; // Default layer
    
    [Header("Mesh Processing")]
    [Tooltip("Enable processing of mesh filters in the scene")]
    public bool meshFilterProcessingEnabled = true;
    
    [Tooltip("Maximum number of processed meshes to track")]
    public int maxProcessedMeshes = 100;
    
    [Tooltip("Maximum number of new meshes to find per frame")]
    public int maxNewMeshesPerFrame = 5;
    
    [Tooltip("Maximum number of meshes to process per frame")]
    public int maxMeshesPerFrame = 3;
    
    [Tooltip("Automatically combine nearby walls")]
    public bool autoCombineWalls = true;
    
    [Tooltip("How often to combine walls (in frames)")]
    public int combineWallsInterval = 30;
    
    // Ссылки на компоненты
    private EnhancedDeepLabPredictor predictor;
    private WallMeshRenderer meshRenderer;
    
    // Internal fields
    private int segmentationWidth = 0;
    private int segmentationHeight = 0;
    private List<Bounds> existingWallBounds = new List<Bounds>();
    private float lastUpdateTime = 0f;
    private GameObject wallParent;
    private EnhancedDeepLabPredictor enhancedPredictor;

    private void Awake()
    {
        // Initialize wall parameters
        existingWallBounds = new List<Bounds>();
        
        // Create a parent object for all wall meshes
        if (wallParent == null)
        {
            wallParent = new GameObject("Wall Meshes");
            wallParent.transform.SetParent(transform);
        }
        
        // Get segmentation setup
        enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        if (enhancedPredictor != null)
        {
            segmentationWidth = enhancedPredictor.TextureWidth;
            segmentationHeight = enhancedPredictor.TextureHeight;
            
            if (showDebugInfo)
                Debug.Log($"WallOptimizer: Using enhancedPredictor with dimensions {segmentationWidth}x{segmentationHeight}");
            
            // Subscribe to segmentation updates
            enhancedPredictor.OnSegmentationUpdated.AddListener(OnSegmentationUpdated);
        }
        
        // Initialize the wall layer mask
        wallLayerMask = LayerMask.GetMask("Default");
    }
    
    private void Start()
    {
        predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        meshRenderer = FindObjectOfType<WallMeshRenderer>();
        
        if (predictor != null)
        {
            // Sync wall class ID with predictor to ensure consistency
            // This ensures both components use the same wall class ID (9 for ADE20K)
            if (predictor.WallClassId != wallClassId)
            {
                Debug.LogWarning($"WallOptimizer: Synchronizing wallClassId from {wallClassId} to predictor's value {predictor.WallClassId}");
                wallClassId = predictor.WallClassId;
            }
            
            Debug.Log($"WallOptimizer: Using wallClassId: {wallClassId} for wall detection");
            
            // Update segmentation dimensions
            segmentationWidth = predictor.TextureWidth;
            segmentationHeight = predictor.TextureHeight;
            
            Debug.Log($"WallOptimizer: Segmentation dimensions {segmentationWidth}x{segmentationHeight}");
        }
        else
        {
            Debug.LogError("WallOptimizer: EnhancedDeepLabPredictor not found!");
        }
        
        if (meshRenderer == null)
        {
            Debug.LogError("WallOptimizer: WallMeshRenderer not found!");
        }
        
        // Initialize walls
        Initialize();
    }
    
    private void OnEnable()
    {
        // Reset state
        lastUpdateTime = 0f;
        
        // Clear existing walls
        if (meshRenderer != null)
        {
            meshRenderer.ClearWalls();
        }
    }
    
    private void OnDisable()
    {
        // Clean up resources
        if (enhancedPredictor != null)
        {
            enhancedPredictor.OnSegmentationUpdated.RemoveListener(OnSegmentationUpdated);
        }
        
        if (meshRenderer != null)
        {
            meshRenderer.ClearWalls();
        }
    }
    
    private void OnSegmentationUpdated(Texture2D segmentationResult)
    {
        if (segmentationResult != null)
        {
            ProcessWallMask(segmentationResult.GetPixels32(), Camera.main);
        }
    }

    private void Update()
    {
        // Throttle updates to avoid performance issues
        if (Time.time - lastUpdateTime < updateInterval)
            return;
            
        lastUpdateTime = Time.time;
        
        // Process the current frame
        ProcessCurrentFrame();
    }
    
    /// <summary>
    /// Process the segmentation mask to identify wall regions
    /// </summary>
    private unsafe void ProcessWallMask(Color32[] segmentationMask, Camera camera)
    {
        if (segmentationMask == null || segmentationMask.Length == 0 || camera == null)
        {
            if (showDebugInfo)
                Debug.LogWarning("WallOptimizer: Invalid segmentation mask or camera");
            return;
        }

        OpenCvSharpMat binaryMask = default(OpenCvSharpMat);
        OpenCvSharpMat confidenceMap = default(OpenCvSharpMat);
        OpenCvSharpMat kernel = default(OpenCvSharpMat);
        OpenCvSharpMat processedMask = default(OpenCvSharpMat);
        
        try
        {
            // Segmentation mask dimensions
            if (segmentationWidth <= 0 || segmentationHeight <= 0)
            {
                Debug.LogError("WallOptimizer: Invalid segmentation dimensions");
                return;
            }

            if (segmentationMask.Length != segmentationWidth * segmentationHeight)
            {
                Debug.LogError($"WallOptimizer: Mask dimensions mismatch. Expected {segmentationWidth}x{segmentationHeight}={segmentationWidth * segmentationHeight}, got {segmentationMask.Length}");
                return;
            }

            // Create binary mask for walls - using constructor with parameters
            binaryMask = new OpenCvSharpMat(segmentationHeight, segmentationWidth, OpenCvSharp.MatType.CV_8UC1);
            
            // Create a confidence map - using constructor with parameters
            confidenceMap = new OpenCvSharpMat(segmentationHeight, segmentationWidth, OpenCvSharp.MatType.CV_32FC1);
            
            // Using unsafe code for better performance
            unsafe
            {
                byte* binaryPtr = binaryMask.DataPointer;
                float* confidencePtr = (float*)confidenceMap.DataPointer;
                
                for (int i = 0; i < segmentationMask.Length; i++)
                {
                    if (segmentationMask[i].r == wallClassId)
                    {
                        float confidence = segmentationMask[i].a / 255.0f;
                        
                        // Apply confidence threshold
                        if (confidence >= confidenceThreshold)
                        {
                            binaryPtr[i] = 255; // Set pixel as wall
                            confidencePtr[i] = confidence;
                        }
                        else
                        {
                            binaryPtr[i] = 0;
                            confidencePtr[i] = 0;
                        }
                    }
                    else
                    {
                        binaryPtr[i] = 0;
                        confidencePtr[i] = 0;
                    }
                }
            }
            
            // Apply morphological operations to reduce noise
            kernel = OpenCvSharp.Cv2.GetStructuringElement(OpenCvSharp.MorphShapes.Rect, new OpenCvSharpSize(3, 3));
            
            // Closing operation (dilation followed by erosion)
            // Initialize processedMask with parameters instead of parameterless constructor
            processedMask = new OpenCvSharpMat(segmentationHeight, segmentationWidth, OpenCvSharp.MatType.CV_8UC1);
            OpenCvSharp.Cv2.MorphologyEx(binaryMask, processedMask, OpenCvSharp.MorphTypes.Close, kernel, iterations: 2);
            
            // Opening operation to remove small noise (erosion followed by dilation)
            OpenCvSharp.Cv2.MorphologyEx(processedMask, processedMask, OpenCvSharp.MorphTypes.Open, kernel, iterations: 1);
            
            // Find contours in the processed mask
            OpenCvSharpPoint[][] contours;
            OpenCvSharpHierarchyIndex[] hierarchy;
            OpenCvSharp.Cv2.FindContours(processedMask, out contours, out hierarchy, OpenCvSharp.RetrievalModes.External, OpenCvSharp.ContourApproximationModes.ApproxSimple);
            
            if (showDebugInfo)
                Debug.Log($"WallOptimizer: Found {contours.Length} wall contours in segmentation mask");
            
            // Create walls from the filtered contours
            CreateWallsFromContours(contours, hierarchy, camera);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallOptimizer: Error processing wall mask: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            // Release OpenCV resources
            if (binaryMask.DataPointer != null) binaryMask.Dispose();
            if (confidenceMap.DataPointer != null) confidenceMap.Dispose();
            if (kernel.DataPointer != null) kernel.Dispose();
            if (processedMask.DataPointer != null) processedMask.Dispose();
        }
    }
    
    /// <summary>
    /// Calculates the world position and dimensions from a contour
    /// </summary>
    private (Vector3 position, Vector3 normal, float width, float height) CalculateWorldPositionFromContour(
        OpenCvSharpPoint[] contour, 
        OpenCvSharpRect boundingRect, 
        Camera camera)
    {
        // Calculate centroid of contour
        OpenCvSharpMoments moments = OpenCvSharp.Cv2.Moments(contour);
        
        // Create Point2f with default values then set its properties
        // This avoids using a parameterless constructor
        OpenCvSharpPoint2f centroid = default(OpenCvSharpPoint2f);
        
        if (moments.M00 != 0)
        {
            centroid.X = (float)(moments.M10 / moments.M00);
            centroid.Y = (float)(moments.M01 / moments.M00);
        }
        else
        {
            // Fallback to center of bounding rect if moments calculation fails
            centroid.X = boundingRect.X + boundingRect.Width / 2f;
            centroid.Y = boundingRect.Y + boundingRect.Height / 2f;
        }
        
        // Convert pixel coordinates to normalized viewport space (0-1)
        float normX = centroid.X / segmentationWidth;
        float normY = 1.0f - (centroid.Y / segmentationHeight); // Flip Y-axis
        
        // Cast ray from camera through the centroid point
        Ray ray = camera.ViewportPointToRay(new Vector3(normX, normY, 0));
        
        // Use raycast to find position in world space
        Vector3 position;
        Vector3 normal;
        
        if (Physics.Raycast(ray, out RaycastHit hit, 10f, LayerMask.GetMask("Default")))
        {
            // We hit something in the real world
            position = hit.point;
            normal = hit.normal;
        }
        else
        {
            // Fallback - place at a fixed distance from camera
            position = ray.GetPoint(2f); // Adjust this distance as needed
            normal = -ray.direction; // Face toward camera
        }
        
        // Return wall parameters
        return (position, normal, boundingRect.Width, boundingRect.Height);
    }
    
    /// <summary>
    /// Создает 3D-стены из отфильтрованных контуров
    /// </summary>
    private void CreateWallsFromContours(OpenCvSharpPoint[][] contours, OpenCvSharpHierarchyIndex[] hierarchy, Camera camera)
    {
        if (contours == null || contours.Length == 0 || camera == null)
            return;
        
        int wallsCreated = 0;
        int validContours = 0;
        
        for (int i = 0; i < contours.Length && wallsCreated < maxWallsPerFrame; i++)
        {
            // Skip small contours
            double area = OpenCvSharp.Cv2.ContourArea(contours[i]);
            if (area < minContourArea)
                continue;
            
            validContours++;
            
            // Get bounding rectangle
            OpenCvSharpRect boundingRect = OpenCvSharp.Cv2.BoundingRect(contours[i]);
            
            // Skip if rectangle is too small
            if (boundingRect.Width < minContourWidth || boundingRect.Height < minContourHeight)
                continue;
                
            // Calculate wall position and parameters
            var (position, normal, width, height) = CalculateWorldPositionFromContour(contours[i], boundingRect, camera);
            
            // Try to create or update wall
            CreateOrUpdateWall(position, normal, width, height);
            wallsCreated++;
        }
        
        if (showDebugInfo && validContours > 0)
        {
            Debug.Log($"WallOptimizer: Processed {validContours} valid contours, created/updated {wallsCreated} walls");
        }
    }
    
    /// <summary>
    /// Process the current frame and request wall segmentation from the predictor
    /// </summary>
    public void ProcessCurrentFrame()
    {
        if (predictor != null)
        {
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Requesting segmentation for wall class ID: {wallClassId}");
            }
            
            Texture2D segmentationResult = predictor.GetSegmentationForClass(wallClassId);
            
            if (segmentationResult != null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"WallOptimizer: Processing segmentation result with dimensions {segmentationResult.width}x{segmentationResult.height}");
                }
                ProcessWallMask(segmentationResult.GetPixels32(), Camera.main);
            }
            else if (showDebugInfo)
            {
                Debug.LogWarning($"WallOptimizer: No segmentation result received for class {wallClassId}");
            }
        }
        else
        {
            if (showDebugInfo)
            {
                Debug.LogError("WallOptimizer: Cannot process frame - predictor is null");
            }
        }
    }

    /// <summary>
    /// Creates or updates a wall at the specified position with the given dimensions and orientation
    /// </summary>
    /// <param name="position">World position for the wall's center</param>
    /// <param name="normal">Wall normal direction (perpendicular to wall surface)</param>
    /// <param name="width">Width in pixels from segmentation</param>
    /// <param name="height">Height in pixels from segmentation</param>
    private void CreateOrUpdateWall(Vector3 position, Vector3 normal, float width, float height)
    {
        if (meshRenderer == null) return;
        
        // Skip if dimensions are too small
        if (width < minContourWidth || height < minContourHeight)
        {
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Skipping wall - dimensions too small: {width}x{height}");
            }
            return;
        }
        
        // Calculate aspect ratio and check if it's within bounds
        float aspectRatio = width / height;
        if (aspectRatio < minAspectRatio || aspectRatio > maxAspectRatio)
        {
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Skipping wall - aspect ratio out of bounds: {aspectRatio}");
            }
            return;
        }
        
        // Calculate wall dimensions (convert from pixels to world units)
        float wallWidth = width * worldUnitScale;
        float wallHeight = height * worldUnitScale;
        
        // Calculate the world space area of the wall
        float wallArea = wallWidth * wallHeight;
        if (wallArea < minWallArea)
        {
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Skipping wall - area too small: {wallArea} m²");
            }
            return;
        }
        
        // Check if we should merge with an existing wall
        int nearbyWallIndex = -1;
        float nearestDistance = wallMergeDistance;
        float bestNormalAlignment = 0.7f; // Minimum threshold for normal alignment
        
        for (int i = 0; i < existingWallBounds.Count; i++)
        {
            // Check distance between centers
            float distance = Vector3.Distance(existingWallBounds[i].center, position);
            
            // Get normal alignment (how parallel the walls are)
            Vector3 existingNormal = meshRenderer.GetWallNormal(i);
            float normalAlignment = Vector3.Dot(existingNormal, normal);
            
            // Check if walls are close enough and facing same direction
            if (distance < nearestDistance && normalAlignment > bestNormalAlignment)
            {
                // Check if the projected areas overlap
                Vector3 delta = position - existingWallBounds[i].center;
                float projectedDist = Vector3.ProjectOnPlane(delta, normal).magnitude;
                
                // If the walls are very close when projected onto their plane
                if (projectedDist < wallMergeDistance * 1.5f) 
                {
                    nearestDistance = distance;
                    nearbyWallIndex = i;
                    bestNormalAlignment = normalAlignment;
                }
            }
        }
        
        if (nearbyWallIndex >= 0)
        {
            // Merge with existing wall - expand bounds if necessary
            Bounds existingBounds = existingWallBounds[nearbyWallIndex];
            Vector3 newSize = existingBounds.size;
            Vector3 newCenter = existingBounds.center;
            
            // Calculate merged size and position
            // Expand the wall size in both directions if needed
            newSize.x = Mathf.Max(newSize.x, wallWidth);
            newSize.y = Mathf.Max(newSize.y, wallHeight);
            
            // Weighted average position, biased toward larger wall
            float existingArea = existingBounds.size.x * existingBounds.size.y;
            float newArea = wallWidth * wallHeight;
            float weightFactor = existingArea / (existingArea + newArea);
            newCenter = Vector3.Lerp(position, newCenter, weightFactor);
            
            // Update existing wall
            meshRenderer.UpdateWallMesh(nearbyWallIndex, newCenter, newSize, Quaternion.LookRotation(normal));
            existingWallBounds[nearbyWallIndex] = new Bounds(newCenter, newSize);
            
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Merged with existing wall at index {nearbyWallIndex}, new size: {newSize}");
            }
        }
        else
        {
            // Create a new wall
            Quaternion rotation = Quaternion.LookRotation(normal);
            Vector3 size = new Vector3(wallWidth, wallHeight, wallThickness);
            
            // Create the wall mesh
            int wallIndex = meshRenderer.CreateWallMesh(position, size, rotation);
            
            if (wallIndex >= 0)
            {
                // Add to existing walls list
                Bounds newWallBounds = new Bounds(position, size);
                existingWallBounds.Add(newWallBounds);
                
                if (showDebugInfo)
                {
                    Debug.Log($"WallOptimizer: Created new wall at {position}, size: {size}, normal: {normal}");
                }
            }
        }
    }
    
    /// <summary>
    /// Initialize and prepare for wall detection
    /// </summary>
    public void Initialize()
    {
        // Clear existing walls
        if (meshRenderer != null)
        {
            meshRenderer.ClearWalls();
        }
        
        // Reset the list of wall bounds
        existingWallBounds.Clear();
        
        // Set up the segmentation width and height
        if (predictor != null)
        {
            segmentationWidth = predictor.TextureWidth;
            segmentationHeight = predictor.TextureHeight;
            
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Initialized with segmentation size {segmentationWidth}x{segmentationHeight}, wall class ID: {wallClassId}");
            }
        }
    }

    /// <summary>
    /// Clear all existing walls
    /// </summary>
    public void ClearWalls()
    {
        if (meshRenderer != null)
        {
            meshRenderer.ClearWalls();
            existingWallBounds.Clear();
            
            if (showDebugInfo)
            {
                Debug.Log("WallOptimizer: Cleared all walls");
            }
        }
    }

    /// <summary>
    /// Process a mesh filter to determine if it's a vertical surface
    /// </summary>
    /// <param name="meshFilter">The mesh filter to process</param>
    /// <returns>True if the mesh was processed as a wall</returns>
    private bool ProcessMesh(MeshFilter meshFilter)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null || meshFilter.gameObject == null)
            return false;
            
        // Skip small meshes
        if (!IsMeshLargeEnough(meshFilter.sharedMesh))
            return false;
            
        // Calculate the average normal in world space
        Vector3 avgNormal = CalculateAverageNormal(meshFilter);
        
        // Check if this is a vertical surface
        if (IsVerticalSurface(avgNormal))
        {
            // Get the renderer
            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null)
                return false;
                
            // Skip meshes that already have a wall material
            if (renderer.sharedMaterial.name.Contains("Wall"))
                return false;
                
            // Get mesh bounds in world space
            Bounds meshBounds = GetWorldBounds(meshFilter);
            
            // Create a wall representation
            Vector3 position = meshBounds.center;
            Vector3 normal = avgNormal;
            
            // Calculate dimensions in world units
            float width = Vector3.Project(meshBounds.size, Vector3.right).magnitude;
            float height = Vector3.Project(meshBounds.size, Vector3.up).magnitude;
            
            // Create or update wall
            CreateOrUpdateWall(position, normal, width * 100, height * 100); // Convert to same scale as contour-based walls
            
            if (showDebugInfo)
            {
                Debug.Log($"WallOptimizer: Processed mesh as wall - position: {position}, normal: {normal}");
                Debug.DrawRay(position, normal, Color.red, 2.0f);
            }
            
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Calculate the average normal of a mesh
    /// </summary>
    private Vector3 CalculateAverageNormal(MeshFilter meshFilter)
    {
        Mesh mesh = meshFilter.sharedMesh;
        Vector3[] normals = mesh.normals;
        Vector3[] vertices = mesh.vertices;
        
        if (normals == null || normals.Length == 0)
            return Vector3.up;
            
        // For very large meshes, sample a subset of normals
        int sampleCount = Mathf.Min(normals.Length, 100);
        Vector3 avgNormal = Vector3.zero;
        
        if (sampleCount < normals.Length)
        {
            // Sample random normals
            for (int i = 0; i < sampleCount; i++)
            {
                int index = UnityEngine.Random.Range(0, normals.Length);
                avgNormal += meshFilter.transform.TransformDirection(normals[index]);
            }
        }
        else
        {
            // Use all normals
            for (int i = 0; i < normals.Length; i++)
            {
                avgNormal += meshFilter.transform.TransformDirection(normals[i]);
            }
        }
        
        if (avgNormal == Vector3.zero)
            return Vector3.up;
            
        return avgNormal.normalized;
    }
    
    /// <summary>
    /// Determines if a surface is vertical based on its normal
    /// </summary>
    private bool IsVerticalSurface(Vector3 normal)
    {
        // Fast check for horizontal normals
        if (Mathf.Abs(normal.y) > 0.8f)
            return false;
            
        // Calculate angle with up vector
        float angle = Vector3.Angle(normal, Vector3.up);
        
        // Vertical surfaces have angles close to 90 degrees
        return angle > 70f && angle < 110f;
    }
    
    /// <summary>
    /// Get the bounds of a mesh in world space
    /// </summary>
    private Bounds GetWorldBounds(MeshFilter meshFilter)
    {
        Bounds localBounds = meshFilter.sharedMesh.bounds;
        Bounds worldBounds = new Bounds(
            meshFilter.transform.TransformPoint(localBounds.center),
            Vector3.zero
        );
        
        // Transform each corner of the local bounds to world space
        Vector3[] corners = new Vector3[8];
        corners[0] = new Vector3(localBounds.min.x, localBounds.min.y, localBounds.min.z);
        corners[1] = new Vector3(localBounds.min.x, localBounds.min.y, localBounds.max.z);
        corners[2] = new Vector3(localBounds.min.x, localBounds.max.y, localBounds.min.z);
        corners[3] = new Vector3(localBounds.min.x, localBounds.max.y, localBounds.max.z);
        corners[4] = new Vector3(localBounds.max.x, localBounds.min.y, localBounds.min.z);
        corners[5] = new Vector3(localBounds.max.x, localBounds.min.y, localBounds.max.z);
        corners[6] = new Vector3(localBounds.max.x, localBounds.max.y, localBounds.min.z);
        corners[7] = new Vector3(localBounds.max.x, localBounds.max.y, localBounds.max.z);
        
        for (int i = 0; i < 8; i++)
        {
            worldBounds.Encapsulate(meshFilter.transform.TransformPoint(corners[i]));
        }
        
        return worldBounds;
    }
    
    /// <summary>
    /// Check if a mesh is large enough to be considered a wall
    /// </summary>
    private bool IsMeshLargeEnough(Mesh mesh)
    {
        if (mesh == null)
            return false;
            
        Bounds bounds = mesh.bounds;
        float area = bounds.size.x * bounds.size.y;
        
        // Skip tiny meshes
        return area > 0.5f;
    }

    /// <summary>
    /// Combines nearby walls to reduce draw calls and improve performance
    /// </summary>
    private void CombineNearbyWalls()
    {
        if (existingWallBounds.Count < 2 || meshRenderer == null)
            return;
            
        bool wallsCombined = false;
        
        // Start from the end to avoid index issues when removing items
        for (int i = existingWallBounds.Count - 1; i >= 0; i--)
        {
            if (i >= existingWallBounds.Count) continue; // Skip if index is out of range
            
            Bounds wallBounds = existingWallBounds[i];
            Vector3 wallNormal = meshRenderer.GetWallNormal(i);
            
            // Find nearby walls to combine with
            for (int j = i - 1; j >= 0; j--)
            {
                if (j >= existingWallBounds.Count) continue; // Skip if index is out of range
                
                Bounds otherBounds = existingWallBounds[j];
                Vector3 otherNormal = meshRenderer.GetWallNormal(j);
                
                // Check distance and normal alignment
                float distance = Vector3.Distance(wallBounds.center, otherBounds.center);
                float normalAlignment = Vector3.Dot(wallNormal, otherNormal);
                
                // If walls are close enough and facing same direction
                if (distance < wallMergeDistance && normalAlignment > 0.7f)
                {
                    // Create a new combined bounds
                    Bounds combinedBounds = new Bounds(wallBounds.center, wallBounds.size);
                    combinedBounds.Encapsulate(otherBounds);
                    
                    // Update the wall mesh
                    Quaternion rotation = Quaternion.LookRotation(
                        (wallNormal + otherNormal).normalized
                    );
                    
                    meshRenderer.UpdateWallMesh(j, combinedBounds.center, combinedBounds.size, rotation);
                    
                    // Remove the other wall
                    meshRenderer.RemoveWallMesh(i);
                    existingWallBounds.RemoveAt(i);
                    
                    // Update the combined wall bounds
                    existingWallBounds[j] = combinedBounds;
                    
                    wallsCombined = true;
                    break; // Break since we removed wall i
                }
            }
            
            if (wallsCombined) break; // Break and run again next frame
        }
        
        if (wallsCombined && showDebugInfo)
        {
            Debug.Log($"WallOptimizer: Combined nearby walls. Remaining wall count: {existingWallBounds.Count}");
        }
    }

    /// <summary>
    /// Gets the normal direction of a wall
    /// </summary>
    private Vector3 GetWallNormal(int wallIndex)
    {
        if (meshRenderer == null || wallIndex < 0 || wallIndex >= existingWallBounds.Count)
            return Vector3.up;
            
        try
        {
            // Try to get the method via reflection
            var getWallNormalMethod = meshRenderer.GetType().GetMethod("GetWallNormal");
            if (getWallNormalMethod != null)
            {
                return (Vector3)getWallNormalMethod.Invoke(meshRenderer, new object[] { wallIndex });
            }
            
            // If not available, estimate from the rotation of the wall
            var getWallMethod = meshRenderer.GetType().GetMethod("GetWall");
            if (getWallMethod != null)
            {
                var wall = getWallMethod.Invoke(meshRenderer, new object[] { wallIndex });
                if (wall != null)
                {
                    var getTransformMethod = wall.GetType().GetMethod("get_transform");
                    if (getTransformMethod != null)
                    {
                        var transform = getTransformMethod.Invoke(wall, null) as Transform;
                        if (transform != null)
                        {
                            return transform.forward;
                        }
                    }
                }
            }
            
            // Default forward direction if all else fails
            return Vector3.forward;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error getting wall normal: {e.Message}");
            return Vector3.forward;
        }
    }
    
    /// <summary>
    /// Updates a wall mesh with new position, size, and rotation
    /// </summary>
    private void UpdateWallMesh(int wallIndex, Vector3 position, Vector3 size, Quaternion rotation)
    {
        if (meshRenderer == null || wallIndex < 0 || wallIndex >= existingWallBounds.Count)
            return;
            
        try
        {
            // Try to get the method via reflection
            var updateWallMethod = meshRenderer.GetType().GetMethod("UpdateWallMesh");
            if (updateWallMethod != null)
            {
                updateWallMethod.Invoke(meshRenderer, new object[] { wallIndex, position, size, rotation });
                return;
            }
            
            // Fallback to other possible method names
            var updateWallBoundsMethod = meshRenderer.GetType().GetMethod("UpdateWallBounds");
            if (updateWallBoundsMethod != null)
            {
                updateWallBoundsMethod.Invoke(meshRenderer, new object[] { wallIndex, position, size, rotation });
                return;
            }
            
            // If no update method exists, try to at least update the transform
            var getWallMethod = meshRenderer.GetType().GetMethod("GetWall");
            if (getWallMethod != null)
            {
                var wall = getWallMethod.Invoke(meshRenderer, new object[] { wallIndex });
                if (wall != null)
                {
                    var getTransformMethod = wall.GetType().GetMethod("get_transform");
                    if (getTransformMethod != null)
                    {
                        var transform = getTransformMethod.Invoke(wall, null) as Transform;
                        if (transform != null)
                        {
                            transform.position = position;
                            transform.rotation = rotation;
                            transform.localScale = size;
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error updating wall mesh: {e.Message}");
        }
    }
    
    /// <summary>
    /// Creates a new wall mesh
    /// </summary>
    private int CreateWallMesh(Vector3 position, Vector3 size, Quaternion rotation)
    {
        if (meshRenderer == null)
            return -1;
            
        try
        {
            // Try to get the method via reflection
            var createWallMethod = meshRenderer.GetType().GetMethod("CreateWallMesh");
            if (createWallMethod != null)
            {
                // Check parameter count to handle different overloads
                var parameters = createWallMethod.GetParameters();
                object result;
                
                if (parameters.Length == 3)
                {
                    // Overload with 3 parameters
                    result = createWallMethod.Invoke(meshRenderer, new object[] { position, size, rotation });
                }
                else if (parameters.Length == 4)
                {
                    // Overload with 4 parameters - guess the extra parameter might be a material or bool
                    result = createWallMethod.Invoke(meshRenderer, new object[] { position, size, rotation, null });
                }
                else
                {
                    Debug.LogError($"Unexpected parameter count for CreateWallMesh: {parameters.Length}");
                    return -1;
                }
                
                // Convert result to int if possible
                if (result is int intResult)
                {
                    return intResult;
                }
                
                // If we got here, assume the wall was created successfully but we don't know the index
                // Just return the current count of walls
                return existingWallBounds.Count;
            }
            
            // Fallback to other possible method names
            var addWallMethod = meshRenderer.GetType().GetMethod("AddWall");
            if (addWallMethod != null)
            {
                var result = addWallMethod.Invoke(meshRenderer, new object[] { position, size, rotation });
                
                if (result is int intResult)
                {
                    return intResult;
                }
                
                return existingWallBounds.Count;
            }
            
            // Last resort: create a new GameObject as a wall
            GameObject newWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            newWall.transform.position = position;
            newWall.transform.rotation = rotation;
            newWall.transform.localScale = size;
            newWall.transform.SetParent(meshRenderer.transform);
            newWall.name = $"Wall_{existingWallBounds.Count}";
            
            return existingWallBounds.Count;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error creating wall mesh: {e.Message}");
            return -1;
        }
    }
    
    /// <summary>
    /// Removes a wall mesh
    /// </summary>
    private void RemoveWallMesh(int wallIndex)
    {
        if (meshRenderer == null || wallIndex < 0 || wallIndex >= existingWallBounds.Count)
            return;
            
        try
        {
            // Try to get the method via reflection
            var removeWallMethod = meshRenderer.GetType().GetMethod("RemoveWallMesh");
            if (removeWallMethod != null)
            {
                removeWallMethod.Invoke(meshRenderer, new object[] { wallIndex });
                return;
            }
            
            // Fallback to other possible method names
            var removeWallAtMethod = meshRenderer.GetType().GetMethod("RemoveWallAt");
            if (removeWallAtMethod != null)
            {
                removeWallAtMethod.Invoke(meshRenderer, new object[] { wallIndex });
                return;
            }
            
            // If no removal method exists, try to just disable the wall
            var getWallMethod = meshRenderer.GetType().GetMethod("GetWall");
            if (getWallMethod != null)
            {
                var wall = getWallMethod.Invoke(meshRenderer, new object[] { wallIndex });
                if (wall != null)
                {
                    var setActiveMethod = wall.GetType().GetMethod("SetActive");
                    if (setActiveMethod != null)
                    {
                        setActiveMethod.Invoke(wall, new object[] { false });
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error removing wall mesh: {e.Message}");
        }
    }
} 