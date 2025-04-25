using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Reflection;
using ML.DeepLab;

/// <summary>
/// Компонент для соединения AR и ML систем
/// Обеспечивает корректную работу компонентов распознавания стен
/// </summary>
[RequireComponent(typeof(ARCameraManager))]
public class MLConnector : MonoBehaviour
{
    [Header("Компоненты AR")]
    [Tooltip("AR Camera Manager - обычно на основной камере")]
    public ARCameraManager cameraManager;
    
    [Tooltip("AR Session - обычно корневой компонент AR системы")]
    public ARSession arSession;
    
    [Header("Компоненты ML")]
    [Tooltip("ML контроллер - обычно на объекте ML Controller")]
    public MonoBehaviour armlController;
    
    [Tooltip("DeepLab предиктор - обычно на отдельном объекте")]
    public MonoBehaviour enhancedPredictor;
    
    [Header("Настройки")]
    [Tooltip("Включить автоматическое соединение при старте")]
    public bool connectOnStart = true;
    
    [Tooltip("Автоматически искать отсутствующие компоненты")]
    public bool autoFindComponents = true;
    
    [Tooltip("Логгировать отладочную информацию")]
    public bool debugLog = true;
    
    // При запуске
    private void Start()
    {
        if (connectOnStart)
        {
            // Даем немного времени для инициализации всех компонентов
            StartCoroutine(ConnectWithDelay(0.5f));
        }
    }
    
    // Соединяет AR и ML системы после небольшой задержки
    private IEnumerator ConnectWithDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Ищем отсутствующие компоненты, если включено
        if (autoFindComponents)
        {
            FindMissingComponents();
        }
        
        // Соединяем компоненты
        ConnectComponents();
    }
    
    // Поиск отсутствующих компонентов
    private void FindMissingComponents()
    {
        // Находим ARCameraManager если не указан
        if (cameraManager == null)
        {
            cameraManager = GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = FindObjectOfType<ARCameraManager>();
                if (cameraManager != null && debugLog)
                    Debug.Log("MLConnector: Найден компонент ARCameraManager в сцене");
            }
        }
        
        // Находим ARSession если не указан
        if (arSession == null)
        {
            arSession = FindObjectOfType<ARSession>();
            if (arSession != null && debugLog)
                Debug.Log("MLConnector: Найден компонент ARSession в сцене");
        }
        
        // Находим ARMLController если не указан
        if (armlController == null)
        {
            // Ищем сначала по имени типа
            var controllers = FindObjectsOfType<MonoBehaviour>();
            foreach (var comp in controllers)
            {
                if (comp.GetType().Name == "ARMLController")
                {
                    armlController = comp;
                    if (debugLog)
                        Debug.Log("MLConnector: Найден компонент ARMLController");
                    break;
                }
            }
        }
        
        // Находим EnhancedDeepLabPredictor если не указан
        if (enhancedPredictor == null)
        {
            // Пытаемся найти через прямой тип, если доступен
            enhancedPredictor = FindObjectOfType<EnhancedDeepLabPredictor>();
            
            if (enhancedPredictor == null)
            {
                // Если не нашли, ищем по имени типа
                var components = FindObjectsOfType<MonoBehaviour>();
                foreach (var comp in components)
                {
                    if (comp.GetType().Name == "EnhancedDeepLabPredictor")
                    {
                        enhancedPredictor = comp;
                        if (debugLog)
                            Debug.Log("MLConnector: Найден компонент EnhancedDeepLabPredictor");
                        break;
                    }
                }
            }
            else if (debugLog)
            {
                Debug.Log("MLConnector: Найден компонент EnhancedDeepLabPredictor");
            }
        }
    }
    
    // Связывает компоненты AR и ML
    public void ConnectComponents()
    {
        if (debugLog)
            Debug.Log("MLConnector: Начинаем соединение AR и ML компонентов...");
        
        // Проверяем наличие необходимых компонентов
        if (cameraManager == null)
        {
            Debug.LogError("MLConnector: Не найден ARCameraManager!");
            return;
        }
        
        if (arSession == null)
        {
            Debug.LogError("MLConnector: Не найден ARSession!");
            return;
        }
        
        // Устанавливаем ссылки в ARMLController
        SetupARMLController();
        
        // Устанавливаем ссылки в EnhancedDeepLabPredictor
        SetupEnhancedPredictor();
        
        // Соединяем ARMLController и предиктор
        ConnectControllerToPredictor();
        
        // Включаем компоненты после установки связей
        EnableComponents();
        
        if (debugLog)
            Debug.Log("MLConnector: Соединение AR и ML компонентов завершено");
    }
    
    // Настройка ARMLController
    private void SetupARMLController()
    {
        if (armlController == null)
        {
            Debug.LogWarning("MLConnector: ARMLController не найден, пропускаем настройку");
            return;
        }
        
        try
        {
            System.Type controllerType = armlController.GetType();
            
            // Устанавливаем cameraManager
            FieldInfo cameraManagerField = controllerType.GetField("cameraManager", 
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                
            if (cameraManagerField != null)
            {
                cameraManagerField.SetValue(armlController, cameraManager);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен cameraManager в ARMLController");
            }
            
            // Устанавливаем arSession
            FieldInfo arSessionField = controllerType.GetField("arSession", 
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                
            if (arSessionField != null)
            {
                arSessionField.SetValue(armlController, arSession);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен arSession в ARMLController");
            }
            
            // Устанавливаем параметр isRunning
            FieldInfo isRunningField = controllerType.GetField("isRunning", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (isRunningField != null)
            {
                isRunningField.SetValue(armlController, true);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен параметр isRunning=true в ARMLController");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"MLConnector: Ошибка при настройке ARMLController: {ex.Message}");
        }
    }
    
    // Настройка EnhancedDeepLabPredictor
    private void SetupEnhancedPredictor()
    {
        if (enhancedPredictor == null)
        {
            Debug.LogWarning("MLConnector: EnhancedDeepLabPredictor не найден, пропускаем настройку");
            return;
        }
        
        try
        {
            System.Type predictorType = enhancedPredictor.GetType();
            
            // Устанавливаем cameraManager
            FieldInfo cameraManagerField = predictorType.GetField("cameraManager", 
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                
            if (cameraManagerField != null)
            {
                cameraManagerField.SetValue(enhancedPredictor, cameraManager);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен cameraManager в EnhancedDeepLabPredictor");
            }
            
            // Включаем обработку
            FieldInfo isRunningField = predictorType.GetField("isRunning", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (isRunningField != null)
            {
                isRunningField.SetValue(enhancedPredictor, true);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен параметр isRunning=true в EnhancedDeepLabPredictor");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"MLConnector: Ошибка при настройке EnhancedDeepLabPredictor: {ex.Message}");
        }
    }
    
    // Соединяет ARMLController и предиктор
    private void ConnectControllerToPredictor()
    {
        if (armlController == null || enhancedPredictor == null)
        {
            Debug.LogWarning("MLConnector: Невозможно соединить ARMLController и EnhancedDeepLabPredictor - один из компонентов отсутствует");
            return;
        }
        
        try
        {
            System.Type controllerType = armlController.GetType();
            
            // Устанавливаем enhancedPredictor в ARMLController
            FieldInfo predictorField = controllerType.GetField("enhancedPredictor", 
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                
            if (predictorField != null)
            {
                predictorField.SetValue(armlController, enhancedPredictor);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен enhancedPredictor в ARMLController");
            }
            
            // Устанавливаем флаг использования enhancedPredictor
            FieldInfo useEnhancedField = controllerType.GetField("useEnhancedPredictor", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (useEnhancedField != null)
            {
                useEnhancedField.SetValue(armlController, true);
                if (debugLog)
                    Debug.Log("MLConnector: Установлен флаг useEnhancedPredictor=true");
            }
            
            // Пробуем найти и вызвать метод StartAR
            MethodInfo startARMethod = controllerType.GetMethod("StartAR", 
                BindingFlags.Public | BindingFlags.Instance);
                
            if (startARMethod != null)
            {
                // Вызываем метод с небольшой задержкой
                StartCoroutine(InvokeMethodAfterDelay(startARMethod, armlController, 1.0f));
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"MLConnector: Ошибка при соединении ARMLController и EnhancedDeepLabPredictor: {ex.Message}");
        }
    }
    
    // Вызывает метод с задержкой
    private IEnumerator InvokeMethodAfterDelay(MethodInfo method, object target, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        try
        {
            method.Invoke(target, null);
            if (debugLog)
                Debug.Log($"MLConnector: Успешно вызван метод {method.Name}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"MLConnector: Ошибка при вызове метода {method.Name}: {ex.Message}");
        }
    }
    
    // Включает все компоненты
    private void EnableComponents()
    {
        if (armlController != null)
        {
            armlController.enabled = true;
            if (debugLog)
                Debug.Log("MLConnector: Включен компонент ARMLController");
        }
        
        if (enhancedPredictor != null)
        {
            enhancedPredictor.enabled = true;
            if (debugLog)
                Debug.Log("MLConnector: Включен компонент EnhancedDeepLabPredictor");
        }
    }
} 