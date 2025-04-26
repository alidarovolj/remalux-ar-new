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
        
        // Используем ARCameraSetup для настройки AR камеры
        Debug.Log("ARBootstrapper: Ensuring AR Camera is properly set up");
        ARCameraSetup.EnsureARCameraExists();
        
        // Проверяем и настраиваем XROrigin
        var xrOrigins = Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        foreach (var origin in xrOrigins)
        {
            // Проверяем настройку CameraFloorOffset
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
        
        // Try to add Tracked Pose Driver if possible
        var xrOriginInstances = Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
        foreach (var origin in xrOriginInstances)
        {
            if (origin.Camera != null)
            {
                // Check for TrackedPoseDriver
                bool hasTrackedPoseDriver = HasTrackedPoseDriver(origin.Camera.gameObject);
                
                if (!hasTrackedPoseDriver)
                {
                    Debug.Log("ARBootstrapper: No TrackedPoseDriver found on AR Camera. Adding one...");
                    AddTrackedPoseDriver(origin.Camera.gameObject);
                }
                else
                {
                    Debug.Log("ARBootstrapper: TrackedPoseDriver already exists on AR Camera");
                }
            }
        }
        
        // Включаем ARCameraManager после настройки
        foreach (var manager in cameraManagers)
        {
            manager.enabled = true;
        }
        
        // Ensure ARMeshManager is configured and enabled
        var meshManager = Object.FindObjectOfType<ARMeshManager>();
        if (meshManager != null)
        {
            meshManager.enabled = true;
        }
        
        // Постепенно включаем ARMLController через небольшую задержку
        GameObject bootstrapHelper = new GameObject("ARBootstrapperHelper");
        var helper = bootstrapHelper.AddComponent<ARBootstrapperHelper>();
        
        // Передаем ссылки на контроллеры
        helper.SetControllers(armlControllers);
        
        // Ensure AR Hierarchy is correct
        GameObject hierarchyFixerObj = new GameObject("ARHierarchyFixer");
        var fixer = hierarchyFixerObj.AddComponent<ARHierarchyFixer>();
        // It will auto-fix and cleanup on start
        
        Debug.Log("ARBootstrapper: Инициализация AR завершена");
    }
    
    /// <summary>
    /// Checks if the given GameObject has any form of TrackedPoseDriver component
    /// </summary>
    private static bool HasTrackedPoseDriver(GameObject cameraObject)
    {
        // Use direct check with TryGetComponent for known types first (most efficient)
        if (TryGetTrackedPoseDriverComponent(cameraObject))
        {
            return true;
        }
        
        // Fallback: Check for any component with "TrackedPose" in its name
        var components = cameraObject.GetComponents<MonoBehaviour>();
        foreach (var component in components)
        {
            if (component != null && component.GetType().Name.Contains("TrackedPose"))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Try to get tracked pose driver component using TryGetComponent for common types
    /// </summary>
    private static bool TryGetTrackedPoseDriverComponent(GameObject cameraObject)
    {
        // Check for Unity XR Legacy Tracked Pose Driver via reflection (avoid direct reference)
        var legacyDriverType = System.Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
        if (legacyDriverType != null)
        {
            if (cameraObject.TryGetComponent(legacyDriverType, out var _))
            {
                return true;
            }
        }
        
        // Check for Input System Tracked Pose Driver via reflection
        var inputSystemDriverType = System.Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
        if (inputSystemDriverType != null)
        {
            if (cameraObject.TryGetComponent(inputSystemDriverType, out var _))
            {
                return true;
            }
        }
        
        // Check for AR Foundation Tracked Pose Driver via reflection
        var arFoundationDriverType = System.Type.GetType("UnityEngine.XR.ARFoundation.ARPoseDriver, Unity.XR.ARFoundation");
        if (arFoundationDriverType != null)
        {
            if (cameraObject.TryGetComponent(arFoundationDriverType, out var _))
            {
                return true;
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Add a tracked pose driver component to the camera
    /// </summary>
    private static void AddTrackedPoseDriver(GameObject cameraObject)
    {
        try
        {
            // Try Input System TPD first (preferred)
            System.Type trackedPoseDriverType = System.Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
            if (trackedPoseDriverType != null)
            {
                cameraObject.AddComponent(trackedPoseDriverType);
                Debug.Log("ARBootstrapper: Added TrackedPoseDriver (Input System) to AR Camera");
                return;
            }
            
            // Try AR Foundation's ARPoseDriver next
            var arPoseDriverType = System.Type.GetType("UnityEngine.XR.ARFoundation.ARPoseDriver, Unity.XR.ARFoundation");
            if (arPoseDriverType != null)
            {
                cameraObject.AddComponent(arPoseDriverType);
                Debug.Log("ARBootstrapper: Added ARPoseDriver to AR Camera");
                return;
            }
            
            // Try Legacy TPD as last resort
            var legacyType = System.Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
            if (legacyType != null)
            {
                cameraObject.AddComponent(legacyType);
                Debug.Log("ARBootstrapper: Added Legacy TrackedPoseDriver to AR Camera");
                return;
            }
            
            Debug.LogWarning("ARBootstrapper: Could not find any TrackedPoseDriver type. Camera position will not be updated.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ARBootstrapper: Failed to add TrackedPoseDriver: {ex.Message}");
        }
    }
}

/// <summary>
/// Вспомогательный класс для выполнения отложенных действий
/// </summary>
public class ARBootstrapperHelper : MonoBehaviour
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