using UnityEngine;
using UnityEditor;
using System.IO;

public class CreateARPlaneGridTexture : EditorWindow
{
    private int textureSize = 512;
    private Color backgroundColor = new Color(1f, 1f, 1f, 0.0f);
    private Color gridColor = new Color(1f, 1f, 1f, 1.0f);
    private int gridSpacing = 10;
    private int gridLineWidth = 2;
    
    [MenuItem("AR/Tools/Create AR Plane Grid Texture")]
    public static void ShowWindow()
    {
        GetWindow<CreateARPlaneGridTexture>("AR Plane Grid Texture Creator");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("AR Plane Grid Texture Creator", EditorStyles.boldLabel);
        
        textureSize = EditorGUILayout.IntSlider("Texture Size", textureSize, 128, 1024);
        
        backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);
        gridColor = EditorGUILayout.ColorField("Grid Color", gridColor);
        
        gridSpacing = EditorGUILayout.IntSlider("Grid Spacing", gridSpacing, 5, 50);
        gridLineWidth = EditorGUILayout.IntSlider("Grid Line Width", gridLineWidth, 1, 5);
        
        if (GUILayout.Button("Generate Grid Texture"))
        {
            GenerateGridTexture();
        }
    }
    
    private void GenerateGridTexture()
    {
        // Create a new texture
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        
        // Fill texture with background color
        Color[] pixels = new Color[textureSize * textureSize];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
        }
        
        // Draw the grid
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                bool isGridLine = false;
                
                // Check if this pixel is on a grid line
                for (int i = 0; i < gridLineWidth; i++)
                {
                    if ((x + i) % gridSpacing == 0 || (y + i) % gridSpacing == 0)
                    {
                        isGridLine = true;
                        break;
                    }
                }
                
                if (isGridLine)
                {
                    pixels[y * textureSize + x] = gridColor;
                }
            }
        }
        
        // Apply pixels to texture
        texture.SetPixels(pixels);
        texture.Apply();
        
        // Save texture to file
        string path = "Assets/Resources";
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        
        string fullPath = Path.Combine(path, "ARPlaneGrid.png");
        byte[] pngData = texture.EncodeToPNG();
        File.WriteAllBytes(fullPath, pngData);
        
        AssetDatabase.Refresh();
        
        Debug.Log("Grid texture created at " + fullPath);
        
        // Select the texture in the project window
        TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            
            // Apply changes
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            
            // Select and ping the texture in the project window
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(fullPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
} 