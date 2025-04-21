using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;

public class ARSetupMenu : MonoBehaviour
{
    [MenuItem("AR Wall Detection/Fix AR Components", false, 10)]
    public static void FixARComponents()
    {
        // Check if the scene is empty
        if (EditorUtility.DisplayDialog("Fix AR Components", 
            "This action will add or fix AR components for wall detection in the current scene. Continue?", 
            "Yes", "Cancel"))
        {
            // Find AR Session Origin
            ARSessionOrigin arSessionOrigin = Object.FindObjectOfType<ARSessionOrigin>();
            if (arSessionOrigin == null)
            {
                EditorUtility.DisplayDialog("Error", 
                    "No ARSessionOrigin found in the scene. Please create an AR scene first.", 
                    "OK");
                return;
            }

            // Add the AR Component Fixer
            GameObject componentFixerObj = new GameObject("AR Component Fixer");
            ARComponentFixer fixer = componentFixerObj.AddComponent<ARComponentFixer>();
            
            // Configure the fixer
            fixer.arSessionOrigin = arSessionOrigin;
            
            // Try to find the wall material
            Material wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMesh.mat");
            if (wallMaterial != null)
            {
                fixer.wallMaterial = wallMaterial;
            }
            else
            {
                Debug.LogWarning("WallMesh material not found at Assets/Materials/WallMesh.mat");
            }
            
            // Run the fixer
            fixer.FixAllComponents();
            
            // Select the fixer object
            Selection.activeGameObject = componentFixerObj;
            
            EditorUtility.DisplayDialog("Success", 
                "AR Component Fixer has been added to the scene and components have been configured for wall detection.", 
                "OK");
        }
    }
    
    [MenuItem("AR Wall Detection/Fix AR Components", true)]
    public static bool ValidateFixARComponents()
    {
        // Check if there's an AR session origin in the scene
        return Object.FindObjectOfType<ARSessionOrigin>() != null;
    }
    
    [MenuItem("AR Wall Detection/Create Material for Walls", false, 20)]
    public static void CreateWallMaterial()
    {
        // Check if the material already exists
        Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallMesh.mat");
        if (existingMaterial != null)
        {
            if (!EditorUtility.DisplayDialog("Material Already Exists", 
                "A WallMesh material already exists. Do you want to replace it?", 
                "Yes", "Cancel"))
            {
                return;
            }
        }
        
        // Create Materials folder if it doesn't exist
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        
        // Create a new material
        Material wallMaterial = new Material(Shader.Find("Standard"));
        wallMaterial.name = "WallMesh";
        
        // Make it semi-transparent blue
        Color wallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
        wallMaterial.color = wallColor;
        wallMaterial.SetFloat("_Mode", 3); // Transparent mode
        wallMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        wallMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        wallMaterial.SetInt("_ZWrite", 0);
        wallMaterial.DisableKeyword("_ALPHATEST_ON");
        wallMaterial.EnableKeyword("_ALPHABLEND_ON");
        wallMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        wallMaterial.renderQueue = 3000;
        
        // Save the material as an asset
        AssetDatabase.CreateAsset(wallMaterial, "Assets/Materials/WallMesh.mat");
        AssetDatabase.SaveAssets();
        
        EditorUtility.DisplayDialog("Success", 
            "WallMesh material has been created at Assets/Materials/WallMesh.mat", 
            "OK");
        
        // Select the created material in the Project window
        Selection.activeObject = wallMaterial;
    }
} 