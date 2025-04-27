using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Helper script to migrate from RemaluxARWallSetup to RemaluxWallDetectionSystem
/// This script helps find and fix references in the scene
/// </summary>
public class RemaluxWallMigrationHelper : MonoBehaviour
{
    // Add this component to any GameObject in your scene
    // It will automatically find and migrate components at runtime
    
    private void Awake()
    {
        Debug.Log("RemaluxWallMigrationHelper: Starting to find and fix references");
        
        // Find all components that might reference the old class
        var wallAnchorConnectors = FindObjectsOfType<WallAnchorConnector>();
        
        foreach (var connector in wallAnchorConnectors)
        {
            // Check if this connector is missing its wall setup reference
            MonoBehaviour wallSetup = null;
            
            // Try to find RemaluxWallDetectionSystem component first
            wallSetup = connector.GetComponent<RemaluxWallDetectionSystem>();
            
            // If not found, try to find RemaluxARWallSetup component
            if (wallSetup == null)
            {
                wallSetup = connector.GetComponent<RemaluxARWallSetup>();
            }
            
            if (wallSetup != null)
            {
                Debug.Log($"RemaluxWallMigrationHelper: Fixing reference in {connector.gameObject.name}");
                // Update the WallSetup reference using reflection (this is a bit hacky but works for runtime fixes)
                var field = connector.GetType().GetField("_wallSetup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(connector, wallSetup);
                    Debug.Log($"RemaluxWallMigrationHelper: Fixed reference in {connector.gameObject.name}");
                }
            }
        }
        
        Debug.Log("RemaluxWallMigrationHelper: Migration complete");
        
        // The component can be removed after it has done its job
        Destroy(this);
    }
    
#if UNITY_EDITOR
    // Menu item to help find references in the project
    [UnityEditor.MenuItem("Tools/Find RemaluxARWallSetup References")]
    public static void FindReferences()
    {
        Debug.Log("Searching for RemaluxARWallSetup references in the current scene...");
        
        // This part will only run in the editor
        var wallAnchorConnectors = Object.FindObjectsOfType<WallAnchorConnector>();
        
        foreach (var connector in wallAnchorConnectors)
        {
            Debug.Log($"Found potential reference in: {connector.gameObject.name}");
        }
        
        Debug.Log("Remember to update any script references in ARSceneSetup.cs as well.");
    }
#endif
} 