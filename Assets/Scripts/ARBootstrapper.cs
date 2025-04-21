using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Класс для раннего инициализации AR компонентов
/// Использует RuntimeInitializeOnLoadMethod для запуска до компонентов Awake
/// </summary>
public static class ARBootstrapper
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Debug.Log("ARBootstrapper: Инициализация AR систем до загрузки сцены");
        Application.runInBackground = true;
        
        // Зарегистрируем callback, который выполнится сразу после загрузки сцены
        Application.onBeforeRender += OnBeforeFirstRender;
    }
    
    private static void OnBeforeFirstRender()
    {
        // Выполняется только один раз
        Application.onBeforeRender -= OnBeforeFirstRender;
        Debug.Log("ARBootstrapper: Настройка AR компонентов перед первым рендером");
        
        // Отключаем временно все ARCameraManager для предотвращения раннего запуска
        var cameraManagers = Object.FindObjectsByType<ARCameraManager>(FindObjectsSortMode.None);
        foreach (var manager in cameraManagers)
        {
            manager.enabled = false;
        }
        
        // Отключаем ARMLController для предотвращения автозапуска
        var armlControllers = Object.FindObjectsByType<ARMLController>(FindObjectsSortMode.None);
        foreach (var controller in armlControllers)
        {
            controller.enabled = false;
        }
        
        // Проверяем и настраиваем XROrigin
        var xrOrigins = Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        foreach (var origin in xrOrigins)
        {
            if (origin.Camera == null)
            {
                // Ищем камеру
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    origin.Camera = mainCamera;
                    Debug.Log("ARBootstrapper: Присвоена камера к XROrigin: " + mainCamera.name);
                }
                
                // Если это ARCamera с ARCameraManager, также проверим её
                var cameraManager = mainCamera?.GetComponent<ARCameraManager>();
                if (cameraManager != null)
                {
                    // Гарантируем, что ARCameraManager отключен до настройки
                    cameraManager.enabled = false;
                }
            }
            
            if (origin.CameraFloorOffsetObject == null)
            {
                // Ищем Camera Offset в дочерних
                Transform cameraOffset = null;
                foreach (Transform child in origin.transform)
                {
                    if (child.name.Contains("Camera Offset"))
                    {
                        cameraOffset = child;
                        break;
                    }
                }
                
                // Если не нашли, ищем родителя камеры
                if (cameraOffset == null && origin.Camera != null)
                {
                    cameraOffset = origin.Camera.transform.parent;
                }
                
                // Если всё ещё не нашли, создаем новый
                if (cameraOffset == null)
                {
                    GameObject newOffset = new GameObject("Camera Offset");
                    newOffset.transform.SetParent(origin.transform);
                    newOffset.transform.localPosition = Vector3.zero;
                    cameraOffset = newOffset.transform;
                    
                    // Если есть камера, переносим её в offset
                    if (origin.Camera != null && origin.Camera.transform.parent != cameraOffset)
                    {
                        origin.Camera.transform.SetParent(cameraOffset);
                        origin.Camera.transform.localPosition = Vector3.zero;
                        origin.Camera.transform.localRotation = Quaternion.identity;
                    }
                }
                
                origin.CameraFloorOffsetObject = cameraOffset.gameObject;
                Debug.Log("ARBootstrapper: Настроен CameraFloorOffset для XROrigin: " + cameraOffset.name);
            }
            
            // Принудительно обновляем XROrigin, чтобы он применил изменения сразу
            var originType = origin.GetType();
            originType.GetMethod("UpgradeOrMigrateXRCameraIfNeeded", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                .Invoke(origin, null);
        }
        
        // Включаем ARCameraManager после настройки
        foreach (var manager in cameraManagers)
        {
            manager.enabled = true;
        }
        
        // Постепенно включаем ARMLController через небольшую задержку
        GameObject bootstrapHelper = new GameObject("ARBootstrapHelper");
        var helper = bootstrapHelper.AddComponent<ARBootstrapHelper>();
        
        // Передаем ссылки на контроллеры
        helper.SetControllers(armlControllers);
        
        Debug.Log("ARBootstrapper: Инициализация AR завершена");
    }
}

/// <summary>
/// Вспомогательный класс для выполнения отложенных действий
/// </summary>
public class ARBootstrapHelper : MonoBehaviour
{
    private ARMLController[] controllers;
    private float startTime;
    private List<ARMLController> initializedControllers = new List<ARMLController>();
    private bool startupComplete = false;
    
    public void SetControllers(ARMLController[] armlControllers)
    {
        controllers = armlControllers;
        startTime = Time.time;
        initializedControllers = new List<ARMLController>();
    }
    
    private void Update()
    {
        // Prevent repeated enabling after startup is complete
        if (startupComplete) return;
        
        // Wait 3 seconds before enabling controllers - increased to 3 for reliability
        if (controllers != null && Time.time - startTime > 3f)
        {
            Debug.Log("ARBootstrapHelper: Enabling ARMLController after initialization (one-time operation)");
            startupComplete = true;
            
            foreach (var controller in controllers)
            {
                if (controller != null && !initializedControllers.Contains(controller))
                {
                    // Disable autoStartAR to prevent immediate launch before session is ready
                    var autoStartField = controller.GetType().GetField("autoStartAR", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    
                    if (autoStartField != null)
                    {
                        autoStartField.SetValue(controller, false);
                    }
                    
                    // Enable controller
                    controller.enabled = true;
                    initializedControllers.Add(controller);
                    
                    // Manually start AR after a short delay to ensure session is ready
                    StartCoroutine(StartARAfterDelay(controller, 1.0f));
                }
            }
            
            // Remove helper after enabling controllers, but with delay
            Destroy(gameObject, 5f);
        }
    }
    
    private System.Collections.IEnumerator StartARAfterDelay(ARMLController controller, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Start the controller manually after session should be ready
        Debug.Log("ARBootstrapHelper: Starting AR controller (one per controller)");
        controller.StartAR();
    }
} 