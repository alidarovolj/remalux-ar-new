using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ScriptableObject defining a palette of paint colors and finishes.
/// Allows for easy editing in the Unity editor and consistent use throughout the app.
/// </summary>
[CreateAssetMenu(fileName = "New Color Palette", menuName = "Paint/Color Palette")]
public class ColorPalette : ScriptableObject
{
    [System.Serializable]
    public class PaintColor
    {
        public string name;
        public Color color = Color.white;
        public string hexCode; // For reference or integration with external systems
        
        [TextArea(1, 3)]
        public string description; // Optional description or product details
        
        // Optional icon for UI
        public Sprite icon;
        
        // Optional - implement any color-matching functionality
        public void UpdateHexCodeFromColor()
        {
            hexCode = ColorUtility.ToHtmlStringRGB(color);
        }
    }
    
    [System.Serializable]
    public class PaintFinish
    {
        public string name;
        public PaintFinishType type;
        [Range(0, 1)] public float glossiness;
        [Range(0, 1)] public float metallic;
        
        [TextArea(1, 2)]
        public string description;
        
        // Optional icon for UI
        public Sprite icon;
    }
    
    [Header("Palette Information")]
    public string paletteName = "Default Palette";
    public string paletteDescription;
    
    [Header("Colors")]
    public List<PaintColor> paintColors = new List<PaintColor>();
    
    [Header("Finishes")]
    public List<PaintFinish> paintFinishes = new List<PaintFinish>();
    
    private void OnValidate()
    {
        // Auto-update hex codes when colors change in the inspector
        foreach (var color in paintColors)
        {
            color.UpdateHexCodeFromColor();
        }
        
        // Ensure we have default finishes if list is empty
        if (paintFinishes.Count == 0)
        {
            CreateDefaultFinishes();
        }
    }
    
    /// <summary>
    /// Creates a set of default paint finishes
    /// </summary>
    private void CreateDefaultFinishes()
    {
        paintFinishes.Add(new PaintFinish
        {
            name = "Matte",
            type = PaintFinishType.Matte,
            glossiness = 0.05f,
            metallic = 0.0f,
            description = "Flat, non-reflective finish. Hides imperfections."
        });
        
        paintFinishes.Add(new PaintFinish
        {
            name = "Eggshell",
            type = PaintFinishType.Eggshell,
            glossiness = 0.2f,
            metallic = 0.0f,
            description = "Subtle low-sheen finish. More washable than matte."
        });
        
        paintFinishes.Add(new PaintFinish
        {
            name = "Semi-Gloss",
            type = PaintFinishType.SemiGloss,
            glossiness = 0.5f,
            metallic = 0.05f,
            description = "Moderately shiny finish. Easy to clean."
        });
        
        paintFinishes.Add(new PaintFinish
        {
            name = "Gloss",
            type = PaintFinishType.Gloss,
            glossiness = 0.9f,
            metallic = 0.1f,
            description = "High-shine finish. Highly durable and washable."
        });
    }
    
    /// <summary>
    /// Creates and populates a default color palette
    /// </summary>
    [ContextMenu("Generate Default Colors")]
    public void GenerateDefaultColors()
    {
        paintColors.Clear();
        
        // Add common paint colors
        AddPaintColor("Pure White", new Color(1.0f, 1.0f, 1.0f), "Bright clean white");
        AddPaintColor("Off White", new Color(0.98f, 0.98f, 0.94f), "Soft white with subtle warmth");
        AddPaintColor("Light Gray", new Color(0.8f, 0.8f, 0.8f), "Classic light neutral gray");
        AddPaintColor("Sky Blue", new Color(0.53f, 0.81f, 0.98f), "Soft and airy blue");
        AddPaintColor("Navy Blue", new Color(0.0f, 0.12f, 0.48f), "Deep, rich blue");
        AddPaintColor("Sage Green", new Color(0.56f, 0.74f, 0.56f), "Calming natural green");
        AddPaintColor("Mint Green", new Color(0.6f, 0.98f, 0.6f), "Fresh and vibrant green");
        AddPaintColor("Sunshine Yellow", new Color(1.0f, 0.94f, 0.0f), "Bright and cheerful yellow");
        AddPaintColor("Terracotta", new Color(0.89f, 0.45f, 0.35f), "Warm earthy orange-red");
        AddPaintColor("Burgundy", new Color(0.5f, 0.0f, 0.13f), "Rich, deep red with purple undertones");
        AddPaintColor("Lavender", new Color(0.7f, 0.52f, 0.9f), "Soft purple with blue undertones");
        AddPaintColor("Charcoal", new Color(0.21f, 0.27f, 0.31f), "Deep gray with subtle blue undertones");
    }
    
    /// <summary>
    /// Helper method to add a color to the palette
    /// </summary>
    private void AddPaintColor(string name, Color color, string description)
    {
        var paintColor = new PaintColor
        {
            name = name,
            color = color,
            description = description
        };
        
        paintColor.UpdateHexCodeFromColor();
        paintColors.Add(paintColor);
    }
    
    /// <summary>
    /// Get a color by its index in the palette
    /// </summary>
    public Color GetColorByIndex(int index)
    {
        if (index >= 0 && index < paintColors.Count)
            return paintColors[index].color;
            
        return Color.white; // Default fallback
    }
    
    /// <summary>
    /// Get a paint finish by type
    /// </summary>
    public PaintFinish GetFinishByType(PaintFinishType type)
    {
        return paintFinishes.Find(f => f.type == type);
    }
} 