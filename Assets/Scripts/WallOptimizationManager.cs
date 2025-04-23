using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;

/// <summary>
/// Менеджер оптимизаций обнаружения стен. Автоматически применяет все необходимые настройки
/// для исправления проблем с "прилипанием стен" и производительностью.
/// </summary>
public class WallOptimizationManager : MonoBehaviour
{
    [Header("Компоненты")]
    [Tooltip("Оптимизатор обнаружения стен")]
    public WallOptimizer wallOptimizer;
    
    [Tooltip("Предиктор DeepLab")]
    public EnhancedDeepLabPredictor enhancedPredictor;
    
    [Tooltip("Компонент AR Raycast Manager")]
    public ARRaycastManager raycastManager;
    
    [Tooltip("Компонент AR Anchor Manager")]
    public ARAnchorManager anchorManager;
    
    [Tooltip("Компонент AR Plane Manager")]
    public ARPlaneManager planeManager;
    
    [Header("Настройки")]
    [Tooltip("Применить оптимизации при старте")]
    public bool applyOnStart = true;
    
    [Tooltip("Задержка перед применением оптимизаций (секунды)")]
    public float applyDelay = 0.5f;
    
    [Tooltip("Включить вертикальное обнаружение плоскостей")]
    public bool enableVerticalPlaneDetection = true;
    
    [Tooltip("Коэффициент даунсемплинга (1-4, где 1 - без уменьшения)")]
    [Range(1, 4)]
    public int downsamplingFactor = 2;
    
    [Tooltip("Интервал между запусками сегментации (секунды)")]
    [Range(0.1f, 2.0f)]
    public float segmentationInterval = 0.2f;
    
    [Tooltip("Включить AR привязку стен")]
    public bool enableARAttachment = true;
    
    [Tooltip("Показывать отладочные сообщения")]
    public bool showDebugLogs = true;
    
    void Start()
    {
        // Находим все компоненты, если они не назначены
        FindComponents();
        
        if (applyOnStart)
        {
            // Применяем с небольшой задержкой
            StartCoroutine(ApplyOptimizationsDelayed());
        }
    }
    
    /// <summary>
    /// Поиск необходимых компонентов в сцене
    /// </summary>
    private void FindComponents()
    {
        if (wallOptimizer == null)
            wallOptimizer = FindObjectOfType<WallOptimizer>();
            
        if (enhancedPredictor == null)
            enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
        if (raycastManager == null)
            raycastManager = FindObjectOfType<ARRaycastManager>();
            
        if (anchorManager == null)
            anchorManager = FindObjectOfType<ARAnchorManager>();
            
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
    }
    
    /// <summary>
    /// Применение оптимизаций с задержкой
    /// </summary>
    private IEnumerator ApplyOptimizationsDelayed()
    {
        yield return new WaitForSeconds(applyDelay);
        ApplyOptimizations();
    }
    
    /// <summary>
    /// Применение всех оптимизаций
    /// </summary>
    public void ApplyOptimizations()
    {
        if (showDebugLogs)
            Debug.Log("WallOptimizationManager: Применение оптимизаций...");
        
        // Настройка вертикального обнаружения плоскостей
        OptimizePlaneDetection();
        
        // Оптимизация предиктора
        OptimizePredictor();
        
        // Настройка WallOptimizer
        OptimizeWallDetection();
        
        if (showDebugLogs)
            Debug.Log("WallOptimizationManager: Все оптимизации применены успешно!");
    }
    
    /// <summary>
    /// Оптимизация обнаружения AR плоскостей
    /// </summary>
    private void OptimizePlaneDetection()
    {
        if (planeManager != null && enableVerticalPlaneDetection)
        {
            // Включаем обнаружение вертикальных плоскостей
            if (planeManager.requestedDetectionMode != UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical)
            {
                planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
                
                if (showDebugLogs)
                    Debug.Log("WallOptimizationManager: Включено обнаружение вертикальных плоскостей");
            }
        }
        else if (showDebugLogs && planeManager == null)
        {
            Debug.LogWarning("WallOptimizationManager: ARPlaneManager не найден, оптимизация плоскостей пропущена");
        }
    }
    
    /// <summary>
    /// Оптимизация предиктора DeepLab
    /// </summary>
    private void OptimizePredictor()
    {
        if (enhancedPredictor != null)
        {
            // Включаем даунсемплинг для повышения производительности
            enhancedPredictor.enableDownsampling = (downsamplingFactor > 1);
            enhancedPredictor.downsamplingFactor = downsamplingFactor;
            enhancedPredictor.minSegmentationInterval = segmentationInterval;
            
            if (showDebugLogs)
                Debug.Log($"WallOptimizationManager: Оптимизирован предиктор (даунсемплинг: {downsamplingFactor}x, интервал: {segmentationInterval}с)");
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning("WallOptimizationManager: EnhancedDeepLabPredictor не найден, оптимизация предиктора пропущена");
        }
    }
    
    /// <summary>
    /// Оптимизация WallOptimizer
    /// </summary>
    private void OptimizeWallDetection()
    {
        if (wallOptimizer != null)
        {
            // Настраиваем привязку стен к AR
            wallOptimizer.useARPlaneAttachment = enableARAttachment && raycastManager != null;
            wallOptimizer.createARAnchors = enableARAttachment && anchorManager != null;
            
            // Устанавливаем интервал обновления
            wallOptimizer.updateInterval = segmentationInterval;
            
            if (showDebugLogs)
                Debug.Log($"WallOptimizationManager: Оптимизирован WallOptimizer (привязка AR: {enableARAttachment}, интервал: {segmentationInterval}с)");
        }
        else if (showDebugLogs)
        {
            Debug.LogWarning("WallOptimizationManager: WallOptimizer не найден, оптимизация обнаружения стен пропущена");
        }
    }
} 