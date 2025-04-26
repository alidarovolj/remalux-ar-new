using UnityEngine;
using UnityEditor;

/// <summary>
/// README script with instructions for using the native AR camera background display
/// </summary>
public class ARCameraDisplayREADME : EditorWindow
{
    private Vector2 scrollPos;

    [MenuItem("AR/Camera Display Help")]
    public static void ShowWindow()
    {
        ARCameraDisplayREADME window = GetWindow<ARCameraDisplayREADME>("AR Camera Display Help");
        window.minSize = new Vector2(450, 400);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Native AR Camera Background Setup Guide", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.HelpBox(
            "This guide explains how to set up AR camera display properly without using a RawImage.",
            MessageType.Info);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Available Tools", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        if (GUILayout.Button("1. Fix Camera Display (Existing Scene)"))
        {
            ARCameraSetupFix.FixARCameraDisplay();
            Close();
        }

        EditorGUILayout.LabelField("Fixes your existing scene to use the native camera background approach.", EditorStyles.miniLabel);

        EditorGUILayout.Space(10);
        if (GUILayout.Button("2. Create New AR Scene (With Native Camera)"))
        {
            ARSceneSetupBasicFix.SetupARSceneWithNativeCamera();
            Close();
        }

        EditorGUILayout.LabelField("Creates a new AR scene with proper camera setup from scratch.", EditorStyles.miniLabel);

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("Step-By-Step Manual Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("If you prefer to set up everything manually, here's how to do it:", EditorStyles.miniLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("1. Remove AR Display RawImage", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Find the 'AR Display' RawImage in your UI Canvas and delete it.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("2. Add Components to AR Camera", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("On your AR Camera (in XR Origin > Camera Offset > AR Camera), add:", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("  • ARCameraManager component", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("  • ARCameraBackground component", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("3. Change Canvas Render Mode", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Update your UI Canvas:", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("  • Render Mode: Screen Space - Camera", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("  • Render Camera: Assign your AR Camera", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("  • Plane Distance: Set to 1", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(5);

        EditorGUILayout.LabelField("4. Universal Render Pipeline Settings", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("If using URP, open Project Settings > Graphics > Scriptable Render Pipeline Settings.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.LabelField("Enable the 'Opaque Texture' option so your UI can draw on top of the AR background.", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Using this approach avoids the need to manually assign materials to RawImage components " +
            "and provides better performance by letting AR Foundation handle the camera feed rendering natively.",
            MessageType.Info);

        EditorGUILayout.EndScrollView();
    }
} 