using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Extension methods for WallMeshRenderer to support WallOptimizer functionality
/// </summary>
public static class WallMeshRendererExtensions
{
    // Dictionary to store wall normals
    private static Dictionary<int, Vector3> wallNormals = new Dictionary<int, Vector3>();
    
    /// <summary>
    /// Gets the normal direction of a wall
    /// </summary>
    public static Vector3 GetWallNormal(this WallMeshRenderer renderer, int wallIndex)
    {
        if (wallNormals.TryGetValue(wallIndex, out Vector3 normal))
        {
            return normal;
        }
        
        // Default forward-facing normal
        return Vector3.forward;
    }
    
    /// <summary>
    /// Updates an existing wall mesh with new parameters
    /// </summary>
    public static void UpdateWallMesh(this WallMeshRenderer renderer, int wallIndex, Vector3 position, Vector3 size, Quaternion rotation)
    {
        // Find the wall GameObject by index
        if (wallIndex < 0 || wallIndex >= renderer.transform.childCount)
            return;
            
        Transform child = renderer.transform.GetChild(wallIndex);
        if (child == null)
            return;
            
        // Update transform
        child.position = position;
        child.rotation = rotation;
        child.localScale = size;
        
        // Store the updated normal
        wallNormals[wallIndex] = rotation * Vector3.forward;
        
        if (renderer.ShowDebugInfo)
        {
            Debug.Log($"WallMeshRenderer: Updated wall at index {wallIndex}, position: {position}, size: {size}");
        }
    }
    
    /// <summary>
    /// Creates a new wall mesh with the given parameters
    /// </summary>
    public static int CreateWallMesh(this WallMeshRenderer renderer, Vector3 position, Vector3 size, Quaternion rotation)
    {
        // Create a new wall GameObject
        GameObject wallObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallObj.name = "Wall_" + renderer.transform.childCount;
        
        // Set transform
        wallObj.transform.position = position;
        wallObj.transform.rotation = rotation;
        wallObj.transform.localScale = size;
        wallObj.transform.SetParent(renderer.transform);
        
        // Apply wall material if available
        MeshRenderer meshRenderer = wallObj.GetComponent<MeshRenderer>();
        if (meshRenderer != null && renderer.WallMaterial != null)
        {
            meshRenderer.material = renderer.WallMaterial;
        }
        
        // Store the normal
        int wallIndex = renderer.transform.childCount - 1;
        wallNormals[wallIndex] = rotation * Vector3.forward;
        
        if (renderer.ShowDebugInfo)
        {
            Debug.Log($"WallMeshRenderer: Created new wall at index {wallIndex}, position: {position}, size: {size}");
        }
        
        return wallIndex;
    }
    
    /// <summary>
    /// Removes a wall mesh at the specified index
    /// </summary>
    public static void RemoveWallMesh(this WallMeshRenderer renderer, int wallIndex)
    {
        if (wallIndex < 0 || wallIndex >= renderer.transform.childCount)
            return;
            
        Transform child = renderer.transform.GetChild(wallIndex);
        if (child == null)
            return;
            
        // Remove normal from dictionary
        wallNormals.Remove(wallIndex);
        
        // Destroy the wall GameObject
        Object.Destroy(child.gameObject);
        
        if (renderer.ShowDebugInfo)
        {
            Debug.Log($"WallMeshRenderer: Removed wall at index {wallIndex}");
        }
    }
} 