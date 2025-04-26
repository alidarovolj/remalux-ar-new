using UnityEngine;
using UnityEditor;
using System.Reflection;
using ML;

[CustomEditor(typeof(SegmentationManager))]
public class SegmentationManagerInspector : Editor
{
    private SerializedProperty modelAssetProp;
    private SerializedProperty inputNameProp;
    private SerializedProperty outputNameProp;
    private SerializedProperty isModelNHWCFormatProp;
    private SerializedProperty inputWidthProp;
    private SerializedProperty inputHeightProp;
    private SerializedProperty inputChannelsProp;
    private SerializedProperty segmentationClassCountProp;
    private SerializedProperty wallClassIdProp;
    private SerializedProperty classificationThresholdProp;
    private SerializedProperty processingIntervalProp;
    private SerializedProperty debugModeProp;

    private bool showTroubleshooting = false;

    private void OnEnable()
    {
        modelAssetProp = serializedObject.FindProperty("_modelAsset");
        inputNameProp = serializedObject.FindProperty("inputName");
        outputNameProp = serializedObject.FindProperty("outputName");
        isModelNHWCFormatProp = serializedObject.FindProperty("isModelNHWCFormat");
        inputWidthProp = serializedObject.FindProperty("inputWidth");
        inputHeightProp = serializedObject.FindProperty("inputHeight");
        inputChannelsProp = serializedObject.FindProperty("inputChannels");
        segmentationClassCountProp = serializedObject.FindProperty("segmentationClassCount");
        wallClassIdProp = serializedObject.FindProperty("wallClassId");
        classificationThresholdProp = serializedObject.FindProperty("classificationThreshold");
        processingIntervalProp = serializedObject.FindProperty("processingInterval");
        debugModeProp = serializedObject.FindProperty("debugMode");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(modelAssetProp);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Input Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(inputNameProp);
        EditorGUILayout.PropertyField(isModelNHWCFormatProp);
        EditorGUILayout.PropertyField(inputWidthProp);
        EditorGUILayout.PropertyField(inputHeightProp);
        EditorGUILayout.PropertyField(inputChannelsProp);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output Configuration", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(outputNameProp);
        EditorGUILayout.PropertyField(segmentationClassCountProp);
        EditorGUILayout.PropertyField(wallClassIdProp);
        EditorGUILayout.PropertyField(classificationThresholdProp);
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Processing Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(processingIntervalProp);
        EditorGUILayout.PropertyField(debugModeProp);

        serializedObject.ApplyModifiedProperties();

        SegmentationManager manager = (SegmentationManager)target;

        // Add Troubleshooting section
        EditorGUILayout.Space();
        showTroubleshooting = EditorGUILayout.Foldout(showTroubleshooting, "Troubleshooting Tools", true, EditorStyles.foldoutHeader);
        
        if (showTroubleshooting)
        {
            EditorGUILayout.HelpBox("Use these tools to fix common issues with model execution.", MessageType.Info);
            
            EditorGUILayout.Space();
            
            // Add ModelConfigFixer if not already present
            EditorGUILayout.BeginVertical("box");
            
            if (GUILayout.Button("Add ModelConfigFixer")) 
            {
                var fixer = manager.gameObject.GetComponent<ModelConfigFixer>() ?? manager.gameObject.AddComponent<ModelConfigFixer>();
                
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
                
                EditorUtility.SetDirty(fixer);
            }
            
            if (GUILayout.Button("Set Output Name to 'logits'"))
            {
                serializedObject.FindProperty("_outputName").stringValue = "logits";
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
            
            if (GUILayout.Button("Set Wall Class ID to 9"))
            {
                serializedObject.FindProperty("_wallClassId").intValue = 9;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Set Input 224x224"))
            {
                serializedObject.FindProperty("_inputWidth").intValue = 224;
                serializedObject.FindProperty("_inputHeight").intValue = 224;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
            
            if (GUILayout.Button("Set Input 256x256"))
            {
                serializedObject.FindProperty("_inputWidth").intValue = 256;
                serializedObject.FindProperty("_inputHeight").intValue = 256;
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);
            }
            EditorGUILayout.EndHorizontal();
            
            if (GUILayout.Button("Log Tensor Dimensions"))
            {
                // Call to LogTensorDimensions using reflection (private method)
                var logDimensionsMethod = manager.GetType().GetMethod("LogTensorDimensions", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                if (logDimensionsMethod != null)
                {
                    logDimensionsMethod.Invoke(manager, null);
                }
                else
                {
                    Debug.LogError("Could not find LogTensorDimensions method");
                }
            }
            
            if (GUILayout.Button("Log Available Output Tensors"))
            {
                // Call the public LogAvailableOutputs method
                manager.LogAvailableOutputs();
            }
            
            if (GUILayout.Button("Reinitialize Model"))
            {
                // Call to InitializeModel using reflection
                var initModelMethod = manager.GetType().GetMethod("InitializeModel", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                if (initModelMethod != null)
                {
                    initModelMethod.Invoke(manager, null);
                }
                else
                {
                    Debug.LogError("Could not find InitializeModel method");
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        // Add ModelConfigFixer button at the bottom always visible
        EditorGUILayout.Space();
        if (GUILayout.Button("Apply All Model Fixes"))
        {
            ModelConfigFixer fixer = manager.gameObject.GetComponent<ModelConfigFixer>();
            if (fixer == null)
            {
                fixer = manager.gameObject.AddComponent<ModelConfigFixer>();
                
                // Set the segmentationManager reference using reflection
                var segmentationManagerField = fixer.GetType().GetField("segmentationManager", 
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    
                if (segmentationManagerField != null)
                {
                    segmentationManagerField.SetValue(fixer, manager);
                }
                else
                {
                    Debug.LogError("Could not find segmentationManager field in ModelConfigFixer");
                }
            }
            
            // Call the fix method
            fixer.FixModelConfiguration();
            Debug.Log("Applied all model fixes");
        }
    }
} 