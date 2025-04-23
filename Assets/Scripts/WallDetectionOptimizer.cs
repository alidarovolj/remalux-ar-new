using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using ML.DeepLab; // Add namespace for EnhancedDeepLabPredictor

/// <summary>
/// Component for controlling and optimizing wall detection with user interface.
/// </summary>
public class WallDetectionOptimizer : MonoBehaviour
{
    [Header("Components")]
    [Tooltip("Reference to DeepLab fixer")]
    public DeepLabFixer fixer;
    
    [Tooltip("Reference to enhanced predictor (if used)")]
    public EnhancedDeepLabPredictor enhancedPredictor;
    
    [Header("UI Settings")]
    [Tooltip("Threshold slider")]
    public Slider thresholdSlider;
    
    [Tooltip("Text for displaying current threshold")]
    public Text thresholdText;
    
    [Tooltip("Dropdown for wall class selection")]
    public Dropdown wallClassDropdown;
    
    [Tooltip("Toggle for ArgMax mode")]
    public Toggle argMaxToggle;
    
    [Tooltip("Optimization tools panel")]
    public GameObject optimizerPanel;
    
    [Header("Settings")]
    [Tooltip("Automatically apply fixes at startup")]
    public bool autoApplyFixes = true;
    
    [Tooltip("Show optimization panel")]
    public bool showOptimizationPanel = true;
    
    [Tooltip("Use enhanced predictor instead of patch")]
    public bool useEnhancedPredictor = true;
    
    [Range(0.0f, 1.0f)]
    [Tooltip("Initial classification threshold")]
    public float initialThreshold = 0.05f; // Lower threshold for better detection
    
    [Tooltip("Classification threshold")]
    public float classificationThreshold = 0.15f;
    
    [Tooltip("Wall class ID")]
    public byte wallClassId = 9; // Updated to ADE20K wall class ID (9)
    
    [Tooltip("ArgMax mode")]
    public bool useArgMaxMode = true;
    
    // Original DeepLabPredictor
    private DeepLabPredictor originalPredictor;
    
    // Common settings
    private int currentWallClassId = 9; // Updated to ADE20K wall class ID (9)
    private bool useArgMax = true;
    
    void Awake()
    {
        // Find components if they are not assigned
        if (fixer == null)
            fixer = FindFirstObjectByType<DeepLabFixer>();
            
        if (enhancedPredictor == null)
            enhancedPredictor = FindFirstObjectByType<EnhancedDeepLabPredictor>();
            
        // Find original predictor
        originalPredictor = FindFirstObjectByType<DeepLabPredictor>();
        
        // If no predictor is found, create an enhanced one
        if (originalPredictor == null && enhancedPredictor == null && useEnhancedPredictor)
        {
            CreateEnhancedPredictor();
        }
    }
    
    void Start()
    {
        // Apply fixes automatically at startup
        if (autoApplyFixes)
        {
            StartCoroutine(ApplyFixesDelayed());
        }
        
        // Setup UI
        SetupUI();
        
        // Show/hide optimization panel
        if (optimizerPanel != null)
            optimizerPanel.SetActive(showOptimizationPanel);
    }
    
    private IEnumerator ApplyFixesDelayed()
    {
        yield return new WaitForSeconds(0.5f);
        ApplyOptimizerSettings();
    }
    
    void SetupUI()
    {
        // Setup threshold slider
        if (thresholdSlider != null)
        {
            thresholdSlider.minValue = 0.01f;
            thresholdSlider.maxValue = 0.99f;
            thresholdSlider.value = initialThreshold;
            thresholdSlider.onValueChanged.AddListener(OnThresholdChanged);
            
            // Update text
            UpdateThresholdText(initialThreshold);
        }
        
        // Setup dropdown for classes
        if (wallClassDropdown != null)
        {
            wallClassDropdown.ClearOptions();
            
            // Add options for the first 20 classes (expanded to support class 9)
            for (int i = 0; i < 20; i++)
            {
                wallClassDropdown.options.Add(new Dropdown.OptionData($"Class {i}"));
            }
            
            wallClassDropdown.value = currentWallClassId;
            wallClassDropdown.onValueChanged.AddListener(OnWallClassChanged);
            wallClassDropdown.RefreshShownValue();
        }
        
        // Setup ArgMax toggle
        if (argMaxToggle != null)
        {
            argMaxToggle.isOn = useArgMax;
            argMaxToggle.onValueChanged.AddListener(OnArgMaxToggled);
        }
    }
    
    public void ApplyOptimizerSettings()
    {
        if (enhancedPredictor != null)
        {
            enhancedPredictor.ClassificationThreshold = classificationThreshold;
            enhancedPredictor.WallClassId = wallClassId;
            enhancedPredictor.useArgMaxMode = useArgMaxMode;
            ApplyToEnhancedPredictor();
        }
        
        Debug.Log($"WallDetectionOptimizer: Applied settings - Threshold: {classificationThreshold}, Wall Class: {wallClassId}, ArgMax: {useArgMaxMode}");
    }
    
    private void ApplyToEnhancedPredictor()
    {
        if (enhancedPredictor == null) return;
        
        enhancedPredictor.ClassificationThreshold = classificationThreshold;
        enhancedPredictor.WallClassId = wallClassId;
        enhancedPredictor.useArgMaxMode = useArgMaxMode;
        
        Debug.Log("WallDetectionOptimizer: Applied settings to enhanced predictor");
    }
    
    void CreateEnhancedPredictor()
    {
        // Create object for enhanced predictor
        GameObject predictorObj = new GameObject("Enhanced DeepLab Predictor");
        enhancedPredictor = predictorObj.AddComponent<EnhancedDeepLabPredictor>();
        
        predictorObj.transform.SetParent(transform);
        
        Debug.Log("WallDetectionOptimizer: Created enhanced predictor");
    }
    
    // Public methods for calling from UI
    public void OnThresholdChanged(float value)
    {
        initialThreshold = value;
        UpdateThresholdText(value);
        
        // Apply changes immediately
        if (enhancedPredictor != null)
            enhancedPredictor.ClassificationThreshold = value;
            
        if (originalPredictor != null)
            originalPredictor.ClassificationThreshold = value;
            
        if (fixer != null)
            fixer.wallThreshold = value;
    }
    
    void UpdateThresholdText(float value)
    {
        if (thresholdText != null)
            thresholdText.text = $"Threshold: {value:F2}";
    }
    
    public void OnWallClassChanged(int value)
    {
        currentWallClassId = value;
        
        // Apply changes immediately
        if (enhancedPredictor != null)
            enhancedPredictor.WallClassId = (byte)value;
            
        if (originalPredictor != null)
            originalPredictor.WallClassId = (byte)value;
            
        Debug.Log($"WallDetectionOptimizer: Wall class ID changed to {value}");
    }
    
    public void OnArgMaxToggled(bool value)
    {
        useArgMax = value;
        
        // Apply changes immediately
        if (enhancedPredictor != null)
            enhancedPredictor.useArgMaxMode = value;
            
        Debug.Log($"WallDetectionOptimizer: ArgMax mode set to {value}");
    }
    
    // Method for calling from UI
    public void ShowClassDistribution()
    {
        StartCoroutine(CollectClassStats());
    }
    
    IEnumerator CollectClassStats()
    {
        // First, reset settings to get data for all classes
        float originalThreshold = initialThreshold;
        
        if (enhancedPredictor != null)
        {
            // Temporarily set low threshold for all classes
            enhancedPredictor.ClassificationThreshold = 0.01f;
            
            // Give time for processing multiple frames
            yield return new WaitForSeconds(1.0f);
            
            // Return original value
            enhancedPredictor.ClassificationThreshold = originalThreshold;
        }
        
        // Show message to user about analysis results
        Debug.Log("WallDetectionOptimizer: Class distribution collected. Check logs for details.");
    }
    
    // Method for calling from UI
    public void RestoreDefaultSettings()
    {
        initialThreshold = 0.3f;
        currentWallClassId = 9;
        useArgMax = true;
        
        // Update UI
        if (thresholdSlider != null)
            thresholdSlider.value = initialThreshold;
            
        if (wallClassDropdown != null)
            wallClassDropdown.value = currentWallClassId;
            
        if (argMaxToggle != null)
            argMaxToggle.isOn = useArgMax;
            
        // Apply settings
        ApplyOptimizerSettings();
        
        Debug.Log("WallDetectionOptimizer: Restored default settings");
    }
    
    // Method for calling from UI
    public void ToggleOptimizationPanel()
    {
        if (optimizerPanel != null)
        {
            optimizerPanel.SetActive(!optimizerPanel.activeSelf);
        }
    }
    
    // Wall class for analysis
    public void SetWallClass(int classId)
    {
        if (wallClassDropdown != null)
            wallClassDropdown.value = classId;
        else
        {
            currentWallClassId = classId;
            OnWallClassChanged(classId);
        }
    }
    
    /// <summary>
    /// Load previously saved settings or set defaults
    /// </summary>
    private void LoadSavedSettings()
    {
        try
        {
            // Load Wall Class ID
            if (PlayerPrefs.HasKey("WallClassId"))
            {
                int savedWallClassId = PlayerPrefs.GetInt("WallClassId");
                
                // Apply to enhanced predictor
                if (enhancedPredictor != null)
                {
                    enhancedPredictor.WallClassId = (byte)savedWallClassId;
                    currentWallClassId = savedWallClassId;
                }
                
                // Apply to original predictor
                if (originalPredictor != null)
                {
                    originalPredictor.WallClassId = (byte)savedWallClassId;
                    currentWallClassId = savedWallClassId;
                }
            }
            
            // Load threshold
            if (PlayerPrefs.HasKey("ClassificationThreshold"))
            {
                float savedThreshold = PlayerPrefs.GetFloat("ClassificationThreshold");
                initialThreshold = savedThreshold;
                
                // Apply to enhanced predictor
                if (enhancedPredictor != null)
                {
                    enhancedPredictor.ClassificationThreshold = savedThreshold;
                }
                
                // Apply to original predictor
                if (originalPredictor != null)
                {
                    originalPredictor.ClassificationThreshold = savedThreshold;
                }
            }
            
            // Load ArgMax setting
            if (PlayerPrefs.HasKey("UseArgMaxMode"))
            {
                bool savedArgMax = PlayerPrefs.GetInt("UseArgMaxMode") == 1;
                useArgMax = savedArgMax;
                
                // Apply to enhanced predictor
                if (enhancedPredictor != null)
                {
                    enhancedPredictor.useArgMaxMode = savedArgMax;
                }
            }
            
            Debug.Log("WallDetectionOptimizer: Settings loaded from PlayerPrefs");
            
            // Update UI with current values
            UpdateUIWithCurrentValues();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallDetectionOptimizer: Error loading settings: {e.Message}");
        }
    }
    
    /// <summary>
    /// Update UI elements with current values
    /// </summary>
    private void UpdateUIWithCurrentValues()
    {
        if (thresholdSlider != null)
        {
            thresholdSlider.value = initialThreshold;
            UpdateThresholdText(initialThreshold);
        }
        
        if (wallClassDropdown != null)
        {
            wallClassDropdown.value = currentWallClassId;
        }
        
        if (argMaxToggle != null)
        {
            argMaxToggle.isOn = useArgMax;
        }
    }

    /// <summary>
    /// Apply the current settings to the DeepLabPredictor and WallMeshRenderer
    /// </summary>
    private void ApplySettings()
    {
        try
        {
            // Apply to enhanced predictor if available
            if (enhancedPredictor != null)
            {
                enhancedPredictor.WallClassId = (byte)currentWallClassId;
                enhancedPredictor.ClassificationThreshold = initialThreshold;
                enhancedPredictor.useArgMaxMode = useArgMax;
                
                Debug.Log($"WallDetectionOptimizer: Applied settings to EnhancedDeepLabPredictor - " +
                          $"WallClassId: {currentWallClassId}, " +
                          $"Threshold: {initialThreshold:F2}, " +
                          $"ArgMax: {useArgMax}");
            }
            
            // Apply to original predictor if available
            if (originalPredictor != null)
            {
                originalPredictor.WallClassId = (byte)currentWallClassId;
                originalPredictor.ClassificationThreshold = initialThreshold;
                
                Debug.Log($"WallDetectionOptimizer: Applied settings to DeepLabPredictor - " +
                          $"WallClassId: {currentWallClassId}, " +
                          $"Threshold: {initialThreshold:F2}");
            }
            
            // Save all preferences
            PlayerPrefs.SetInt("WallClassId", currentWallClassId);
            PlayerPrefs.SetFloat("ClassificationThreshold", initialThreshold);
            PlayerPrefs.SetInt("UseArgMaxMode", useArgMax ? 1 : 0);
            PlayerPrefs.Save();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"WallDetectionOptimizer: Error applying settings: {e.Message}");
        }
    }
} 