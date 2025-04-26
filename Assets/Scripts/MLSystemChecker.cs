using System.Collections;
using UnityEngine;
using Unity.Barracuda;
using ML.DeepLab;
using System;
using System.Reflection;

/// <summary>
/// Проверяет наличие и состояние компонентов ML-системы в сцене
/// </summary>
[DefaultExecutionOrder(-500)] // Запускается после ARMLInitializer (-999) и ModelAssigner (-100)
public class MLSystemChecker : MonoBehaviour
{
    [Header("Target Components")]
    [SerializeField] private SegmentationManager segmentationManager;
    [SerializeField] private EnhancedDeepLabPredictor deepLabPredictor;
    
    [Header("Settings")]
    [SerializeField] private bool checkOnStart = true;
    [SerializeField] private bool fixSegmentationManager = true;
    [SerializeField] private bool fixDeepLabPredictor = true;
    [SerializeField] private bool verbose = true;
    
    // Start запускается после Awake всех объектов, включая инициализаторы
    private IEnumerator Start()
    {
        // Даем время другим компонентам инициализироваться
        yield return new WaitForSeconds(0.5f);
        
        if (checkOnStart)
        {
            CheckAndFixMLSystem();
        }
    }
    
    /// <summary>
    /// Check and fix ML system configuration
    /// </summary>
    public void CheckAndFixMLSystem()
    {
        LogMessage("Starting ML System check...");
        
        FindComponents();
        
        if (fixSegmentationManager && segmentationManager != null)
        {
            FixSegmentationManager();
        }
        
        if (fixDeepLabPredictor && deepLabPredictor != null)
        {
            FixDeepLabPredictor();
        }
        
        LogMessage("ML System check completed");
    }
    
    /// <summary>
    /// Find ML components if not set
    /// </summary>
    private void FindComponents()
    {
        if (segmentationManager == null)
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
            if (segmentationManager != null)
            {
                LogMessage("Found SegmentationManager automatically");
            }
        }
        
        if (deepLabPredictor == null)
        {
            deepLabPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            if (deepLabPredictor != null)
            {
                LogMessage("Found EnhancedDeepLabPredictor automatically");
            }
        }
    }
    
    /// <summary>
    /// Fix SegmentationManager configuration
    /// </summary>
    private void FixSegmentationManager()
    {
        LogMessage("Fixing SegmentationManager configuration...");
        
        try
        {
            // Fix output name
            SetPrivateField(segmentationManager, "outputName", "logits");
            
            // Fix wall class ID
            SetPrivateField(segmentationManager, "wallClassId", 9);
            
            // Set input dimensions to common values for segmentation models
            SetPrivateField(segmentationManager, "inputWidth", 256);
            SetPrivateField(segmentationManager, "inputHeight", 256);
            
            // Ensure debug mode is on during development
            SetPrivateField(segmentationManager, "debugMode", true);
            
            // Reinitialize the model
            MethodInfo initMethod = segmentationManager.GetType().GetMethod(
                "InitializeModel", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (initMethod != null)
            {
                initMethod.Invoke(segmentationManager, null);
                LogMessage("Reinitialized SegmentationManager model");
            }
            
            LogMessage("SegmentationManager configuration fixed successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error fixing SegmentationManager: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Fix DeepLabPredictor configuration
    /// </summary>
    private void FixDeepLabPredictor()
    {
        LogMessage("Fixing EnhancedDeepLabPredictor configuration...");
        
        try
        {
            // Set DeepLabPredictor properties using reflection to handle missing fields
            
            // Set wall class ID
            deepLabPredictor.WallClassId = 9;
            
            // Configure performance settings
            SetPrivateField(deepLabPredictor, "enableDownsampling", true);
            SetPrivateField(deepLabPredictor, "downsamplingFactor", 2);
            SetPrivateField(deepLabPredictor, "minSegmentationInterval", 0.2f);
            
            // Enable post-processing for better results
            SetPrivateField(deepLabPredictor, "applyNoiseReduction", true);
            SetPrivateField(deepLabPredictor, "applyWallFilling", true);
            
            LogMessage("EnhancedDeepLabPredictor configuration fixed successfully");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error fixing EnhancedDeepLabPredictor: {e.Message}\n{e.StackTrace}");
        }
    }
    
    /// <summary>
    /// Set a private field value using reflection
    /// </summary>
    private void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null)
        {
            Debug.LogError($"Cannot set field {fieldName} on null target");
            return;
        }
        
        FieldInfo field = target.GetType().GetField(
            fieldName, 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (field != null)
        {
            field.SetValue(target, value);
            LogMessage($"Set {fieldName} = {value}");
        }
        else
        {
            Debug.LogWarning($"Field {fieldName} not found on {target.GetType().Name}");
        }
    }
    
    /// <summary>
    /// Log a message with appropriate verbosity
    /// </summary>
    private void LogMessage(string message)
    {
        if (verbose)
        {
            Debug.Log($"[MLSystemChecker] {message}");
        }
    }
    
    [ContextMenu("Run ML System Check")]
    private void RunCheck()
    {
        CheckAndFixMLSystem();
    }
} 