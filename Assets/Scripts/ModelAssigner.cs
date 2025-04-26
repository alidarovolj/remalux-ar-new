using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using Unity.Barracuda;

/// <summary>
/// Автоматически назначает модель для SegmentationManager при запуске
/// </summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(SegmentationManager))]
public class ModelAssigner : MonoBehaviour
{
    [SerializeField] private string modelResourcePath = "model";
    [SerializeField] private bool loadOnAwake = true;
    [SerializeField] private bool debugLogging = true;
    public UnityEvent onModelAssigned;

    private SegmentationManager segmentationManager;

    void Awake()
    {
        segmentationManager = GetComponent<SegmentationManager>();
        if (segmentationManager == null)
        {
            Debug.LogError("SegmentationManager component not found on the same GameObject.");
            return;
        }

        if (segmentationManager.ModelAsset == null)
        {
            Debug.Log("Model is not assigned in SegmentationManager. Attempting to load from Resources...");
            LoadModelFromResources();
        }
        else
        {
            Debug.Log("Model is already assigned in SegmentationManager.");
        }
    }

    private void LoadModelFromResources()
    {
        try
        {
            // First try to load as NNModel (pre-created asset)
            NNModel model = Resources.Load<NNModel>(modelResourcePath);
            
            if (model != null)
            {
                Debug.Log("Successfully loaded NNModel from Resources: " + modelResourcePath);
                segmentationManager.ModelAsset = model;
                
                // Re-initialize the model if SegmentationManager has already attempted initialization
                if (segmentationManager.isActiveAndEnabled)
                {
                    Debug.Log("Re-initializing SegmentationManager with newly assigned model.");
                    segmentationManager.InitializeModel();
                }
                
                if (onModelAssigned != null)
                    onModelAssigned.Invoke();
            }
            else
            {
                // If NNModel not found, try loading raw ONNX file and create NNModel at runtime
                TextAsset onnxFile = Resources.Load<TextAsset>(modelResourcePath);
                if (onnxFile != null)
                {
                    Debug.Log("Found ONNX file in Resources. Creating NNModel at runtime.");
                    
                    // Create NNModel from ONNX bytes
                    model = ScriptableObject.CreateInstance<NNModel>();
                    model.modelData = new NNModelData();
                    model.modelData.Value = onnxFile.bytes;
                    
                    segmentationManager.ModelAsset = model;
                    
                    if (segmentationManager.isActiveAndEnabled)
                    {
                        Debug.Log("Re-initializing SegmentationManager with created NNModel from ONNX.");
                        segmentationManager.InitializeModel();
                    }
                    
                    if (onModelAssigned != null)
                        onModelAssigned.Invoke();
                }
                else
                {
                    Debug.LogError("Failed to load model from Resources. Path: " + modelResourcePath);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Error loading model from Resources: " + e.Message);
        }
    }
} 