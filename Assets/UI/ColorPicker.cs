using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

[System.Serializable]
public class ColorChangeEvent : UnityEvent<Color> { }

public class ColorPicker : MonoBehaviour
{
    [Header("Color Presets")]
    [SerializeField] private Color[] presetColors = new Color[]
    {
        Color.white,
        Color.grey,
        new Color(0.9f, 0.9f, 0.8f), // Cream
        new Color(0.95f, 0.8f, 0.7f), // Beige
        new Color(0.6f, 0.8f, 0.9f), // Light blue
        new Color(0.8f, 0.6f, 0.9f), // Lavender
        new Color(0.9f, 0.6f, 0.6f), // Light red
        new Color(0.7f, 0.9f, 0.7f)  // Light green
    };
    
    [Header("UI References")]
    [SerializeField] private Transform colorButtonsContainer;
    [SerializeField] private Button colorButtonPrefab;
    [SerializeField] private Image selectedColorIndicator;
    
    [Header("Events")]
    public ColorChangeEvent onColorChanged = new ColorChangeEvent();
    
    private Color currentColor = Color.white;
    
    private void Start()
    {
        InitializeColorButtons();
        SetColor(presetColors[0]);
    }
    
    private void InitializeColorButtons()
    {
        if (colorButtonsContainer == null || colorButtonPrefab == null)
            return;
            
        // Remove existing buttons
        foreach (Transform child in colorButtonsContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Create color buttons
        foreach (Color color in presetColors)
        {
            Button btn = Instantiate(colorButtonPrefab, colorButtonsContainer);
            
            // Set button color
            Image btnImage = btn.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = color;
            }
            
            // Add click handler
            btn.onClick.AddListener(() => SetColor(color));
        }
    }
    
    public void SetColor(Color color)
    {
        currentColor = color;
        
        // Update selection indicator if available
        if (selectedColorIndicator != null)
        {
            selectedColorIndicator.color = color;
        }
        
        // Invoke color change event
        onColorChanged.Invoke(color);
    }
    
    public Color GetCurrentColor()
    {
        return currentColor;
    }
} 