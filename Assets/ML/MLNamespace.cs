using UnityEngine;
using System;
using System.Collections.Generic;

namespace ML
{
    // This is a namespace definition file to fix missing ML namespace errors
    
    // Common interfaces and base types for ML components
    public interface IMLModel
    {
        bool Initialize();
        void Process();
        void Shutdown();
    }
    
    // Basic ML component types that might be referenced
    public abstract class MLComponent : MonoBehaviour
    {
        public virtual bool IsInitialized { get; protected set; }
        
        public virtual void Initialize() 
        {
            IsInitialized = true;
        }
        
        public virtual void Shutdown()
        {
            IsInitialized = false;
        }
    }
    
    // Basic data structures that might be used across ML components
    [Serializable]
    public struct MLConfig
    {
        public string modelName;
        public int inputWidth;
        public int inputHeight;
        public int classCount;
    }
    
    // Common enums
    public enum ModelType
    {
        Segmentation,
        Detection,
        Custom
    }
    
    // Segmentation specific namespace
    public class Segmentation
    {
        public enum ClassType
        {
            Background = 0,
            Wall = 1,
            Floor = 2,
            Ceiling = 3,
            Window = 4,
            Door = 5,
            Furniture = 6,
            Other = 7
        }
    }
} 