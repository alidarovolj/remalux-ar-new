using UnityEngine;
using System.Collections.Generic;

public class WallStabilizer : MonoBehaviour
{
    private Dictionary<Vector3, float> wallConfidence = new Dictionary<Vector3, float>();
    private const float CONFIDENCE_THRESHOLD = 0.8f;
    private const float CONFIDENCE_DECAY = 0.05f;
    private const float POSITION_THRESHOLD = 0.1f; // Distance threshold for considering positions the same
    
    public void UpdateWallConfidence(Vector3 position)
    {
        // Find closest existing wall position
        Vector3 closestPos = Vector3.zero;
        float minDistance = float.MaxValue;
        
        foreach (var pos in wallConfidence.Keys)
        {
            float distance = Vector3.Distance(pos, position);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestPos = pos;
            }
        }
        
        // Update existing wall or add new one
        if (minDistance < POSITION_THRESHOLD && wallConfidence.ContainsKey(closestPos))
        {
            wallConfidence[closestPos] = Mathf.Min(wallConfidence[closestPos] + 0.1f, 1.0f);
        }
        else
        {
            wallConfidence.Add(position, 0.1f);
        }
    }
    
    public bool IsStableWall(Vector3 position)
    {
        foreach (var pair in wallConfidence)
        {
            if (Vector3.Distance(pair.Key, position) < POSITION_THRESHOLD)
            {
                return pair.Value >= CONFIDENCE_THRESHOLD;
            }
        }
        return false;
    }
    
    private void Update()
    {
        var positions = new List<Vector3>(wallConfidence.Keys);
        foreach (var pos in positions)
        {
            wallConfidence[pos] = Mathf.Max(0, wallConfidence[pos] - CONFIDENCE_DECAY * Time.deltaTime);
            if (wallConfidence[pos] <= 0)
            {
                wallConfidence.Remove(pos);
            }
        }
    }
    
    public void Reset()
    {
        wallConfidence.Clear();
    }
} 