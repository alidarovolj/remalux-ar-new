using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using System.IO;

/// <summary>
/// Editor menu items for fixing AR visualization issues
/// </summary>
public class ARFixMenu : MonoBehaviour
{
    [MenuItem("AR Wall Detection/Fix Plane Visualization")]
    public static void FixPlaneVisualization()
    {
        // Find ARPlaneManager
        ARPlaneManager planeManager = Object.FindFirstObjectByType<ARPlaneManager>();
        if (planeManager == null)
        {
            Debug.LogError("No ARPlaneManager found in scene!");
            EditorUtility.DisplayDialog("Error", "No ARPlaneManager found in scene!", "OK");
            return;
        }
        
        // Create a custom plane prefab with bright visible material
        GameObject planePrefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
        planePrefab.name = "AR Plane Visual";
        
        // Create bright material for planes
        Material planeMaterial = new Material(Shader.Find("Unlit/Color"));
        if (planeMaterial.shader == null)
        {
            planeMaterial.shader = Shader.Find("Standard");
        }
        planeMaterial.color = new Color(1.0f, 0.0f, 1.0f, 1.0f); // Bright magenta
        
        // Apply material to plane prefab
        MeshRenderer prefabRenderer = planePrefab.GetComponent<MeshRenderer>();
        prefabRenderer.material = planeMaterial;
        prefabRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        prefabRenderer.receiveShadows = false;
        
        // Add to asset database to use as prefab
        string prefabPath = "Assets/Prefabs/ARPlaneVisual.prefab";
        if (!Directory.Exists("Assets/Prefabs"))
        {
            Directory.CreateDirectory("Assets/Prefabs");
        }
        
        try
        {
            // Make sure parent directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            
            // Save it as prefab asset
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(planePrefab, prefabPath);
            Object.DestroyImmediate(planePrefab); // Clean up the temporary object
            
            // Assign to plane manager
            planeManager.planePrefab = savedPrefab;
            
            // Mark plane manager as dirty to save changes
            EditorUtility.SetDirty(planeManager);
            
            Debug.Log("Successfully updated plane prefab with bright material");
            EditorUtility.DisplayDialog("Success", "Plane visualization fixed with bright magenta material!", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create plane prefab asset: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to create plane prefab asset: {e.Message}", "OK");
        }
    }
    
    [MenuItem("AR Wall Detection/Force Update All Materials")]
    public static void ForceUpdateAllMaterials()
    {
        // Set materials to bright magenta
        Color brightColor = new Color(1.0f, 0.0f, 1.0f, 1.0f);
        
        // Update WallMeshRenderer materials
        WallMeshRenderer[] wallRenderers = Object.FindObjectsByType<WallMeshRenderer>(FindObjectsSortMode.None);
        foreach (WallMeshRenderer renderer in wallRenderers)
        {
            if (renderer.WallMaterial != null)
            {
                renderer.WallMaterial.color = brightColor;
                EditorUtility.SetDirty(renderer.WallMaterial);
            }
            
            // Set show all meshes and debug visualizer
            renderer.ShowAllMeshes = true;
            renderer.ShowDebugInfo = true;
            
            EditorUtility.SetDirty(renderer);
            
            // Force toggle to refresh
            renderer.ToggleShowAllMeshes();
            renderer.ToggleDebugVisualizer();
            renderer.ToggleDebugVisualizer(); // Toggle twice to ensure it's on
            
            // Force update meshes
            renderer.ForceUpdateMeshes();
        }
        
        // Update ARPlaneHelper materials
        ARPlaneHelper[] planeHelpers = Object.FindObjectsByType<ARPlaneHelper>(FindObjectsSortMode.None);
        foreach (ARPlaneHelper helper in planeHelpers)
        {
            // Use reflection to set private fields
            System.Reflection.FieldInfo colorField = typeof(ARPlaneHelper).GetField("wallPlaneColor", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
            if (colorField != null)
            {
                colorField.SetValue(helper, brightColor);
            }
            
            EditorUtility.SetDirty(helper);
            
            // Force update planes if possible
            System.Reflection.MethodInfo updateMethod = typeof(ARPlaneHelper).GetMethod("ForceUpdatePlanes", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
            if (updateMethod != null)
            {
                updateMethod.Invoke(helper, null);
            }
        }
        
        // Update WallMaterial.mat and WallMesh.mat assets
        Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMaterial.mat");
        if (wallMaterial != null)
        {
            wallMaterial.color = brightColor;
            EditorUtility.SetDirty(wallMaterial);
        }
        
        Material wallMeshMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMesh.mat");
        if (wallMeshMaterial != null)
        {
            wallMeshMaterial.color = brightColor;
            EditorUtility.SetDirty(wallMeshMaterial);
        }
        
        // Save all assets
        AssetDatabase.SaveAssets();
        
        Debug.Log("Successfully updated all AR materials to bright magenta");
        EditorUtility.DisplayDialog("Success", "All AR materials updated to bright magenta!", "OK");
    }
    
    [MenuItem("AR Wall Detection/Fix AR Camera Culling")]
    public static void FixARCameraCulling()
    {
        // Find all cameras
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        
        int cameraFixed = 0;
        foreach (Camera camera in cameras)
        {
            if (camera.gameObject.name.Contains("AR") || camera.tag == "MainCamera")
            {
                camera.cullingMask = -1; // Include all layers
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0, 0, 0, 0);
                
                // Set appropriate near and far clipping planes
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30f;
                
                EditorUtility.SetDirty(camera);
                cameraFixed++;
            }
        }
        
        if (cameraFixed > 0)
        {
            Debug.Log($"Successfully updated {cameraFixed} camera settings");
            EditorUtility.DisplayDialog("Success", $"Fixed culling settings on {cameraFixed} cameras!", "OK");
        }
        else
        {
            Debug.LogWarning("No AR cameras found to fix");
            EditorUtility.DisplayDialog("Warning", "No AR cameras found to fix", "OK");
        }
    }
} 