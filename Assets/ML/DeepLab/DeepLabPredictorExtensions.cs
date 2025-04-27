using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

namespace ML.DeepLab
{
    /// <summary>
    /// Extension methods and components for DeepLabPredictor
    /// </summary>
    public static class DeepLabPredictorExtensions
    {
        // Dictionary to store UnityEvent objects for each DeepLabPredictor instance
        private static Dictionary<int, UnityEvent> segmentationEvents = new Dictionary<int, UnityEvent>();
        
        /// <summary>
        /// Get or create a segmentation complete event for a DeepLabPredictor
        /// </summary>
        public static UnityEvent GetOnSegmentationComplete(this DeepLabPredictor predictor)
        {
            int instanceId = predictor.GetInstanceID();
            
            if (!segmentationEvents.TryGetValue(instanceId, out UnityEvent segmentationEvent))
            {
                segmentationEvent = new UnityEvent();
                segmentationEvents[instanceId] = segmentationEvent;
            }
            
            return segmentationEvent;
        }
        
        /// <summary>
        /// Invokes the segmentation complete event for a DeepLabPredictor
        /// </summary>
        public static void InvokeSegmentationComplete(this DeepLabPredictor predictor)
        {
            int instanceId = predictor.GetInstanceID();
            
            if (segmentationEvents.TryGetValue(instanceId, out UnityEvent segmentationEvent))
            {
                segmentationEvent.Invoke();
            }
        }
        
        /// <summary>
        /// Clean up resources when a DeepLabPredictor is destroyed
        /// </summary>
        public static void CleanupEvents(this DeepLabPredictor predictor)
        {
            int instanceId = predictor.GetInstanceID();
            
            if (segmentationEvents.ContainsKey(instanceId))
            {
                segmentationEvents.Remove(instanceId);
            }
        }
    }
    
    /// <summary>
    /// Component to add OnSegmentationComplete event to DeepLabPredictor
    /// </summary>
    [RequireComponent(typeof(DeepLabPredictor))]
    public class DeepLabPredictorEventAdapter : MonoBehaviour
    {
        private DeepLabPredictor _predictor;
        [SerializeField] private UnityEvent _onSegmentationComplete = new UnityEvent();
        
        /// <summary>
        /// Access to the OnSegmentationComplete event
        /// </summary>
        public UnityEvent OnSegmentationComplete => _onSegmentationComplete;
        
        private void Awake()
        {
            _predictor = GetComponent<DeepLabPredictor>();
        }
        
        private void OnDestroy()
        {
            if (_predictor != null)
            {
                _predictor.CleanupEvents();
            }
        }
        
        /// <summary>
        /// Invoke the segmentation complete event
        /// </summary>
        public void InvokeSegmentationComplete()
        {
            _onSegmentationComplete?.Invoke();
        }
    }
} 