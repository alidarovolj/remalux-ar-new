using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestScript : MonoBehaviour
{
    [SerializeField] private RemaluxARWallSetup _arWallSetup;
    [SerializeField] private RemaluxWallDetectionSystem _wallDetectionSystem;
    
    private void Start()
    {
        Debug.Log("Test script started. Checking for wall detection systems...");
        
        if (_arWallSetup != null)
        {
            Debug.Log("Found RemaluxARWallSetup: " + _arWallSetup.name);
        }
        
        if (_wallDetectionSystem != null)
        {
            Debug.Log("Found RemaluxWallDetectionSystem: " + _wallDetectionSystem.name);
        }
        
        if (_arWallSetup == null && _wallDetectionSystem == null)
        {
            // Try to find them
            _arWallSetup = FindObjectOfType<RemaluxARWallSetup>();
            _wallDetectionSystem = FindObjectOfType<RemaluxWallDetectionSystem>();
            
            if (_arWallSetup != null)
            {
                Debug.Log("Found RemaluxARWallSetup through FindObjectOfType: " + _arWallSetup.name);
            }
            
            if (_wallDetectionSystem != null)
            {
                Debug.Log("Found RemaluxWallDetectionSystem through FindObjectOfType: " + _wallDetectionSystem.name);
            }
        }
    }
} 