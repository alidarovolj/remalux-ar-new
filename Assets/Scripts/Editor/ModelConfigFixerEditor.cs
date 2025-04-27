using UnityEngine;
using UnityEditor;
using ML;

[CustomEditor(typeof(ModelConfigFixer))]
public class ModelConfigFixerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ModelConfigFixer fixer = (ModelConfigFixer)target;

        EditorGUILayout.Space();
        
        if (GUILayout.Button("Fix Model Configuration"))
        {
            // Call the method to fix the configuration
            fixer.FixModelConfiguration();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "This component automatically analyzes the model file and sets the correct input/output configuration in the SegmentationManager.\n\n" +
            "Click 'Fix Model Configuration' to update the settings based on the current model, or enable 'Fix On Awake' to do this automatically at runtime.", 
            MessageType.Info);
    }
} 