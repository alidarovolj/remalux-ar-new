using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using Unity.XR.CoreUtils;

/// <summary>
/// Enhanced version of ARMLController with improvements and fixes
/// </summary>
[RequireComponent(typeof(ARMLController))]
public class EnhancedARMLController : MonoBehaviour
{
    [SerializeField] public ARSession arSession;
    
    [Header("Fix Settings")]
    [Tooltip("Apply fixes automatically on start")]
    public bool fixOnStart = true;
    
    [Tooltip("Delay before applying fixes (seconds)")]
    public float fixDelay = 1.5f;
    
    [Tooltip("Enable debug logging")]
    public bool enableDebugLogs = true;
    
    private ARMLController controller;
    private ARSessionHelper sessionHelper;
    private bool hasAttemptedFix = false;
    
    private void Awake()
    {
        controller = GetComponent<ARMLController>();
        sessionHelper = FindFirstObjectByType<ARSessionHelper>(FindObjectsInactive.Include);
        
        // Отключаем автозапуск в ARMLController
        var autoStartField = controller.GetType().GetField("autoStartOnLoad", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (autoStartField != null)
        {
            autoStartField.SetValue(controller, false);
        }
        
        // Проверяем, инициализирована ли сессия AR
        if (arSession == null)
        {
            arSession = FindFirstObjectByType<ARSession>();
            if (arSession == null)
            {
                if (enableDebugLogs)
                    Debug.LogError("EnhancedARMLController: ARSession not found in the scene!");
            }
        }
    }
    
    private void Start()
    {
        if (fixOnStart)
        {
            StartCoroutine(InitializeARSession());
        }
    }
    
    private IEnumerator InitializeARSession()
    {
        // Wait for fixDelay
        yield return new WaitForSeconds(fixDelay);
        
        // Проверяем состояние AR Session
        if (enableDebugLogs)
            Debug.Log($"EnhancedARMLController: Current AR Session state: {ARSession.state}");
        
        // Если сессия еще не инициализирована, ждем инициализации
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
        {
            yield return ARSession.CheckAvailability();
        }
        
        // Если устройство не поддерживает AR
        if (ARSession.state == ARSessionState.Unsupported)
        {
            Debug.LogError("EnhancedARMLController: AR is not supported on this device!");
            yield break;
        }
        
        // Включаем AR Session
        if (arSession != null)
        {
            arSession.enabled = true;
            if (enableDebugLogs)
                Debug.Log("EnhancedARMLController: AR Session enabled");
        }
        
        // Проверяем наличие камеры под XROrigin
        var xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null)
        {
            if (xrOrigin.Camera == null)
            {
                Debug.LogError("EnhancedARMLController: No camera found under XROrigin! Attempting to fix...");
                
                // Ищем объект AR Camera
                var arCamera = GameObject.Find("AR Camera")?.GetComponent<Camera>();
                if (arCamera == null)
                {
                    // Ищем любую камеру с тегом MainCamera
                    arCamera = GameObject.FindWithTag("MainCamera")?.GetComponent<Camera>();
                }
                
                if (arCamera != null)
                {
                    // Устанавливаем ссылку на камеру
                    xrOrigin.Camera = arCamera;
                    if (enableDebugLogs)
                        Debug.Log("EnhancedARMLController: Set AR Camera reference in XROrigin");
                }
            }
            else if (enableDebugLogs)
            {
                Debug.Log($"EnhancedARMLController: XROrigin camera is set to {xrOrigin.Camera.gameObject.name}");
            }
        }
        
        // Продолжаем стандартную проверку
        yield return new WaitForSeconds(0.5f);
        CheckARSessionStatus();
    }
    
    private void CheckARSessionStatus()
    {
        if (hasAttemptedFix) return;
        
        bool isARReady = false;
        
        // Проверяем через ARSessionHelper
        if (sessionHelper != null)
        {
            isARReady = sessionHelper.IsARSessionReady();
        }
        else
        {
            // Альтернативный способ проверки
            isARReady = ARSession.state == ARSessionState.Ready || 
                       ARSession.state == ARSessionState.SessionTracking;
        }
        
        if (isARReady)
        {
            if (enableDebugLogs)
                Debug.Log("EnhancedARMLController: AR сессия готова, вызываем StartAR вручную");
            hasAttemptedFix = true;
            
            // Проверяем, есть ли метод StartAR
            var startMethod = controller.GetType().GetMethod("StartAR", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            if (startMethod != null)
            {
                // Запускаем AR через 1 секунду
                Invoke("StartARDelayed", 1.0f);
            }
        }
        else
        {
            // Пробуем проверить еще раз через 1 секунду
            Invoke("CheckARSessionStatus", 1.0f);
        }
    }
    
    private void StartARDelayed()
    {
        if (controller != null)
        {
            // Прямой вызов StartAR
            controller.GetType().GetMethod("StartAR", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.Invoke(controller, null);
                
            if (enableDebugLogs)
                Debug.Log("EnhancedARMLController: StartAR вызван успешно");
        }
    }
    
    public void ManualStartAR()
    {
        if (controller != null)
        {
            controller.GetType().GetMethod("StartAR", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.Invoke(controller, null);
        }
    }
} 