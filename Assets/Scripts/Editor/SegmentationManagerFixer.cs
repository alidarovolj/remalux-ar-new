using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using ML; // Add ML namespace for SegmentationManager
using System.Reflection;

/// <summary>
/// Custom editor tool to fix common SegmentationManager configuration issues
/// </summary>
[CustomEditor(typeof(SegmentationManager))]
public class SegmentationManagerFixer : Editor 
{
    // Common output names to try
    private readonly string[] commonOutputNames = new string[] { 
        "logits", "SemanticPredictions", "output_segmentations", "output", "softmax", 
        "final_output", "segmentation_output", "predictions", "sigmoid_output" 
    };
    
    // Common input dimensions to try
    private readonly Vector2Int[] commonInputDimensions = new Vector2Int[] {
        new Vector2Int(256, 256),
        new Vector2Int(224, 224),
        new Vector2Int(320, 320),
        new Vector2Int(512, 512),
        new Vector2Int(384, 384)
    };
    
    private bool showFixSection = true;
    private bool showInputOptions = false;
    private bool showOutputOptions = false;
    
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SegmentationManager manager = (SegmentationManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Troubleshooting Tools", EditorStyles.boldLabel);

        if (GUILayout.Button("Add ModelConfigFixer"))
        {
            ModelConfigFixer fixer = manager.gameObject.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                Undo.RecordObject(manager.gameObject, "Add ModelConfigFixer");
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                EditorUtility.SetDirty(manager.gameObject);
            }

            // Set the segmentationManager field using reflection
            SetSegmentationManagerField(fixer, manager);
        }

        if (GUILayout.Button("Fix Configuration (Apply All Fixes)"))
        {
            ModelConfigFixer fixer = manager.gameObject.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                
                // Set the segmentationManager field using enhanced reflection
                SetSegmentationManagerField(fixer, manager);
            }
            
            // Call the FixModelConfiguration method
            var method = fixer.GetType().GetMethod("FixModelConfiguration", 
                BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(fixer, null);
                Debug.Log("Applied configuration fixes via ModelConfigFixer");
            }
            
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(fixer);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Individual Fixes", EditorStyles.boldLabel);

        if (GUILayout.Button("Set Output Name to 'logits'"))
        {
            Undo.RecordObject(manager, "Set Output Name");
            SetPrivateField(manager, "_outputName", "logits");
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Set Wall Class ID to 9"))
        {
            Undo.RecordObject(manager, "Set Wall Class ID");
            SetPrivateField(manager, "_wallClassId", 9);
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Set Input Dimensions to 224x224"))
        {
            Undo.RecordObject(manager, "Set Input Dimensions");
            SetPrivateField(manager, "_inputWidth", 224);
            SetPrivateField(manager, "_inputHeight", 224);
            EditorUtility.SetDirty(manager);
        }

        if (GUILayout.Button("Log Tensor Dimensions"))
        {
            var method = manager.GetType().GetMethod("LogTensorDimensions", 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
            if (method != null)
            {
                method.Invoke(manager, null);
            }
            else
            {
                Debug.LogError("Could not find LogTensorDimensions method");
            }
        }
    }
    
    // Helper method to set the segmentationManager field using reflection
    private void SetSegmentationManagerField(ModelConfigFixer fixer, SegmentationManager manager)
    {
        if (fixer == null || manager == null) return;
        
        // Try using a public property first if it exists
        var segManagerProperty = fixer.GetType().GetProperty("segmentationManager", 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (segManagerProperty != null && segManagerProperty.CanWrite)
        {
            segManagerProperty.SetValue(fixer, manager);
            Debug.Log("Set segmentationManager via property");
            return;
        }
        
        // Next try a public or private field
        var segManagerField = fixer.GetType().GetField("segmentationManager", 
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            
        if (segManagerField != null)
        {
            segManagerField.SetValue(fixer, manager);
            Debug.Log("Set segmentationManager via field reflection");
            return;
        }
        
        // Last resort - check for a method that might set the reference
        var setManagerMethod = fixer.GetType().GetMethod("SetSegmentationManager", 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (setManagerMethod != null)
        {
            setManagerMethod.Invoke(fixer, new object[] { manager });
            Debug.Log("Set segmentationManager via method call");
            return;
        }
        
        Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
    }
    
    private void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, 
            BindingFlags.Instance | BindingFlags.NonPublic);
            
        if (field != null)
        {
            field.SetValue(target, value);
        }
        else
        {
            Debug.LogWarning($"Could not find field {fieldName} on {target.GetType().Name}");
        }
    }
    
    private void SetInputDimensions(SegmentationManager manager, int width, int height)
    {
        // Use reflection to access private fields
        var inputWidthField = typeof(SegmentationManager).GetField("inputWidth", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        var inputHeightField = typeof(SegmentationManager).GetField("inputHeight", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (inputWidthField != null && inputHeightField != null)
        {
            // Get current values for undo
            int currentWidth = (int)inputWidthField.GetValue(manager);
            int currentHeight = (int)inputHeightField.GetValue(manager);
            
            // Record object for undo
            Undo.RecordObject(manager, "Change Input Dimensions");
            
            // Set new values
            inputWidthField.SetValue(manager, width);
            inputHeightField.SetValue(manager, height);
            
            // Mark as dirty
            EditorUtility.SetDirty(manager);
            
            Debug.Log($"Changed input dimensions from {currentWidth}×{currentHeight} to {width}×{height}");
        }
    }
    
    private void SetOutputName(SegmentationManager manager, string outputName)
    {
        // Use reflection to access private fields
        var outputNameField = typeof(SegmentationManager).GetField("outputName", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (outputNameField != null)
        {
            // Get current value for undo
            string currentName = (string)outputNameField.GetValue(manager);
            
            // Record object for undo
            Undo.RecordObject(manager, "Change Output Name");
            
            // Set new value
            outputNameField.SetValue(manager, outputName);
            
            // Mark as dirty
            EditorUtility.SetDirty(manager);
            
            Debug.Log($"Changed output name from '{currentName}' to '{outputName}'");
        }
    }
    
    private void ToggleModelFormat(SegmentationManager manager)
    {
        // Use reflection to access private fields
        var formatField = typeof(SegmentationManager).GetField("isModelNHWCFormat", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
        if (formatField != null)
        {
            // Get current value for undo
            bool currentFormat = (bool)formatField.GetValue(manager);
            
            // Record object for undo
            Undo.RecordObject(manager, "Toggle Model Format");
            
            // Set new value
            formatField.SetValue(manager, !currentFormat);
            
            // Mark as dirty
            EditorUtility.SetDirty(manager);
            
            Debug.Log($"Changed model format from {(currentFormat ? "NHWC" : "NCHW")} to {(!currentFormat ? "NHWC" : "NCHW")}");
        }
    }
    
    private void LogModelOutputs(SegmentationManager manager)
    {
        // Use reflection to call the LogAvailableOutputs method
        var method = typeof(SegmentationManager).GetMethod("LogAvailableOutputs", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
        if (method != null)
        {
            method.Invoke(manager, null);
        }
        else
        {
            Debug.LogWarning("LogAvailableOutputs method not found");
        }
    }
    
    private void ShowCustomDimensionsDialog(SegmentationManager manager)
    {
        // Create a simple dialog to input custom dimensions
        InputDialog window = EditorWindow.GetWindow<InputDialog>(true, "Custom Dimensions", true);
        window.Initialize(
            "Set Custom Input Dimensions",
            "Enter the width and height for the input dimensions:",
            (string result) => {
                // Parse the result (format: "width,height")
                string[] parts = result.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height))
                {
                    SetInputDimensions(manager, width, height);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Input", "Please use the format 'width,height' (e.g. '224,224')", "OK");
                }
            });
    }
    
    private void ShowCustomOutputNameDialog(SegmentationManager manager)
    {
        // Create a simple dialog to input custom output name
        InputDialog window = EditorWindow.GetWindow<InputDialog>(true, "Custom Output Name", true);
        window.Initialize(
            "Set Custom Output Name",
            "Enter the custom output layer name:",
            (string result) => {
                if (!string.IsNullOrEmpty(result))
                {
                    SetOutputName(manager, result);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid Input", "Output name cannot be empty", "OK");
                }
            });
    }
    
    // Simple input dialog for custom values
    public class InputDialog : EditorWindow
    {
        private string title;
        private string message;
        private string inputValue = "";
        private System.Action<string> callback;
        
        public void Initialize(string title, string message, System.Action<string> callback)
        {
            this.title = title;
            this.message = message;
            this.callback = callback;
        }
        
        void OnGUI()
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField(message);
            inputValue = EditorGUILayout.TextField("Value:", inputValue);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            
            if (GUILayout.Button("OK"))
            {
                callback?.Invoke(inputValue);
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    [MenuItem("AR ML Tools/Fix Segmentation Manager")]
    public static void FixSegmentationManager()
    {
        var segManager = FindObjectOfType<SegmentationManager>();
        if (segManager == null)
        {
            EditorUtility.DisplayDialog("Error", 
                "No SegmentationManager found in the scene. Please add a SegmentationManager first.", "OK");
            return;
        }

        // Check if ModelConfigFixer is already attached
        var fixer = segManager.GetComponent<ModelConfigFixer>();
        if (fixer == null)
        {
            // Add ModelConfigFixer if it doesn't exist
            Undo.RecordObject(segManager.gameObject, "Add ModelConfigFixer to SegmentationManager");
            fixer = Undo.AddComponent<ModelConfigFixer>(segManager.gameObject);
            
            // Set the segmentationManager field using reflection
            var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
                
            if (segmentationManagerField != null)
            {
                segmentationManagerField.SetValue(fixer, segManager);
            }
            else
            {
                Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
            }
            
            EditorUtility.DisplayDialog("Success", 
                "ModelConfigFixer has been added to the SegmentationManager. This will fix common configuration issues.", "OK");
        }
        else
        {
            var result = EditorUtility.DisplayDialog("ModelConfigFixer Exists", 
                "ModelConfigFixer is already attached to the SegmentationManager. Would you like to apply fixes now?", 
                "Apply Fixes", "Cancel");
                
            if (result)
            {
                // Try to invoke the fix method
                try
                {
                    var fixMethod = fixer.GetType().GetMethod("FixModelConfiguration", 
                        BindingFlags.Public | BindingFlags.Instance);
                    
                    if (fixMethod != null)
                    {
                        Undo.RecordObject(segManager, "Fix Model Configuration");
                        fixMethod.Invoke(fixer, null);
                        EditorUtility.DisplayDialog("Success", "Model configuration has been fixed.", "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", 
                            "Could not find FixModelConfiguration method. The ModelConfigFixer script may have changed.", "OK");
                    }
                }
                catch (System.Exception e)
                {
                    EditorUtility.DisplayDialog("Error", 
                        $"An error occurred while trying to fix the model configuration: {e.Message}", "OK");
                }
            }
        }
    }
} 