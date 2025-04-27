using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestClassReferences : MonoBehaviour
{
    [SerializeField] private RemaluxARWallSetup remaluxARWallSetup;
    [SerializeField] private RemaluxWallDetectionSystem remaluxWallDetectionSystem;
    [SerializeField] private ARWallDetectionSetup arWallDetectionSetup;
    [SerializeField] private ARAnchorSetup arAnchorSetup;
    
    void Start()
    {
        Debug.Log("Testing class references...");
        
        if (remaluxARWallSetup != null)
            Debug.Log("RemaluxARWallSetup found!");
            
        if (remaluxWallDetectionSystem != null)
            Debug.Log("RemaluxWallDetectionSystem found!");
            
        if (arWallDetectionSetup != null)
            Debug.Log("ARWallDetectionSetup found!");
            
        if (arAnchorSetup != null)
            Debug.Log("ARAnchorSetup found!");
    }
} 