using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Этот скрипт исправляет распространенные проблемы AR/ML во время выполнения
/// </summary>
public class ARRuntimeFixer : MonoBehaviour
{
    [SerializeField] private bool fixOnAwake = true;
    [SerializeField] private bool fixOnStart = true;
    [SerializeField] private bool fixOnFirstFrame = true;
    [SerializeField] private float delayBeforeFix = 0.5f;
    
    private bool _hasStarted = false;
    private bool _hasFixedOnFirstFrame = false;
    
    void Awake()
    {
        if (fixOnAwake)
        {
            Debug.Log("ARRuntimeFixer: Fixing AR problems on Awake");
            FixAllProblems();
        }
    }
    
    void Start()
    {
        _hasStarted = true;
        
        if (fixOnStart)
        {
            Debug.Log("ARRuntimeFixer: Fixing AR problems on Start");
            FixAllProblems();
        }
        
        // Запускаем корутину с задержкой
        StartCoroutine(FixWithDelay(delayBeforeFix));
    }
    
    void Update()
    {
        if (fixOnFirstFrame && !_hasFixedOnFirstFrame && _hasStarted)
        {
            Debug.Log("ARRuntimeFixer: Fixing AR problems on first Update frame");
            FixAllProblems();
            _hasFixedOnFirstFrame = true;
        }
    }
    
    private IEnumerator FixWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Debug.Log($"ARRuntimeFixer: Fixing AR problems after {delay} seconds delay");
        FixAllProblems();
    }
    
    /// <summary>
    /// Исправляет все обнаруженные проблемы AR системы
    /// </summary>
    public void FixAllProblems()
    {
        FixDuplicateARSessions();
        FixXROriginCameraReference();
        FixARMLControllerReferences();
    }
    
    /// <summary>
    /// Удаляет дублирующиеся ARSession компоненты
    /// </summary>
    private void FixDuplicateARSessions()
    {
        ARSession[] sessions = FindObjectsOfType<ARSession>();
        
        if (sessions.Length <= 1)
        {
            Debug.Log("ARRuntimeFixer: No duplicate ARSessions found");
            return;
        }
        
        Debug.LogWarning($"ARRuntimeFixer: Found {sessions.Length} ARSessions, removing duplicates");
        
        // Оставляем только первую сессию
        ARSession primarySession = sessions[0];
        
        for (int i = 1; i < sessions.Length; i++)
        {
            if (sessions[i] != primarySession)
            {
                Debug.Log($"ARRuntimeFixer: Disabling duplicate ARSession on {sessions[i].gameObject.name}");
                sessions[i].enabled = false;
            }
        }
    }
    
    /// <summary>
    /// Исправляет ссылку на камеру в XROrigin
    /// </summary>
    private void FixXROriginCameraReference()
    {
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogWarning("ARRuntimeFixer: No XROrigin found in scene");
            return;
        }
        
        // Проверяем, есть ли ссылка на камеру
        if (xrOrigin.Camera != null)
        {
            Debug.Log("ARRuntimeFixer: XROrigin already has camera reference");
            return;
        }
        
        Debug.LogWarning("ARRuntimeFixer: XROrigin has no camera reference, attempting to fix");
        
        // Проверяем Camera Offset
        Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            Debug.Log("ARRuntimeFixer: Creating Camera Offset under XROrigin");
            GameObject offsetObj = new GameObject("Camera Offset");
            offsetObj.transform.SetParent(xrOrigin.transform, false);
            cameraOffset = offsetObj.transform;
        }
        
        // Ищем AR Camera
        Camera arCamera = cameraOffset.GetComponentInChildren<Camera>();
        if (arCamera == null)
        {
            Debug.Log("ARRuntimeFixer: Creating AR Camera under Camera Offset");
            GameObject cameraObj = new GameObject("AR Camera");
            cameraObj.transform.SetParent(cameraOffset, false);
            
            arCamera = cameraObj.AddComponent<Camera>();
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.1f;
            arCamera.farClipPlane = 20f;
            
            if (cameraObj.GetComponent<ARCameraManager>() == null)
            {
                cameraObj.AddComponent<ARCameraManager>();
            }
            
            if (cameraObj.GetComponent<ARCameraBackground>() == null)
            {
                cameraObj.AddComponent<ARCameraBackground>();
            }
            
            // Добавляем TrackedPoseDriver если нужно
            if (cameraObj.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>() == null)
            {
                var tpd = cameraObj.AddComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
                tpd.SetPoseSource(UnityEngine.SpatialTracking.TrackedPoseDriver.DeviceType.GenericXRDevice, 
                                  UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose.Center);
                tpd.trackingType = UnityEngine.SpatialTracking.TrackedPoseDriver.TrackingType.RotationAndPosition;
                tpd.updateType = UnityEngine.SpatialTracking.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }
        }
        
        // Устанавливаем ссылку на камеру
        xrOrigin.Camera = arCamera;
        
        // Устанавливаем ссылку на Camera Offset
        var field = typeof(XROrigin).GetField("m_CameraFloorOffsetObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(xrOrigin, cameraOffset.gameObject);
        }
        
        Debug.Log("ARRuntimeFixer: Fixed XROrigin camera reference");
    }
    
    /// <summary>
    /// Исправляет ссылки в ARMLController
    /// </summary>
    private void FixARMLControllerReferences()
    {
        ARMLController armlController = FindObjectOfType<ARMLController>();
        if (armlController == null)
        {
            Debug.LogWarning("ARRuntimeFixer: No ARMLController found in scene");
            return;
        }
        
        Debug.Log("ARRuntimeFixer: Fixing ARMLController references");
        
        // Получаем компоненты
        SegmentationManager segManager = FindObjectOfType<SegmentationManager>();
        MaskProcessor maskProcessor = FindObjectOfType<MaskProcessor>();
        Camera arCamera = FindObjectOfType<XROrigin>()?.Camera;
        ARCameraManager cameraManager = arCamera?.GetComponent<ARCameraManager>();
        ARSession arSession = FindObjectOfType<ARSession>();
        
        // Находим EnhancedDeepLabPredictor
        Component enhancedPredictor = null;
        
        // Проверяем несколько возможных имен типов
        System.Type[] possibleTypes = new System.Type[] {
            System.Type.GetType("EnhancedDeepLabPredictor"),
            System.Type.GetType("EnhancedDeepLabPredictor, Assembly-CSharp"),
            System.Type.GetType("ML.EnhancedDeepLabPredictor, Assembly-CSharp")
        };
        
        foreach (var type in possibleTypes)
        {
            if (type != null)
            {
                enhancedPredictor = FindObjectOfType(type) as Component;
                if (enhancedPredictor != null) break;
            }
        }
        
        // Устанавливаем ссылки в ARMLController через рефлексию
        System.Type armlType = typeof(ARMLController);
        
        // Устанавливаем SegmentationManager
        if (segManager != null)
        {
            var field = armlType.GetField("segmentationManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, segManager);
                Debug.Log("ARRuntimeFixer: Set segmentationManager in ARMLController");
            }
        }
        
        // Устанавливаем MaskProcessor
        if (maskProcessor != null)
        {
            var field = armlType.GetField("maskProcessor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, maskProcessor);
                Debug.Log("ARRuntimeFixer: Set maskProcessor in ARMLController");
            }
        }
        
        // Устанавливаем Camera
        if (arCamera != null)
        {
            var field = armlType.GetField("arCamera", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, arCamera);
                Debug.Log("ARRuntimeFixer: Set arCamera in ARMLController");
            }
        }
        
        // Устанавливаем ARCameraManager
        if (cameraManager != null)
        {
            var field = armlType.GetField("cameraManager", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, cameraManager);
                Debug.Log("ARRuntimeFixer: Set cameraManager in ARMLController");
            }
        }
        
        // Устанавливаем ARSession
        if (arSession != null)
        {
            var field = armlType.GetField("arSession", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, arSession);
                Debug.Log("ARRuntimeFixer: Set arSession in ARMLController");
            }
        }
        
        // Устанавливаем EnhancedDeepLabPredictor
        if (enhancedPredictor != null)
        {
            var field = armlType.GetField("enhancedPredictor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(armlController, enhancedPredictor);
                Debug.Log("ARRuntimeFixer: Set enhancedPredictor in ARMLController");
            }
        }
        
        // Обеспечиваем, чтобы ARMLController был включен
        if (!armlController.enabled)
        {
            armlController.enabled = true;
            Debug.Log("ARRuntimeFixer: Enabled ARMLController");
        }
    }
} 