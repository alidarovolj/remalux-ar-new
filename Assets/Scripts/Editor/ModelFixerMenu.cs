using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using ML; // Add ML namespace for SegmentationManager
using System.Reflection;
using UnityEngine.SceneManagement;

/// <summary>
/// Menu items for fixing tensor shape issues with SegmentationManager
/// </summary>
public static class ModelFixerMenu
{
    [MenuItem("AR/Fix SegmentationManager Configuration")]
    public static void FixSegmentationManager()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "No SegmentationManager found in the scene.", "OK");
            return;
        }

        var manager = managers[0];
        Debug.Log($"Found SegmentationManager on {manager.gameObject.name}");

        // Check if ModelConfigFixer is already attached
        var fixer = manager.GetComponent<ModelConfigFixer>();
        if (fixer == null)
        {
            // Add ModelConfigFixer component
            fixer = Undo.AddComponent<ModelConfigFixer>(manager.gameObject);
            Debug.Log("Added ModelConfigFixer component to SegmentationManager");
        }
        
        // Set the segmentationManager field using reflection
        SetSegmentationManagerField(fixer, manager);

        // Call the FixModelConfiguration method
        var method = fixer.GetType().GetMethod("FixModelConfiguration");
        if (method != null)
        {
            method.Invoke(fixer, null);
            Debug.Log("Applied fixes to SegmentationManager configuration");
        }
        else
        {
            Debug.LogError("Could not find FixModelConfiguration method in ModelConfigFixer");
        }
    }

    [MenuItem("AR/Fix All ML Components")]
    public static void FixAllMLComponents()
    {
        // Fix SegmentationManager first
        FixSegmentationManager();

        // Fix MLManagerAdapter instances
        var adapters = Object.FindObjectsOfType<MLManagerAdapter>();
        foreach (var adapter in adapters)
        {
            Debug.Log($"Checking MLManagerAdapter on {adapter.gameObject.name}");
            if (adapter.DownsampleFactor < 2)
            {
                Undo.RecordObject(adapter, "Update MLManagerAdapter DownsampleFactor");
                adapter.DownsampleFactor = 4;
                Debug.Log($"Updated MLManagerAdapter DownsampleFactor to 4");
            }
            
            if (adapter.ProcessingInterval < 0.2f)
            {
                Undo.RecordObject(adapter, "Update MLManagerAdapter ProcessingInterval");
                adapter.ProcessingInterval = 0.5f;
                Debug.Log($"Updated MLManagerAdapter ProcessingInterval to 0.5");
            }
        }

        // Fix DeepLabPredictors
        var predictors = Object.FindObjectsOfType<ML.DeepLab.DeepLabPredictor>();
        foreach (var predictor in predictors)
        {
            Debug.Log($"Checking DeepLabPredictor on {predictor.gameObject.name}");
            Undo.RecordObject(predictor, "Update DeepLabPredictor settings");
            
            if (predictor.outputTensorName != "logits")
            {
                predictor.outputTensorName = "logits";
                Debug.Log($"Updated DeepLabPredictor outputTensorName to 'logits'");
            }
            
            if (predictor.wallClassId != 9)
            {
                predictor.wallClassId = 9;
                Debug.Log($"Updated DeepLabPredictor wallClassId to 9");
            }
            
            if (!predictor.useStandardFormatInput)
            {
                predictor.useStandardFormatInput = true;
                Debug.Log($"Enabled useStandardFormatInput on DeepLabPredictor");
            }
        }

        EditorUtility.DisplayDialog("ML Components Fixed", 
            "Applied fixes to all ML components in the scene.\n\n" +
            "• SegmentationManager configuration updated\n" +
            "• MLManagerAdapter settings optimized\n" +
            "• DeepLabPredictor settings corrected", "OK");
    }
    
    [MenuItem("AR/ML Tools/Add ARMLInitializer")]
    public static void AddARMLInitializer()
    {
        var existingInitializer = FindObjectOfType<ARMLInitializer>();
        if (existingInitializer != null)
        {
            Selection.activeGameObject = existingInitializer.gameObject;
            EditorGUIUtility.PingObject(existingInitializer.gameObject);
            EditorUtility.DisplayDialog("ARMLInitializer Already Exists", 
                $"ARMLInitializer already exists on the GameObject: {existingInitializer.gameObject.name}", "OK");
            return;
        }
        
        GameObject initializerObj = new GameObject("ARMLInitializer");
        Undo.RegisterCreatedObjectUndo(initializerObj, "Create ARMLInitializer");
        
        var initializer = initializerObj.AddComponent<ARMLInitializer>();
        
        EditorUtility.SetDirty(initializerObj);
        Selection.activeGameObject = initializerObj;
        EditorUtility.DisplayDialog("ARMLInitializer Added", 
            "ARMLInitializer has been added to the scene on a new GameObject.\n\n" +
            "It will help connect AR and ML systems at runtime.", "OK");
    }
    
    [MenuItem("AR/ML Tools/Add ModelConfigFixer To Selection")]
    public static void AddModelFixerToSelection()
    {
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            EditorUtility.DisplayDialog(
                "No Object Selected", 
                "Please select a GameObject with a SegmentationManager component.", 
                "OK");
            return;
        }
        
        SegmentationManager manager = selectedObject.GetComponent<SegmentationManager>();
        if (manager == null)
        {
            EditorUtility.DisplayDialog(
                "No SegmentationManager Component", 
                "The selected GameObject does not have a SegmentationManager component.", 
                "OK");
            return;
        }
        
        AddFixerToManager(manager);
    }
    
    private static void AddFixerToManager(SegmentationManager manager)
    {
        if (manager.gameObject.GetComponent<ModelConfigFixer>() != null)
        {
            if (!EditorUtility.DisplayDialog(
                "ModelConfigFixer Already Exists",
                $"A ModelConfigFixer component already exists on {manager.gameObject.name}.\n\n" + 
                "Do you want to replace it?",
                "Replace", "Cancel"))
            {
                return;
            }
            
            Object.DestroyImmediate(manager.gameObject.GetComponent<ModelConfigFixer>());
        }
        
        Undo.RecordObject(manager.gameObject, "Add ModelConfigFixer");
        
        ModelConfigFixer fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
        
        // Use the helper method to set the segmentationManager field
        SetSegmentationManagerField(fixer, manager);
        
        EditorUtility.SetDirty(manager.gameObject);
        
        Selection.activeGameObject = manager.gameObject;
        
        Debug.Log($"Added ModelConfigFixer to {manager.gameObject.name}");
        
        EditorUtility.DisplayDialog(
            "ModelConfigFixer Added",
            $"Added ModelConfigFixer to {manager.gameObject.name}.\n\n" +
            "When you play the scene, it will automatically try to fix common model shape issues.",
            "OK");
    }
    
    // Selection window for multiple managers
    public class ManagerSelectionWindow : EditorWindow
    {
        private SegmentationManager[] managers;
        private Vector2 scrollPosition;
        
        public void Initialize(SegmentationManager[] managers)
        {
            this.managers = managers;
            minSize = new Vector2(300, 200);
        }
        
        void OnGUI()
        {
            EditorGUILayout.LabelField("Select SegmentationManager", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Click on a SegmentationManager to add ModelConfigFixer:");
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            foreach (var manager in managers)
            {
                EditorGUILayout.BeginHorizontal();
                
                if (GUILayout.Button(manager.gameObject.name, GUILayout.Height(30)))
                {
                    AddFixerToManager(manager);
                    Close();
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
        }
    }

    [MenuItem("AR Menu/Add ModelConfigFixer")]
    public static void AddModelConfigFixer()
    {
        // Find an existing SegmentationManager in the scene
        SegmentationManager manager = Object.FindObjectOfType<SegmentationManager>();
        
        if (manager == null)
        {
            Debug.LogError("Could not find SegmentationManager in the scene. Please add one first.");
            return;
        }
        
        // Create or get component on the same GameObject
        GameObject targetObject = manager.gameObject;
        ModelConfigFixer fixer = targetObject.GetComponent<ModelConfigFixer>();
        
        if (fixer == null)
        {
            fixer = targetObject.AddComponent<ModelConfigFixer>();
            
            // Set the segmentationManager field using reflection
            var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
                
            if (segmentationManagerField != null)
            {
                segmentationManagerField.SetValue(fixer, manager);
            }
            else
            {
                Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
            }
            
            Debug.Log("Added ModelConfigFixer to " + targetObject.name);
        }
        else
        {
            Debug.Log("ModelConfigFixer already exists on " + targetObject.name);
        }
        
        // Select the GameObject
        Selection.activeGameObject = targetObject;
        
        // Focus on the newly added component
        EditorGUIUtility.PingObject(fixer);
    }

    // Helper method to set the segmentationManager field using reflection
    private static void SetSegmentationManagerField(ModelConfigFixer fixer, SegmentationManager manager)
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
        
        Debug.LogError("Could not find any way to set segmentationManager reference on ModelConfigFixer");
    }

    [MenuItem("Tools/ML/Fix All SegmentationManagers")]
    public static void FixAllSegmentationManagers()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            Debug.LogWarning("No SegmentationManager found in the scene");
            return;
        }

        Debug.Log($"Found {managers.Length} SegmentationManager components");
        
        foreach (var manager in managers)
        {
            // Check if this manager already has a fixer
            var fixer = manager.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                // Add a new fixer component
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                
                // Set the segmentationManager field using reflection
                var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.NonPublic);
                    
                if (segmentationManagerField != null)
                {
                    segmentationManagerField.SetValue(fixer, manager);
                }
                else
                {
                    Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
                }
                
                Debug.Log($"Added ModelConfigFixer to {manager.gameObject.name}");
            }
            else
            {
                Debug.Log($"Found existing ModelConfigFixer on {manager.gameObject.name}");
            }
            
            // Run the fixer to update the configuration
            fixer.FixModelConfiguration();
        }
        
        Debug.Log("Finished fixing all SegmentationManager components");
    }
    
    [MenuItem("Tools/ML/Log Model Input Shapes")]
    public static void LogAllModelInputShapes()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            Debug.LogWarning("No SegmentationManager found in the scene");
            return;
        }

        Debug.Log($"Found {managers.Length} SegmentationManager components - logging input shapes");
        
        foreach (var manager in managers)
        {
            Debug.Log($"Logging input shape for {manager.gameObject.name}");
            
            // Call the LogModelInputShape method using reflection since it might be private
            var logMethod = manager.GetType().GetMethod("LogModelInputShape", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
            if (logMethod != null)
            {
                logMethod.Invoke(manager, null);
            }
            else
            {
                Debug.LogError($"Could not find LogModelInputShape method in {manager.GetType().Name}");
            }
        }
    }

    [MenuItem("AR/Debug/Fix All SegmentationManagers")]
    public static void FixAllSegmentationManagers()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        Debug.Log($"Found {managers.Length} SegmentationManager components in the scene");
        
        foreach (var manager in managers)
        {
            var fixer = manager.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                Debug.Log("Added ModelConfigFixer component to " + manager.gameObject.name);
            }

            // Set the segmentationManager field using reflection
            var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | 
                System.Reflection.BindingFlags.NonPublic);
                
            if (segmentationManagerField != null)
            {
                segmentationManagerField.SetValue(fixer, manager);
            }
            else
            {
                Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
            }

            fixer.FixModelConfiguration();
        }
    }

    [MenuItem("AR/Debug/Log Model Input Shapes")]
    public static void LogAllModelInputShapes()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            Debug.LogWarning("No SegmentationManager found in the scene!");
            return;
        }
        
        foreach (var manager in managers)
        {
            Debug.Log($"Logging input shape for SegmentationManager on {manager.gameObject.name}");
            
            // Using reflection to call the LogModelInputShape method
            MethodInfo logMethod = manager.GetType().GetMethod("LogModelInputShape", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (logMethod != null)
            {
                logMethod.Invoke(manager, null);
            }
            else
            {
                Debug.LogError("Could not find LogModelInputShape method in SegmentationManager");
            }
        }
    }
    
    [MenuItem("AR/Debug/Analyze Reshape Error")]
    public static void AnalyzeReshapeError()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            Debug.LogWarning("No SegmentationManager found in the scene!");
            return;
        }
        
        foreach (var manager in managers)
        {
            var fixer = manager.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                Debug.Log("Added ModelConfigFixer component to " + manager.gameObject.name);
                
                // Set the segmentationManager field using reflection
                var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                if (segmentationManagerField != null)
                {
                    segmentationManagerField.SetValue(fixer, manager);
                }
                else
                {
                    Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
                    continue;
                }
            }
            
            fixer.AnalyzeReshapeError();
        }
    }
    
    [MenuItem("AR/Debug/Log Model Structure")]
    public static void LogModelStructure()
    {
        var managers = Object.FindObjectsOfType<SegmentationManager>();
        if (managers.Length == 0)
        {
            Debug.LogWarning("No SegmentationManager found in the scene!");
            return;
        }
        
        foreach (var manager in managers)
        {
            var fixer = manager.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                Debug.Log("Added ModelConfigFixer component to " + manager.gameObject.name);
                
                // Set the segmentationManager field using reflection
                var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                if (segmentationManagerField != null)
                {
                    segmentationManagerField.SetValue(fixer, manager);
                }
                else
                {
                    Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
                    continue;
                }
            }
            
            fixer.LogModelStructure();
        }
    }

    [MenuItem("Tools/ML/Fix Configuration")]
    public static void FixConfiguration()
    {
        // ... existing code ...
    }
    
    [MenuItem("Tools/ML/Analyze Reshape Error")]
    public static void AnalyzeReshapeError()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        var fixer = GameObject.FindObjectOfType<ML.ModelConfigFixer>();
        if (fixer == null)
        {
            Debug.LogError("No ModelConfigFixer found in the scene");
            return;
        }
        
        // Get the current size from the error message
        int size = 7593984; // From error message
        
        fixer.AnalyzeReshapeError(
            size, 
            manager.inputWidth, 
            manager.inputHeight, 
            manager.inputChannels, 
            manager.outputName
        );
        
        Debug.Log("Analysis complete. Check console for results.");
    }

    [MenuItem("Tools/ML/Change Input Dimensions/224x224x3")]
    public static void SetInputDimensions224()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        manager.SetInputDimensions(224, 224, 3);
    }
    
    [MenuItem("Tools/ML/Change Input Dimensions/256x256x3")]
    public static void SetInputDimensions256()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        manager.SetInputDimensions(256, 256, 3);
    }
    
    [MenuItem("Tools/ML/Change Input Dimensions/512x512x3")]
    public static void SetInputDimensions512()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        manager.SetInputDimensions(512, 512, 3);
    }
    
    [MenuItem("Tools/ML/Change Input Dimensions/271x271x3")]
    public static void SetInputDimensions271()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        manager.SetInputDimensions(271, 271, 3);
    }
    
    [MenuItem("Tools/ML/Change Input Dimensions/513x513x3")]
    public static void SetInputDimensions513()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        manager.SetInputDimensions(513, 513, 3);
    }

    [MenuItem("Tools/ML/Auto-Fix Reshape Error")]
    public static void AutoFixReshapeError()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        var fixer = GameObject.FindObjectOfType<ML.ModelConfigFixer>();
        if (fixer == null)
        {
            Debug.LogError("No ModelConfigFixer found in the scene");
            return;
        }
        
        // Get the error size from the error message
        int errorSize = 7593984; // From error message
        
        fixer.TryAutoFix(errorSize);
        
        Debug.Log("Auto-fix attempt complete. Check console for results.");
    }

    [MenuItem("Tools/ML/Test Dimensions/Try 576x576 (Output 72x32)")]
    public static void TestDimensions576()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        bool success = manager.TestInputDimensions(576, 256, 3, "logits");
        Debug.Log("Test completed, success: " + success);
    }
    
    [MenuItem("Tools/ML/Test Dimensions/Try 576x256 (Output 72x32)")]
    public static void TestDimensions576x256()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        bool success = manager.TestInputDimensions(576, 256, 3, "logits");
        Debug.Log("Test completed, success: " + success);
    }
    
    [MenuItem("Tools/ML/Test Dimensions/Try 736x256 (Output 92x32)")]
    public static void TestDimensions736x256()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        bool success = manager.TestInputDimensions(736, 256, 3, "logits");
        Debug.Log("Test completed, success: " + success);
    }
    
    [MenuItem("Tools/ML/Test Dimensions/Try DeepLab v3+ 513x513")]
    public static void TestDimensionsDeepLabV3Plus()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        bool success = manager.TestInputDimensions(513, 513, 3, "logits");
        Debug.Log("Test completed, success: " + success);
    }
    
    [MenuItem("Tools/ML/Test All Available Output Names")]
    public static void TestAllOutputNames()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        // Get a reference to the model
        if (manager.ModelAsset == null)
        {
            Debug.LogError("No model asset assigned to SegmentationManager");
            return;
        }
        
        try
        {
            // Load model
            Model model = ModelLoader.Load(manager.ModelAsset);
            
            Debug.Log($"Model loaded with {model.outputs.Count} outputs. Testing each output name:");
            
            foreach (var output in model.outputs)
            {
                Debug.Log($"Testing output '{output}'...");
                
                // Try a few common dimensions per output name
                int[][] dimensionSets = new int[][] 
                {
                    new int[] { 576, 256, 3 },  // Based on reported error (72x32)
                    new int[] { 513, 513, 3 },  // DeepLab v3+
                    new int[] { 512, 512, 3 },  // DeepLab v3
                    new int[] { 256, 256, 3 }   // Common baseline
                };
                
                foreach (var dims in dimensionSets)
                {
                    bool success = manager.TestInputDimensions(dims[0], dims[1], dims[2], output);
                    Debug.Log($"Tested {dims[0]}x{dims[1]}x{dims[2]} with output '{output}': {(success ? "SUCCESS" : "FAILED")}");
                    
                    // If we found a successful configuration, stop testing this output
                    if (success) break;
                }
            }
            
            Debug.Log("Testing complete. Check logs for successful configurations.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error testing outputs: {e.Message}");
        }
    }

    [MenuItem("Tools/ML/Fix 7593984 Error")]
    public static void FixSpecificError()
    {
        var manager = GameObject.FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene");
            return;
        }
        
        // Based on the error, the expected output shape is (1, 32, 72, 103)
        // So we need to find input dimensions that would result in this output
        
        Debug.Log("Attempting to fix 'Cannot reshape array of size 7593984 into shape (n:1, h:32, w:72, c:103)' error");
        
        // Set output name to "logits"
        manager.outputName = "logits";
        
        // Try specific input dimensions that might work with this model
        // For DeepLab models, common input:output ratios are 1:8, 1:16, or 1:4
        // So we need to try different combinations
        
        // Option 1: Try 576x256 (which would produce 72x32 output at 1:8 scale)
        Debug.Log("Trying dimensions 576x256x3 (should produce 72x32 output at 1:8 scale)");
        manager.SetInputDimensions(576, 256, 3);
        
        Debug.Log("Fix applied. Try running the scene again with these settings:");
        Debug.Log("- Input dimensions: 576x256x3");
        Debug.Log("- Output name: logits");
        Debug.Log("- Expected output: 72x32x103");
        
        EditorUtility.DisplayDialog(
            "Potential Fix Applied",
            "Applied potential fix for the 'Cannot reshape array of size 7593984' error.\n\n" +
            "Set input dimensions to 576x256x3 and output name to 'logits'.\n\n" +
            "Try running the scene again. If it still doesn't work, try other options from the Tools > ML menu.",
            "OK"
        );
    }

    [MenuItem("AR/Model Config/Fix Model Configuration")]
    public static void FixModelConfiguration()
    {
        // Find the ModelConfigFixer in the scene
        var fixer = GameObject.FindObjectOfType<ML.ModelConfigFixer>();
        
        if (fixer == null)
        {
            Debug.LogError("Could not find ModelConfigFixer in the scene!");
            return;
        }
        
        // Call the FixModelConfiguration method on the fixer
        Debug.Log("Calling FixModelConfiguration on ModelConfigFixer...");
        fixer.FixModelConfiguration();
        Debug.Log("Model configuration fix completed. Check the console for details.");
    }

    [MenuItem("AR/Model Config/Examine Model Tensor Shapes")]
    private static void ExamineModelTensorShapes()
    {
        var fixer = GameObject.FindObjectOfType<ML.ModelConfigFixer>();
        if (fixer != null)
        {
            fixer.ExamineModelTensorShapes();
        }
        else
        {
            Debug.LogError("ModelConfigFixer not found in scene");
        }
    }

    [MenuItem("AR/Model Config/Examine Model")]
    private static void ExamineModel()
    {
        var fixer = GameObject.FindObjectOfType<ML.ModelConfigFixer>();
        if (fixer != null)
        {
            fixer.ExamineModel();
        }
        else
        {
            Debug.LogError("ModelConfigFixer not found in scene");
        }
    }

    [MenuItem("AR/Model Config/Analyze Possible Tensor Shapes")]
    static void AnalyzePossibleTensorShapes()
    {
        SegmentationManager manager = FindObjectOfType<SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in the scene!");
            return;
        }
        
        string result = EditorInputDialog.Show(
            "Analyze Tensor Shapes", 
            "Enter the total number of elements in the tensor:",
            "7593984"); // Default value based on previous discussions
        
        if (string.IsNullOrEmpty(result))
        {
            Debug.LogError("Operation cancelled or no input provided.");
            return;
        }
        
        if (!long.TryParse(result, out long totalElements) || totalElements <= 0)
        {
            Debug.LogError("Invalid input. Please enter a positive number.");
            return;
        }
        
        // Log the analysis header
        Debug.Log($"[TensorAnalysis] Analyzing possible tensor shapes for {totalElements} elements");
        
        // Check common model formats
        AnalyzeTensorShapeForClassCount(totalElements, 103);
        AnalyzeTensorShapeForClassCount(totalElements, 1);
        
        // Log input tensor information if available
        manager.LogModelInputShape();
    }

    private static void AnalyzeTensorShapeForClassCount(long totalElements, int classCount)
    {
        Debug.Log($"[TensorAnalysis] Possible shapes for {totalElements} elements with class count {classCount}:");
        
        // For NCHW format: [1, classCount, H, W]
        for (int height = 1; height <= 1024; height++)
        {
            if (totalElements % (classCount * height) == 0)
            {
                int width = (int)(totalElements / (classCount * height));
                // Only show reasonable dimensions
                if (width <= 1024 && width > 0)
                {
                    Debug.Log($"[TensorAnalysis] Format NCHW: [1, {classCount}, {height}, {width}]");
                }
            }
        }
        
        // For NHWC format: [1, H, W, classCount]
        for (int height = 1; height <= 1024; height++)
        {
            if (totalElements % (classCount * height) == 0)
            {
                int width = (int)(totalElements / (classCount * height));
                // Only show reasonable dimensions
                if (width <= 1024 && width > 0)
                {
                    Debug.Log($"[TensorAnalysis] Format NHWC: [1, {height}, {width}, {classCount}]");
                }
            }
        }
    }

    [MenuItem("AR/Model Config/Analyze Tensor Shape (7593984)")]
    public static void AnalyzeTensorShape7593984()
    {
        SegmentationManager manager = FindObjectOfType<SegmentationManager>();
        if (manager != null)
        {
            manager.AnalyzeTensorShapePossibilities(7593984, 103);
            Debug.Log("Tensor shape analysis completed. Check console for results.");
        }
        else
        {
            Debug.LogError("SegmentationManager not found in the scene.");
        }
    }

    [MenuItem("AR/Model Config/Analyze Tensor Shapes")]
    private static void AnalyzeTensorShapes()
    {
        var manager = FindObjectOfType<ML.SegmentationManager>();
        if (manager == null)
        {
            Debug.LogError("No SegmentationManager found in scene");
            return;
        }

        // Use a dialog to get tensor size and class count
        int tensorSize = EditorUtility.DisplayDialogComplex(
            "Analyze Tensor Shapes",
            "Enter tensor size to analyze:",
            "Use Default (7593984)", 
            "Cancel",
            "Custom Size");

        int classCount = EditorUtility.DisplayDialogComplex(
            "Analyze Tensor Shapes",
            "Enter class count:",
            "Use Default (103)", 
            "Cancel",
            "Custom Count");

        int actualTensorSize = 7593984;
        int actualClassCount = 103;

        if (tensorSize == 2) // Custom Size
        {
            string sizeString = EditorInputDialog.Show("Enter Tensor Size", "Size:", "7593984");
            if (!string.IsNullOrEmpty(sizeString) && int.TryParse(sizeString, out int customSize))
            {
                actualTensorSize = customSize;
            }
        }
        else if (tensorSize == 1) // Cancel
        {
            return;
        }

        if (classCount == 2) // Custom Count
        {
            string countString = EditorInputDialog.Show("Enter Class Count", "Count:", "103");
            if (!string.IsNullOrEmpty(countString) && int.TryParse(countString, out int customCount))
            {
                actualClassCount = customCount;
            }
        }
        else if (classCount == 1) // Cancel
        {
            return;
        }

        manager.AnalyzeTensorShapePossibilities(actualTensorSize, actualClassCount);
        Debug.Log($"Analyzed tensor shapes for size {actualTensorSize} with {actualClassCount} classes. Check console for results.");
    }

    [MenuItem("AR/Model Config/Analyze Common Tensor Shapes")]
    public static void AnalyzeCommonTensorShapes()
    {
        ModelConfigFixer fixer = FindObjectOfType<ModelConfigFixer>();
        if (fixer == null)
        {
            Debug.LogError("ModelConfigFixer not found in scene");
            return;
        }

        // Analyze common tensor sizes for 103 classes
        Debug.Log("=== Analyzing Common Tensor Shapes for 103 Classes ===");
        fixer.AnalyzeTensorShapePossibilities(7593984, 103);  // The size from error logs
        
        // Calculate input size for reference
        int inputSize = 224 * 224 * 3;
        Debug.Log($"Input dimensions: 224x224x3 = {inputSize} elements");
    }
} 