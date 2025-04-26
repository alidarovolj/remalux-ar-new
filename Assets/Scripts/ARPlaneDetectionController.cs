using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Компонент, обеспечивающий правильную настройку обнаружения плоскостей при запуске игры
/// </summary>
public class ARPlaneDetectionController : MonoBehaviour
{
    [SerializeField] 
    private ARPlaneManager planeManager;
    
    [SerializeField]
    private bool enableVerticalPlanes = true;
    
    [SerializeField]
    private bool enableHorizontalPlanes = true;
    
    [Tooltip("Включает детальное логирование в консоль, отключите для релизных билдов")]
    [SerializeField]
    private bool enableDetailedLogging = true;
    
    /// <summary>
    /// Публичное свойство для установки ARPlaneManager
    /// </summary>
    public ARPlaneManager PlaneManager
    {
        get { return planeManager; }
        set
        {
            if (value != planeManager)
            {
                planeManager = value;
                if (isActiveAndEnabled)
                {
                    ConfigurePlaneDetection();
                }
            }
        }
    }
    
    /// <summary>
    /// Установка горизонтального обнаружения плоскостей
    /// </summary>
    public bool EnableHorizontalPlanes
    {
        get { return enableHorizontalPlanes; }
        set
        {
            if (value != enableHorizontalPlanes)
            {
                enableHorizontalPlanes = value;
                if (isActiveAndEnabled && planeManager != null)
                {
                    ConfigurePlaneDetection();
                }
            }
        }
    }
    
    /// <summary>
    /// Установка вертикального обнаружения плоскостей
    /// </summary>
    public bool EnableVerticalPlanes
    {
        get { return enableVerticalPlanes; }
        set
        {
            if (value != enableVerticalPlanes)
            {
                enableVerticalPlanes = value;
                if (isActiveAndEnabled && planeManager != null)
                {
                    ConfigurePlaneDetection();
                }
            }
        }
    }
    
    /// <summary>
    /// Публичное свойство для включения/отключения логирования
    /// </summary>
    public bool EnableDetailedLogging
    {
        get { return enableDetailedLogging; }
        set { enableDetailedLogging = value; }
    }
    
    /// <summary>
    /// Публичный метод для установки ARPlaneManager извне
    /// </summary>
    public void SetPlaneManager(ARPlaneManager manager)
    {
        PlaneManager = manager;
    }
    
    /// <summary>
    /// Логирование с возможностью отключения
    /// </summary>
    private void LogInfo(string message)
    {
        if (enableDetailedLogging)
        {
            Debug.Log(message);
        }
    }
    
    /// <summary>
    /// Логирование предупреждений с возможностью отключения
    /// </summary>
    private void LogWarning(string message)
    {
        if (enableDetailedLogging)
        {
            Debug.LogWarning(message);
        }
    }
    
    /// <summary>
    /// Логирование ошибок (всегда активно)
    /// </summary>
    private void LogError(string message)
    {
        // Ошибки логируем всегда, даже при отключенном детальном логировании
        Debug.LogError(message);
    }
    
    void Awake()
    {
        // Если planeManager не назначен в инспекторе, пытаемся найти его
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
        }
        
        if (planeManager == null)
        {
            LogWarning("ARPlaneDetectionController: ARPlaneManager не найден в сцене");
            enabled = false;
            return;
        }
        
        // Автоматически отключаем детальное логирование в билде
        #if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        enableDetailedLogging = false;
        #endif
    }
    
    void Start()
    {
        ConfigurePlaneDetection();
    }
    
    void OnEnable()
    {
        if (planeManager != null)
        {
            ConfigurePlaneDetection();
        }
    }
    
    /// <summary>
    /// Настраивает режим обнаружения плоскостей в зависимости от заданных параметров
    /// </summary>
    private void ConfigurePlaneDetection()
    {
        if (planeManager == null)
            return;
            
        // Определяем режим обнаружения на основе настроек
        PlaneDetectionMode detectionMode = PlaneDetectionMode.None;
        
        if (enableHorizontalPlanes)
            detectionMode |= PlaneDetectionMode.Horizontal;
            
        if (enableVerticalPlanes)
            detectionMode |= PlaneDetectionMode.Vertical;
            
        // Применяем режим обнаружения напрямую через публичное свойство
        #if UNITY_2022_1_OR_NEWER || UNITY_2021_1_OR_NEWER || UNITY_2020_1_OR_NEWER
        planeManager.requestedDetectionMode = detectionMode;
        #else
        // Для более старых версий AR Foundation
        PlaneDetectionFlags detectionFlags = PlaneDetectionFlags.None;
        if (enableHorizontalPlanes)
            detectionFlags |= PlaneDetectionFlags.Horizontal;
        if (enableVerticalPlanes)
            detectionFlags |= PlaneDetectionFlags.Vertical;
            
        planeManager.detectionMode = detectionFlags;
        #endif
        
        string enabledPlanes = "";
        if (enableHorizontalPlanes) enabledPlanes += "горизонтальных ";
        if (enableVerticalPlanes) enabledPlanes += "вертикальных ";
        
        LogInfo($"ARPlaneDetectionController: Установлен режим обнаружения {enabledPlanes}плоскостей");
    }
} 