using System;
using UnityEngine;

/// <summary>
/// A simplified version of DeepLabPredictor that provides basic wall detection functionality.
/// </summary>
public class SimpleDeepLabPredictor : DeepLabPredictor
{
    [SerializeField] private new int wallClassId = 9;  // Updated default to 9
    
    public override int WallClassId 
    { 
        get => wallClassId;
        set
        {
            if (wallClassId != value)
            {
                wallClassId = value;
                OnWallClassIdChanged?.Invoke((byte)value);
            }
        }
    }
    
    public override event Action<byte> OnWallClassIdChanged;

    protected override void Start()
    {
        base.Start();
        Debug.Log($"SimpleDeepLabPredictor initialized with wall class ID: {wallClassId}");
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // Additional setup if needed
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        // Cleanup if needed
    }
} 