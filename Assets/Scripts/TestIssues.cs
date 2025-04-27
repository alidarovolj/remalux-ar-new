using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestIssues : MonoBehaviour
{
    [SerializeField] private RemaluxARWallSetup remaluxARWallSetup;
    [SerializeField] private RemaluxWallDetectionSystem remaluxWallDetectionSystem;
    [SerializeField] private ARWallDetectionSetup arWallDetectionSetup;
    
    void Start()
    {
        Debug.Log("Testing class references...");
        
        if (remaluxARWallSetup != null)
            Debug.Log("RemaluxARWallSetup found!");
            
        if (remaluxWallDetectionSystem != null)
            Debug.Log("RemaluxWallDetectionSystem found!");
            
        if (arWallDetectionSetup != null)
            Debug.Log("ARWallDetectionSetup found!");
    }
} 