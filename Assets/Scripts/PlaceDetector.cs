using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using System.IO;
using System.Collections;

/// <summary>
/// Detects horizontal surfaces in a classroom and places AR objects at specific locations
/// </summary>
public class PlaceDetector : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARRaycastManager raycastManager;
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARSessionHelper sessionHelper;
    
    [Header("Detection Settings")]
    [SerializeField] private float minPlaneSize = 0.5f; // Minimum size of plane to be considered (in square meters)
    // Commented out unused field
    // [SerializeField] private float detectionInterval = 0.5f; // How often to check for new places
    
    [Header("Placement Settings")]
    [SerializeField] private GameObject placementIndicator; // Visual indicator for placement
    [SerializeField] private GameObject[] arObjectPrefabs; // Different objects to place
    // Commented out unused field
    // [SerializeField] private float placementDistance = 5f; // Maximum distance for placement
    
    // Runtime variables
    private List<ARPlane> detectedSurfaces = new List<ARPlane>();
    private List<PlaceData> savedPlaces = new List<PlaceData>();
    private List<GameObject> placedObjects = new List<GameObject>();
    private int currentPrefabIndex = 0;
    private bool isScanning = false;
    private bool isPlacementMode = false;
    private Camera arCamera;
    
    // Data structure for saved places
    [System.Serializable]
    private class PlaceData
    {
        public Vector3 position;
        public Quaternion rotation;
        public int prefabIndex;
        public string label;
        
        public PlaceData(Vector3 pos, Quaternion rot, int index, string name)
        {
            position = pos;
            rotation = rot;
            prefabIndex = index;
            label = name;
        }
    }
    
    // Data structure for all saved places
    [System.Serializable]
    private class PlaceDataCollection
    {
        public List<PlaceData> places = new List<PlaceData>();
    }
    
    private void Awake()
    {
        // Auto-find components if not set
        if (raycastManager == null)
            raycastManager = FindFirstObjectByType<ARRaycastManager>();
            
        if (planeManager == null)
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            
        if (sessionHelper == null)
            sessionHelper = FindFirstObjectByType<ARSessionHelper>();
            
        arCamera = Camera.main;
        
        // Initialize placement indicator
        if (placementIndicator != null)
            placementIndicator.SetActive(false);
    }
    
    private void Start()
    {
        // Subscribe to plane events
        if (planeManager != null)
        {
            #pragma warning disable CS0618 // Disable obsolete warning
            planeManager.planesChanged += OnPlanesChanged;
            #pragma warning restore CS0618
        }
        
        // Load previously saved places if available
        LoadPlaces();
    }
    
    private void OnDestroy()
    {
        // Unsubscribe from events
        if (planeManager != null)
        {
            #pragma warning disable CS0618 // Disable obsolete warning
            planeManager.planesChanged -= OnPlanesChanged;
            #pragma warning restore CS0618
        }
    }
    
    private void Update()
    {
        // Only process when AR session is ready
        if (sessionHelper == null || !sessionHelper.IsSessionReady())
            return;
            
        if (isPlacementMode)
        {
            UpdatePlacementIndicator();
        }
    }
    
    /// <summary>
    /// Handle plane detection events
    /// </summary>
    #pragma warning disable CS0618 // Disable obsolete warning
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    #pragma warning restore CS0618
    {
        if (!isScanning) return;
        
        // Process added planes
        foreach (ARPlane plane in args.added)
        {
            if (IsValidSurface(plane) && !detectedSurfaces.Contains(plane))
            {
                detectedSurfaces.Add(plane);
                Debug.Log($"Surface detected: {plane.trackableId} - Area: {plane.size.x * plane.size.y} m²");
            }
        }
        
        // Process updated planes
        foreach (ARPlane plane in args.updated)
        {
            if (IsValidSurface(plane) && !detectedSurfaces.Contains(plane))
            {
                detectedSurfaces.Add(plane);
            }
        }
        
        // Process removed planes
        foreach (ARPlane plane in args.removed)
        {
            detectedSurfaces.Remove(plane);
        }
    }
    
    /// <summary>
    /// Check if a surface meets our criteria
    /// </summary>
    private bool IsValidSurface(ARPlane plane)
    {
        // Only horizontal surfaces (floor, tables, desks)
        bool isHorizontal = plane.alignment == PlaneAlignment.HorizontalUp || 
                             plane.alignment == PlaneAlignment.HorizontalDown;
                          
        // Check minimum size
        float area = plane.size.x * plane.size.y;
        bool isLargeEnough = area >= minPlaneSize;
        
        return isHorizontal && isLargeEnough;
    }
    
    /// <summary>
    /// Start scanning for surfaces
    /// </summary>
    public void StartScanning()
    {
        if (sessionHelper == null || !sessionHelper.IsSessionReady())
        {
            Debug.LogWarning("AR Session not ready. Cannot start scanning.");
            return;
        }
        
        if (planeManager != null)
        {
            planeManager.enabled = true;
            isScanning = true;
            Debug.Log("Surface scanning started");
        }
    }
    
    /// <summary>
    /// Stop scanning for surfaces
    /// </summary>
    public void StopScanning()
    {
        isScanning = false;
        Debug.Log($"Surface scanning stopped. Found {detectedSurfaces.Count} valid surfaces.");
    }
    
    /// <summary>
    /// Toggle placement mode
    /// </summary>
    public void TogglePlacementMode(bool enable)
    {
        isPlacementMode = enable;
        
        if (placementIndicator != null)
        {
            placementIndicator.SetActive(enable);
        }
    }
    
    /// <summary>
    /// Update indicator position for object placement
    /// </summary>
    private void UpdatePlacementIndicator()
    {
        if (placementIndicator == null || raycastManager == null)
            return;
            
        Vector2 screenCenter = new Vector2(Screen.width / 2, Screen.height / 2);
        List<ARRaycastHit> hits = new List<ARRaycastHit>();
        
        if (raycastManager.Raycast(screenCenter, hits, TrackableType.PlaneWithinPolygon))
        {
            placementIndicator.SetActive(true);
            placementIndicator.transform.position = hits[0].pose.position;
            placementIndicator.transform.rotation = hits[0].pose.rotation;
        }
        else
        {
            placementIndicator.SetActive(false);
        }
    }
    
    /// <summary>
    /// Place AR object at current indicator position
    /// </summary>
    public void PlaceObject(string label = "")
    {
        if (!isPlacementMode || placementIndicator == null || !placementIndicator.activeSelf)
            return;
            
        if (arObjectPrefabs == null || arObjectPrefabs.Length == 0 || currentPrefabIndex >= arObjectPrefabs.Length)
        {
            Debug.LogError("No valid prefabs for placement");
            return;
        }
        
        // Create AR object at indicator position
        GameObject prefab = arObjectPrefabs[currentPrefabIndex];
        GameObject newObject = Instantiate(prefab, 
                                        placementIndicator.transform.position, 
                                        placementIndicator.transform.rotation);
        
        // Save the placed object
        placedObjects.Add(newObject);
        
        // Save place data
        PlaceData newPlace = new PlaceData(
            newObject.transform.position,
            newObject.transform.rotation,
            currentPrefabIndex,
            string.IsNullOrEmpty(label) ? $"Place_{System.DateTime.Now.Ticks}" : label
        );
        
        savedPlaces.Add(newPlace);
        
        Debug.Log($"Placed object: {newPlace.label} at {newPlace.position}");
        
        // Save to disk automatically
        SavePlaces();
    }
    
    /// <summary>
    /// Change the current prefab for placement
    /// </summary>
    public void CyclePrefab()
    {
        if (arObjectPrefabs == null || arObjectPrefabs.Length == 0)
            return;
            
        currentPrefabIndex = (currentPrefabIndex + 1) % arObjectPrefabs.Length;
        Debug.Log($"Selected prefab: {currentPrefabIndex}");
    }
    
    /// <summary>
    /// Save all placed objects to persistent storage
    /// </summary>
    public void SavePlaces()
    {
        PlaceDataCollection collection = new PlaceDataCollection();
        collection.places = savedPlaces;
        
        string json = JsonUtility.ToJson(collection, true);
        string path = Path.Combine(Application.persistentDataPath, "saved_places.json");
        
        File.WriteAllText(path, json);
        Debug.Log($"Saved {savedPlaces.Count} places to: {path}");
    }
    
    /// <summary>
    /// Load previously saved places
    /// </summary>
    public void LoadPlaces()
    {
        string path = Path.Combine(Application.persistentDataPath, "saved_places.json");
        
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            PlaceDataCollection collection = JsonUtility.FromJson<PlaceDataCollection>(json);
            
            if (collection != null && collection.places != null)
            {
                savedPlaces = collection.places;
                Debug.Log($"Loaded {savedPlaces.Count} places from storage");
                
                // Recreate objects if we're in a ready state
                if (sessionHelper != null && sessionHelper.IsSessionReady())
                {
                    StartCoroutine(RecreateLoadedObjects());
                }
            }
        }
    }
    
    /// <summary>
    /// Recreate loaded objects with a delay to ensure tracking is stable
    /// </summary>
    private IEnumerator RecreateLoadedObjects()
    {
        // Wait to make sure tracking is stable
        yield return new WaitForSeconds(3.0f);
        
        foreach (PlaceData place in savedPlaces)
        {
            if (arObjectPrefabs != null && place.prefabIndex < arObjectPrefabs.Length)
            {
                GameObject prefab = arObjectPrefabs[place.prefabIndex];
                GameObject newObject = Instantiate(prefab, place.position, place.rotation);
                placedObjects.Add(newObject);
                
                Debug.Log($"Recreated object: {place.label} at {place.position}");
            }
        }
    }
    
    /// <summary>
    /// Clear all placed objects
    /// </summary>
    public void ClearPlacedObjects()
    {
        foreach (GameObject obj in placedObjects)
        {
            if (obj != null)
            {
                Destroy(obj);
            }
        }
        
        placedObjects.Clear();
        savedPlaces.Clear();
        
        // Clear saved file
        string path = Path.Combine(Application.persistentDataPath, "saved_places.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        
        Debug.Log("Cleared all placed objects");
    }
} 