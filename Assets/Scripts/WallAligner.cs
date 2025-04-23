using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

[RequireComponent(typeof(ARMeshManager))]
public class WallAligner : MonoBehaviour
{
    public Material wallMaterial;
    public bool combineMeshes = false;
    public float combineThreshold = 0.5f;
    public float verticalThreshold = 0.7f; // Cosine of the angle (0.7 ~= 45 degrees)
    
    // Minimum wall dimensions
    [Header("Wall Size Settings")]
    [SerializeField] private float _minimumWallWidth = 0.3f;
    [SerializeField] private float _minimumWallHeight = 0.5f;

    private ARMeshManager meshManager;
    private HashSet<MeshFilter> _processedMeshes = new HashSet<MeshFilter>();
    private HashSet<MeshFilter> _verticalMeshes = new HashSet<MeshFilter>();
    private float _lastUpdateTime = 0f;
    private float _updateInterval = 0.5f; // Update every half second instead of every frame

    private void Awake()
    {
        meshManager = GetComponent<ARMeshManager>();
    }

    private void OnEnable()
    {
        if (meshManager != null)
        {
            meshManager.meshesChanged += OnMeshesChanged;
            Debug.Log("WallAligner: Subscribed to ARMeshManager meshesChanged event");
        }
        else
        {
            Debug.LogError("WallAligner: ARMeshManager component not found");
        }
    }

    private void OnDisable()
    {
        if (meshManager != null)
        {
            meshManager.meshesChanged -= OnMeshesChanged;
            Debug.Log("WallAligner: Unsubscribed from ARMeshManager meshesChanged event");
        }
    }

    private void OnMeshesChanged(ARMeshesChangedEventArgs args)
    {
        // Process added meshes
        if (args.added != null && args.added.Count > 0)
        {
            Debug.Log($"WallAligner: Processing {args.added.Count} new meshes");
            foreach (var meshFilter in args.added)
            {
                ProcessMesh(meshFilter);
            }
        }

        // Process updated meshes
        if (args.updated != null && args.updated.Count > 0)
        {
            Debug.Log($"WallAligner: Processing {args.updated.Count} updated meshes");
            foreach (var meshFilter in args.updated)
            {
                // For updated meshes, remove from tracking and reprocess
                if (_processedMeshes.Contains(meshFilter))
                {
                    // If it was previously identified as a vertical mesh, remove it
                    if (_verticalMeshes.Contains(meshFilter))
                    {
                        _verticalMeshes.Remove(meshFilter);
                    }
                    
                    // Remove from tracking to force reprocessing
                    _processedMeshes.Remove(meshFilter);
                }
                
                // Process the updated mesh
                ProcessMesh(meshFilter);
            }
        }

        // Handle removed meshes
        if (args.removed != null && args.removed.Count > 0)
        {
            Debug.Log($"WallAligner: Cleaning up {args.removed.Count} removed meshes");
            foreach (var meshFilter in args.removed)
            {
                if (_processedMeshes.Contains(meshFilter))
                {
                    bool wasVertical = _verticalMeshes.Contains(meshFilter);
                    _processedMeshes.Remove(meshFilter);
                    
                    if (wasVertical)
                    {
                        _verticalMeshes.Remove(meshFilter);
                        Debug.Log($"WallAligner: Removed tracking for vertical mesh {meshFilter.gameObject.name}");
                    }
                }
            }
        }

        // Combine vertical meshes if option enabled and we have enough meshes
        if (combineMeshes && _verticalMeshes.Count > 1)
        {
            Debug.Log($"WallAligner: Combining {_verticalMeshes.Count} vertical meshes");
            CombineNearbyWalls();
        }
    }

    private void ProcessMesh(MeshFilter meshFilter)
    {
        if (meshFilter == null || meshFilter.sharedMesh == null) return;
        
        // Skip if already processed
        if (_processedMeshes.Contains(meshFilter)) return;
        _processedMeshes.Add(meshFilter);
        
        // Calculate the average normal in world space
        Mesh mesh = meshFilter.sharedMesh;
        Vector3 localAverageNormal = CalculateAverageNormal(mesh);
        
        // Convert local normal to world space
        Vector3 worldAverageNormal = meshFilter.transform.TransformDirection(localAverageNormal);
        
        // Check if the mesh is a vertical surface
        if (IsVerticalSurface(worldAverageNormal))
        {
            // Check mesh size before further processing
            if (IsMeshLargeEnough(meshFilter, worldAverageNormal))
            {
                // Apply wall material
                ApplyWallMaterial(meshFilter);
                
                // Try to combine with nearby walls
                CombineWithNearbyWalls(meshFilter, worldAverageNormal);
            }
        }
    }

    /// <summary>
    /// Calculate the average normal of the given mesh.
    /// </summary>
    private Vector3 CalculateAverageNormal(Mesh mesh)
    {
        if (mesh == null) return Vector3.zero;

        Vector3[] normals = mesh.normals;
        Vector3 averageNormal = Vector3.zero;

        // Optimization: If there are too many vertices, sample them instead of processing all
        int maxSamples = 100; // Maximum number of normals to sample
        
        if (normals.Length <= maxSamples)
        {
            // Process all normals
            for (int i = 0; i < normals.Length; i++)
                averageNormal += normals[i];
                
            if (normals.Length > 0)
                averageNormal /= normals.Length;
        }
        else
        {
            // Sample normals
            int sampleStep = normals.Length / maxSamples;
            int sampleCount = 0;
            
            for (int i = 0; i < normals.Length; i += sampleStep)
            {
                averageNormal += normals[i];
                sampleCount++;
            }
            
            if (sampleCount > 0)
                averageNormal /= sampleCount;
        }

        return averageNormal;
    }

    /// <summary>
    /// Determine if the given normal indicates a vertical surface.
    /// </summary>
    private bool IsVerticalSurface(Vector3 normal)
    {
        // Fast check: If normal is very close to horizontal, return false immediately
        if (Mathf.Abs(normal.y) > 0.9f)
            return false;
            
        // Calculate the angle between the normal and the up vector
        float angleWithUp = Vector3.Angle(normal, Vector3.up);
        
        // A vertical surface has an angle with up vector close to 90 degrees
        // Use a wider threshold to catch more potential walls
        bool isVertical = angleWithUp > 70.0f && angleWithUp < 110.0f;
        
        // Additional check for minimum size, if normal is confirmed to be vertical
        return isVertical;
    }

    /// <summary>
    /// Check if the mesh is large enough to be considered a wall
    /// </summary>
    private bool IsMeshLargeEnough(MeshFilter meshFilter, Vector3 normal)
    {
        // Quick size check using the mesh bounds
        Bounds bounds = meshFilter.sharedMesh.bounds;
        Vector3 size = Vector3.Scale(bounds.size, meshFilter.transform.lossyScale);
        
        // For a wall, we want minimum width and height
        // Project the size onto a plane perpendicular to the normal
        Vector3 upVector = Vector3.up;
        
        // Make sure up vector is not parallel to normal
        if (Vector3.Dot(normal.normalized, upVector) > 0.9f)
            upVector = Vector3.forward;
            
        // Get the "right" vector for the wall
        Vector3 rightVector = Vector3.Cross(upVector, normal).normalized;
        
        // And get the real up vector for the wall
        Vector3 wallUpVector = Vector3.Cross(normal, rightVector).normalized;
        
        // Project the size onto these axes
        float width = Mathf.Abs(Vector3.Dot(size, rightVector));
        float height = Mathf.Abs(Vector3.Dot(size, wallUpVector));
        
        // Minimum dimensions for a wall (could be adjusted based on your needs)
        bool isLargeEnough = (width >= _minimumWallWidth && height >= _minimumWallHeight);
        
        return isLargeEnough;
    }

    /// <summary>
    /// Apply the wall material to the given mesh filter.
    /// </summary>
    private void ApplyWallMaterial(MeshFilter meshFilter)
    {
        // Make sure the mesh has a renderer and material is assigned
        MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
        if (renderer == null || wallMaterial == null) return;

        // Apply the wall material without modifying the transform
        renderer.material = wallMaterial;
        
        // Ensure the mesh is marked as a wall for other systems
        try 
        {
            if (!meshFilter.gameObject.CompareTag("Wall"))
            {
                meshFilter.gameObject.tag = "Wall";
            }
        }
        catch (System.Exception e)
        {
            // In case the Wall tag doesn't exist in the project
            Debug.LogWarning($"WallAligner: Unable to tag object as Wall: {e.Message}");
            
            // Add a component to mark this as a wall if tagging fails
            if (meshFilter.gameObject.GetComponent<WallIdentifier>() == null)
            {
                meshFilter.gameObject.AddComponent<WallIdentifier>();
            }
        }
        
        Debug.Log($"WallAligner: Applied material to wall mesh {meshFilter.gameObject.name} at position {meshFilter.transform.position}");
    }

    private void CombineNearbyWalls()
    {
        // Group nearby wall meshes
        List<List<MeshFilter>> wallGroups = new List<List<MeshFilter>>();

        foreach (MeshFilter meshFilter in _verticalMeshes)
        {
            bool addedToGroup = false;
            foreach (List<MeshFilter> group in wallGroups)
            {
                if (group.Count > 0)
                {
                    float distance = Vector3.Distance(
                        meshFilter.transform.position,
                        group[0].transform.position);

                    if (distance < combineThreshold)
                    {
                        group.Add(meshFilter);
                        addedToGroup = true;
                        break;
                    }
                }
            }

            if (!addedToGroup)
            {
                List<MeshFilter> newGroup = new List<MeshFilter> { meshFilter };
                wallGroups.Add(newGroup);
            }
        }

        // Combine meshes in each group
        for (int i = 0; i < wallGroups.Count; i++)
        {
            if (wallGroups[i].Count > 1)
            {
                CombineMeshGroup(wallGroups[i], i);
            }
        }
    }

    private void CombineMeshGroup(List<MeshFilter> meshFilters, int groupIndex)
    {
        if (meshFilters.Count <= 0)
            return;

        CombineInstance[] combine = new CombineInstance[meshFilters.Count];

        // Get the center position of all meshes in this group to position the combined mesh
        Vector3 centerPosition = Vector3.zero;
        for (int i = 0; i < meshFilters.Count; i++)
        {
            centerPosition += meshFilters[i].transform.position;
        }
        centerPosition /= meshFilters.Count;

        for (int i = 0; i < meshFilters.Count; i++)
        {
            combine[i].mesh = meshFilters[i].sharedMesh;
            combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
        }

        // Create a new game object at the calculated center position
        GameObject combinedObject = new GameObject($"CombinedWall_{groupIndex}");
        // Don't use SetParent to avoid potential scale/position issues, place directly in scene root
        combinedObject.transform.position = centerPosition;
        combinedObject.transform.rotation = Quaternion.identity;

        MeshFilter combinedMeshFilter = combinedObject.AddComponent<MeshFilter>();
        Mesh combinedMesh = new Mesh();
        combinedMesh.CombineMeshes(combine, true, true);
        combinedMeshFilter.sharedMesh = combinedMesh;

        MeshRenderer combinedRenderer = combinedObject.AddComponent<MeshRenderer>();
        combinedRenderer.material = wallMaterial;

        // Instead of hiding, we should just disable the renderer to maintain the original transform
        foreach (MeshFilter meshFilter in meshFilters)
        {
            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }

    void Update()
    {
        // Only process meshes at specified intervals
        if (Time.time - _lastUpdateTime < _updateInterval)
            return;
            
        _lastUpdateTime = Time.time;
        
        // Find all mesh filters in the scene that are not part of vertical meshes yet
        MeshFilter[] meshFilters = FindObjectsOfType<MeshFilter>();
        
        int processedThisFrame = 0;
        int maxProcessPerFrame = 5; // Limit processing to avoid performance spikes
        
        foreach (MeshFilter meshFilter in meshFilters)
        {
            // Skip already processed meshes
            if (_processedMeshes.Contains(meshFilter))
                continue;
                
            // Process the mesh
            ProcessMesh(meshFilter);
            
            // Count processed meshes and exit if limit reached
            processedThisFrame++;
            if (processedThisFrame >= maxProcessPerFrame)
                break;
        }
    }
    
    /// <summary>
    /// Combine the given mesh with nearby vertical walls.
    /// </summary>
    private void CombineWithNearbyWalls(MeshFilter meshFilter, Vector3 normal)
    {
        if (!combineMeshes || meshFilter == null)
            return;
            
        // Get the center position of this mesh
        Vector3 thisPosition = meshFilter.transform.position;
        Bounds thisBounds = meshFilter.sharedMesh.bounds;
        
        // Scale bounds to world space
        Vector3 worldSize = Vector3.Scale(thisBounds.size, meshFilter.transform.lossyScale);
        float thisRadius = worldSize.magnitude * 0.5f;
        
        List<MeshFilter> nearbyWalls = new List<MeshFilter>();
        
        // Find nearby vertical walls
        foreach (MeshFilter verticalMesh in _verticalMeshes)
        {
            // Skip self and null meshes
            if (verticalMesh == meshFilter || verticalMesh == null || verticalMesh.sharedMesh == null)
                continue;
                
            // Check distance between centers
            Vector3 otherPosition = verticalMesh.transform.position;
            float distance = Vector3.Distance(thisPosition, otherPosition);
            
            // Get the other mesh bounds
            Bounds otherBounds = verticalMesh.sharedMesh.bounds;
            Vector3 otherWorldSize = Vector3.Scale(otherBounds.size, verticalMesh.transform.lossyScale);
            float otherRadius = otherWorldSize.magnitude * 0.5f;
            
            // Check if meshes are close enough
            if (distance < combineThreshold + thisRadius + otherRadius)
            {
                // Check if normals are roughly aligned
                Vector3 otherNormal = CalculateAverageNormal(verticalMesh.sharedMesh);
                float normalAlignment = Vector3.Dot(normal.normalized, otherNormal.normalized);
                
                // Consider walls with similar orientation
                if (normalAlignment > 0.7f) // cos(45°) ≈ 0.7
                {
                    nearbyWalls.Add(verticalMesh);
                }
            }
        }
        
        // If nearby walls found, combine them
        if (nearbyWalls.Count > 0)
        {
            nearbyWalls.Add(meshFilter); // Add current mesh to the list
            CombineMeshes(nearbyWalls);
        }
        else
        {
            // Add to vertical meshes for future combining
            _verticalMeshes.Add(meshFilter);
        }
    }
    
    /// <summary>
    /// Combine multiple mesh filters into a single mesh.
    /// </summary>
    private void CombineMeshes(List<MeshFilter> meshesToCombine)
    {
        if (meshesToCombine.Count <= 1)
            return;
            
        // Create combine instances for each mesh
        CombineInstance[] combineInstances = new CombineInstance[meshesToCombine.Count];
        
        for (int i = 0; i < meshesToCombine.Count; i++)
        {
            MeshFilter meshFilter = meshesToCombine[i];
            
            // Set up combine instance
            combineInstances[i] = new CombineInstance
            {
                mesh = meshFilter.sharedMesh,
                transform = meshFilter.transform.localToWorldMatrix
            };
        }
        
        // Create a new game object for the combined mesh
        GameObject combinedObject = new GameObject("CombinedWall");
        combinedObject.transform.position = Vector3.zero;
        combinedObject.transform.rotation = Quaternion.identity;
        
        // Add components
        MeshFilter combinedMeshFilter = combinedObject.AddComponent<MeshFilter>();
        MeshRenderer combinedRenderer = combinedObject.AddComponent<MeshRenderer>();
        
        // Create combined mesh
        Mesh combinedMesh = new Mesh();
        combinedMesh.CombineMeshes(combineInstances, true, true);
        combinedMeshFilter.sharedMesh = combinedMesh;
        
        // Apply wall material
        combinedRenderer.material = wallMaterial;
        
        // Add the combined mesh to processed and vertical lists
        foreach (MeshFilter meshFilter in meshesToCombine)
        {
            _processedMeshes.Add(meshFilter);
            _verticalMeshes.Add(meshFilter);
        }
        
        // Disable original meshes
        foreach (MeshFilter meshFilter in meshesToCombine)
        {
            if (meshFilter && meshFilter.gameObject)
                meshFilter.gameObject.SetActive(false);
        }
    }
} 