using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;
using ML;
using ML.DeepLab;

/// <summary>
/// Editor menu for fixing common ML model configuration issues
/// </summary>
public class MLConfigFixerMenu : EditorWindow
{
    private SegmentationManager segmentationManager;
    private EnhancedDeepLabPredictor deepLabPredictor;
    private MLManagerAdapter mlManagerAdapter;
    
    private bool fixSegmentationManager = true;
    private bool fixDeepLabPredictor = true;
    private bool fixMLManagerAdapter = true;
    private bool addModelConfigFixer = true;
    
    private Vector2 scrollPosition;
    
    [MenuItem("AR/Fix ML Configuration")]
    public static void ShowWindow()
    {
        var window = GetWindow<MLConfigFixerMenu>("ML Config Fixer");
        window.minSize = new Vector2(400, 500);
        window.FindComponents();
    }
    
    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ML Configuration Fixer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This tool helps fix common configuration issues with ML components", MessageType.Info);
        EditorGUILayout.Space();
        
        // Component References
        EditorGUILayout.LabelField("Components", EditorStyles.boldLabel);
        
        // SegmentationManager
        EditorGUILayout.BeginHorizontal();
        segmentationManager = EditorGUILayout.ObjectField("SegmentationManager:", segmentationManager, typeof(SegmentationManager), true) as SegmentationManager;
        if (GUILayout.Button("Find", GUILayout.Width(60)))
        {
            segmentationManager = FindObjectOfType<SegmentationManager>();
        }
        EditorGUILayout.EndHorizontal();
        
        // DeepLabPredictor
        EditorGUILayout.BeginHorizontal();
        deepLabPredictor = EditorGUILayout.ObjectField("DeepLabPredictor:", deepLabPredictor, typeof(EnhancedDeepLabPredictor), true) as EnhancedDeepLabPredictor;
        if (GUILayout.Button("Find", GUILayout.Width(60)))
        {
            deepLabPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        }
        EditorGUILayout.EndHorizontal();
        
        // MLManagerAdapter
        EditorGUILayout.BeginHorizontal();
        mlManagerAdapter = EditorGUILayout.ObjectField("MLManagerAdapter:", mlManagerAdapter, typeof(MLManagerAdapter), true) as MLManagerAdapter;
        if (GUILayout.Button("Find", GUILayout.Width(60)))
        {
            mlManagerAdapter = FindObjectOfType<MLManagerAdapter>();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        // Fix Options
        EditorGUILayout.LabelField("Fix Options", EditorStyles.boldLabel);
        
        fixSegmentationManager = EditorGUILayout.Toggle("Fix SegmentationManager", fixSegmentationManager);
        if (fixSegmentationManager && segmentationManager != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Will set:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Output Name: 'logits'", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Wall Class ID: 9", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Input Dimensions: 256x256", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        fixDeepLabPredictor = EditorGUILayout.Toggle("Fix DeepLabPredictor", fixDeepLabPredictor);
        if (fixDeepLabPredictor && deepLabPredictor != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Will set:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Use Standard Format Input: true", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Wall Class ID: 9", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Enable Downsampling: true (factor 2)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Processing Interval: 0.2", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        fixMLManagerAdapter = EditorGUILayout.Toggle("Fix MLManagerAdapter", fixMLManagerAdapter);
        if (fixMLManagerAdapter && mlManagerAdapter != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Will set:", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Processing Interval: 0.5", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("- Link to SegmentationManager", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        addModelConfigFixer = EditorGUILayout.Toggle("Add ModelConfigFixer", addModelConfigFixer);
        if (addModelConfigFixer && segmentationManager != null)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Will add ModelConfigFixer component to SegmentationManager", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("for runtime automatic configuration", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Find All Components"))
        {
            FindComponents();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space();
        
        GUI.backgroundColor = new Color(0.7f, 0.9f, 0.7f);
        if (GUILayout.Button("Apply Fixes", GUILayout.Height(40)))
        {
            ApplyFixes();
        }
        GUI.backgroundColor = Color.white;
        
        EditorGUILayout.Space();
        
        EditorGUILayout.EndScrollView();
    }
    
    private void FindComponents()
    {
        segmentationManager = FindObjectOfType<SegmentationManager>();
        deepLabPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        mlManagerAdapter = FindObjectOfType<MLManagerAdapter>();
        
        string message = "Found Components:\n";
        message += segmentationManager != null ? "✓ SegmentationManager\n" : "✗ SegmentationManager missing\n";
        message += deepLabPredictor != null ? "✓ EnhancedDeepLabPredictor\n" : "✗ EnhancedDeepLabPredictor missing\n";
        message += mlManagerAdapter != null ? "✓ MLManagerAdapter\n" : "✗ MLManagerAdapter missing\n";
        
        EditorUtility.DisplayDialog("Component Search Results", message, "OK");
    }
    
    private void ApplyFixes()
    {
        Undo.SetCurrentGroupName("ML Config Fixes");
        int undoGroupIndex = Undo.GetCurrentGroup();
        
        bool anyChanges = false;
        
        if (fixSegmentationManager && segmentationManager != null)
        {
            FixSegmentationManager();
            anyChanges = true;
        }
        
        if (fixDeepLabPredictor && deepLabPredictor != null)
        {
            FixDeepLabPredictor();
            anyChanges = true;
        }
        
        if (fixMLManagerAdapter && mlManagerAdapter != null)
        {
            FixMLManagerAdapter();
            anyChanges = true;
        }
        
        if (addModelConfigFixer && segmentationManager != null)
        {
            AddModelConfigFixer();
            anyChanges = true;
        }
        
        Undo.CollapseUndoOperations(undoGroupIndex);
        
        if (anyChanges)
        {
            EditorUtility.DisplayDialog("ML Config Fixed", 
                "Successfully applied ML configuration fixes.\n\nChanges will be immediately visible in the Inspector.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("No Changes", 
                "No ML components were fixed. Please check your component references and selected fix options.", "OK");
        }
    }
    
    private void FixSegmentationManager()
    {
        Debug.Log("Fixing SegmentationManager configuration...");
        
        Undo.RecordObject(segmentationManager, "Fix SegmentationManager");
        
        // Fix output name
        SetPrivateField(segmentationManager, "outputName", "logits");
        
        // Fix wall class ID
        SetPrivateField(segmentationManager, "wallClassId", 9);
        
        // Set input dimensions
        SetPrivateField(segmentationManager, "inputWidth", 256);
        SetPrivateField(segmentationManager, "inputHeight", 256);
        
        // Ensure debug mode is on during development
        SetPrivateField(segmentationManager, "debugMode", true);
        
        EditorUtility.SetDirty(segmentationManager);
    }
    
    private void FixDeepLabPredictor()
    {
        Debug.Log("Fixing EnhancedDeepLabPredictor configuration...");
        
        Undo.RecordObject(deepLabPredictor, "Fix DeepLabPredictor");
        
        // Configure the predictor
        // Comment out properties that don't exist in EnhancedDeepLabPredictor
        // deepLabPredictor.useStandardFormatInput = true;
        deepLabPredictor.WallClassId = 9;
        deepLabPredictor.enableDownsampling = true;
        deepLabPredictor.downsamplingFactor = 2;
        deepLabPredictor.minSegmentationInterval = 0.2f;
        deepLabPredictor.applyNoiseReduction = true;
        deepLabPredictor.applyWallFilling = true;
        
        EditorUtility.SetDirty(deepLabPredictor);
    }
    
    private void FixMLManagerAdapter()
    {
        Debug.Log("Fixing MLManagerAdapter configuration...");
        
        Undo.RecordObject(mlManagerAdapter, "Fix MLManagerAdapter");
        
        // Set processing interval
        SetPrivateField(mlManagerAdapter, "processingInterval", 0.5f);
        
        // Link with SegmentationManager if available
        if (segmentationManager != null)
        {
            SetFieldByNameOrProperty(mlManagerAdapter, "segmentationManager", segmentationManager);
        }
        
        EditorUtility.SetDirty(mlManagerAdapter);
    }
    
    private void AddModelConfigFixer()
    {
        if (segmentationManager == null) return;
        
        var existingFixer = segmentationManager.GetComponent<ModelConfigFixer>();
        if (existingFixer != null)
        {
            Debug.Log("ModelConfigFixer already exists on SegmentationManager");
            return;
        }
        
        Undo.RecordObject(segmentationManager.gameObject, "Add ModelConfigFixer");
        
        var modelFixer = Undo.AddComponent<ModelConfigFixer>(segmentationManager.gameObject);
        if (modelFixer != null)
        {
            // Link with SegmentationManager
            var segManagerField = modelFixer.GetType().GetField("segmentationManager", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
            if (segManagerField != null)
            {
                segManagerField.SetValue(modelFixer, segmentationManager);
            }
            else
            {
                // Try reflection to access the public field by name
                var reflectionField = modelFixer.GetType().GetField("segmentationManager", 
                    BindingFlags.Public | BindingFlags.Instance);
                    
                if (reflectionField != null)
                {
                    reflectionField.SetValue(modelFixer, segmentationManager);
                }
            }
            
            Debug.Log("Added ModelConfigFixer component to SegmentationManager GameObject");
            EditorUtility.SetDirty(segmentationManager.gameObject);
        }
    }
    
    private void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (field != null)
        {
            field.SetValue(target, value);
            Debug.Log($"Set {fieldName} = {value}");
        }
        else
        {
            Debug.LogWarning($"Field {fieldName} not found on {target.GetType().Name}");
        }
    }
    
    private void SetFieldByNameOrProperty(object target, string fieldName, object value)
    {
        // Try normal property
        PropertyInfo property = target.GetType().GetProperty(fieldName, 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value);
            Debug.Log($"Set property {fieldName} = {value}");
            return;
        }
        
        // Try public field
        FieldInfo publicField = target.GetType().GetField(fieldName, 
            BindingFlags.Public | BindingFlags.Instance);
            
        if (publicField != null)
        {
            publicField.SetValue(target, value);
            Debug.Log($"Set public field {fieldName} = {value}");
            return;
        }
        
        // Try private field
        FieldInfo privateField = target.GetType().GetField(fieldName, 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (privateField != null)
        {
            privateField.SetValue(target, value);
            Debug.Log($"Set private field {fieldName} = {value}");
            return;
        }
        
        Debug.LogWarning($"Could not find any way to set {fieldName} on {target.GetType().Name}");
    }
    
    [MenuItem("AR/Fix ML Configuration/Quick Fix All")]
    public static void QuickFixAll()
    {
        var window = CreateInstance<MLConfigFixerMenu>();
        window.FindComponents();
        window.ApplyFixes();
    }
} 