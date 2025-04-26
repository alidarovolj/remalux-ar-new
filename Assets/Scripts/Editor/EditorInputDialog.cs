using UnityEngine;
using UnityEditor;
using System;

/// <summary>
/// Provides a simple input dialog for the Unity Editor.
/// </summary>
public class EditorInputDialog : EditorWindow
{
    public static string Show(string title, string message, string defaultText)
    {
        EditorInputDialog window = CreateInstance<EditorInputDialog>();
        window.titleContent = new GUIContent(title);
        window._message = message;
        window._inputText = defaultText;
        window.ShowModal();
        return window._result;
    }

    private string _message = "";
    private string _inputText = "";
    private string _result = "";
    private bool _canceled = false;

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);
        
        GUI.SetNextControlName("InputField");
        _inputText = EditorGUILayout.TextField(_inputText);
        EditorGUI.FocusTextInControl("InputField");
        
        GUILayout.Space(10);
        
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        if (GUILayout.Button("Cancel", GUILayout.Width(100)))
        {
            _canceled = true;
            _result = "";
            Close();
        }
        
        if (GUILayout.Button("OK", GUILayout.Width(100)) || 
            (Event.current.keyCode == KeyCode.Return && Event.current.type == EventType.KeyDown))
        {
            _result = _inputText;
            Close();
        }
        
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        // Set window size based on content
        Rect position = this.position;
        position.width = 400;
        position.height = 150;
        this.position = position;
    }
} 