using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Automatically fixes duplicate ARSession objects in the scene
/// </summary>
public class ARSessionFixer : MonoBehaviour
{
    public bool fixOnStart = true;
    public bool logResults = true;

    private void Start()
    {
        if (fixOnStart)
        {
            FixDuplicateARSessions();
        }
    }

    /// <summary>
    /// Fixes duplicate ARSession objects by keeping only one active
    /// </summary>
    public void FixDuplicateARSessions()
    {
        // Find all ARSession components in the scene
        ARSession[] sessions = FindObjectsOfType<ARSession>();
        
        if (sessions.Length <= 1)
        {
            if (logResults)
            {
                Debug.Log("ARSessionFixer: No duplicate ARSession objects found.");
            }
            return;
        }
        
        if (logResults)
        {
            Debug.LogWarning($"ARSessionFixer: Found {sessions.Length} ARSession objects. Will keep only one active.");
        }
        
        // Keep the first session, disable all others
        for (int i = 1; i < sessions.Length; i++)
        {
            if (logResults)
            {
                Debug.Log($"ARSessionFixer: Disabling duplicate ARSession on {sessions[i].gameObject.name}");
            }
            sessions[i].enabled = false;
        }
        
        // Make sure the first session is enabled
        sessions[0].enabled = true;
        
        if (logResults)
        {
            Debug.Log($"ARSessionFixer: Kept ARSession on {sessions[0].gameObject.name} active");
        }
    }
} 