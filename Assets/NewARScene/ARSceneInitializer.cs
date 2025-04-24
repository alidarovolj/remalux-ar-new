using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using ML.DeepLab;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.XR.ARSubsystems;
using System.Linq;
using Unity.XR.CoreUtils;

/// <summary>
/// Инициализатор AR сцены, устанавливает связи между системами
/// </summary>
public class NewARSceneInitializer : MonoBehaviour
{
    [Header("Корневые объекты")]
    [SerializeField] private GameObject arSystem;
    [SerializeField] private GameObject mlSystem;
    [SerializeField] private GameObject wallSystem;
    [SerializeField] private GameObject uiCanvas;
    [SerializeField] private GameObject wallOptimizationManager;

    [Header("Материалы")]
    [SerializeField] private Material wallMaterial;

    [Header("Настройки")]
    [SerializeField] private bool autoConnectOnStart = true;
    [SerializeField] private bool fixDuplicateComponents = true;
    [SerializeField] private bool addMissingComponents = true;

    private Dictionary<string, Component> cachedComponents = new Dictionary<string, Component>();

    private void OnEnable()
    {
        if (autoConnectOnStart && Application.isEditor && !Application.isPlaying)
        {
            SetupARScene();
        }
    }

    /// <summary>
    /// Настраивает ссылки между всеми компонентами AR сцены
    /// </summary>
    [ContextMenu("Настроить ссылки AR сцены")]
    public void SetupARScene()
    {
        Debug.Log("Начало настройки ссылок AR сцены...");
        
        // Подготовка кеша компонентов
        CacheComponents();
        
        // Фиксация дублирующихся компонентов
        if (fixDuplicateComponents)
        {
            FixDuplicateComponents();
        }
        
        // Добавление отсутствующих компонентов
        if (addMissingComponents)
        {
            AddMissingComponents();
        }
        
        // Настройка AR системы
        SetupARSystem();
        
        // Настройка ML системы
        SetupMLSystem();
        
        // Настройка Wall системы
        SetupWallSystem();
        
        // Настройка Wall Optimization Manager
        SetupWallOptimizationManager();
        
        Debug.Log("Настройка ссылок AR сцены завершена!");
    }

    /// <summary>
    /// Кеширует важные компоненты для быстрого доступа
    /// </summary>
    private void CacheComponents()
    {
        // AR System компоненты
        CacheComponent<ARSession>("ARSession", FindComponentInChildren<ARSession>(arSystem));
        CacheComponent<ARSessionHelper>("ARSessionHelper", FindComponentInChildren<ARSessionHelper>(arSystem));
        
        // Replace XROrigin with GameObject or ARSessionOrigin as appropriate
        GameObject xrOriginGameObject = FindGameObjectInChildren(arSystem, "XR Origin");
        if (xrOriginGameObject != null)
        {
            cachedComponents["XROrigin"] = xrOriginGameObject.transform;
        }
        
        CacheComponent<ARCameraManager>("ARCameraManager", FindComponentInChildren<ARCameraManager>(arSystem));
        CacheComponent<ARPlaneManager>("ARPlaneManager", FindComponentInChildren<ARPlaneManager>(arSystem));
        CacheComponent<ARRaycastManager>("ARRaycastManager", FindComponentInChildren<ARRaycastManager>(arSystem));
        CacheComponent<ARMeshManager>("ARMeshManager", FindComponentInChildren<ARMeshManager>(arSystem));
        CacheComponent<WallAligner>("WallAligner", FindComponentInChildren<WallAligner>(arSystem));
        CacheComponent<ARManager>("ARManager", FindComponentInChildren<ARManager>(arSystem));
        CacheComponent<ARAnchorManager>("ARAnchorManager", FindComponentInChildren<ARAnchorManager>(arSystem));
        
        // ML System компоненты
        CacheComponent<DeepLabPredictor>("DeepLabPredictor", FindComponentInChildren<DeepLabPredictor>(mlSystem));
        CacheComponent<EnhancedDeepLabPredictor>("EnhancedDeepLabPredictor", FindComponentInChildren<EnhancedDeepLabPredictor>(mlSystem));
        CacheComponent<MLManager>("MLManager", FindComponentInChildren<MLManager>(mlSystem));
        CacheComponent<WallDetectionSetup>("WallDetectionSetup", FindComponentInChildren<WallDetectionSetup>(mlSystem));
        CacheComponent<WallColorizer>("WallColorizer", FindComponentInChildren<WallColorizer>(mlSystem));
        CacheComponent<ARMLController>("ARMLController", FindComponentInChildren<ARMLController>(mlSystem));
        CacheComponent<FixARMLController>("FixARMLController", FindComponentInChildren<FixARMLController>(mlSystem));
        
        // Wall System компоненты
        CacheComponent<WallOptimizer>("WallOptimizer", FindComponentInChildren<WallOptimizer>(wallSystem));
        CacheComponent<EnhancedWallRenderer>("EnhancedWallRenderer", FindComponentInChildren<EnhancedWallRenderer>(wallSystem));
        CacheComponent<WallDetectionTuner>("WallDetectionTuner", FindComponentInChildren<WallDetectionTuner>(wallSystem));
        
        // UI компоненты
        CacheComponent<RawImage>("ARDisplay", FindComponentInChildren<RawImage>(uiCanvas, "AR Display"));
        
        // Wall Optimization Manager
        CacheComponent<WallOptimizationManager>("WallOptimizationManager", FindComponentInChildren<WallOptimizationManager>(wallOptimizationManager));
    }

    /// <summary>
    /// Настраивает ссылки в AR System
    /// </summary>
    private void SetupARSystem()
    {
        // Настройка ARManager
        ARManager arManager = GetCachedComponent<ARManager>("ARManager");
        ARSession arSession = GetCachedComponent<ARSession>("ARSession");
        ARPlaneManager arPlaneManager = GetCachedComponent<ARPlaneManager>("ARPlaneManager");
        
        if (arManager != null)
        {
            if (arSession != null)
            {
                Reflection.SetPropertyValue(arManager, "arSession", arSession);
                Debug.Log("Установлена ссылка ARManager.arSession");
            }
            
            if (arPlaneManager != null)
            {
                Reflection.SetPropertyValue(arManager, "planeManager", arPlaneManager);
                Debug.Log("Установлена ссылка ARManager.planeManager");
            }
        }
        
        // Настройка WallAligner
        WallAligner wallAligner = GetCachedComponent<WallAligner>("WallAligner");
        if (wallAligner != null && wallMaterial != null)
        {
            Reflection.SetPropertyValue(wallAligner, "wallMaterial", wallMaterial);
            Debug.Log("Установлена ссылка WallAligner.wallMaterial");
        }
    }

    /// <summary>
    /// Настраивает ссылки в ML System
    /// </summary>
    private void SetupMLSystem()
    {
        // Настройка MLManager
        MLManager mlManager = GetCachedComponent<MLManager>("MLManager");
        DeepLabPredictor deepLabPredictor = GetCachedComponent<DeepLabPredictor>("DeepLabPredictor");
        ARCameraManager arCameraManager = GetCachedComponent<ARCameraManager>("ARCameraManager");
        
        if (mlManager != null)
        {
            if (deepLabPredictor != null)
            {
                Reflection.SetPropertyValue(mlManager, "deepLabPredictor", deepLabPredictor);
                Debug.Log("Установлена ссылка MLManager.deepLabPredictor");
            }
            
            if (arCameraManager != null)
            {
                Reflection.SetPropertyValue(mlManager, "arCameraManager", arCameraManager);
                Debug.Log("Установлена ссылка MLManager.arCameraManager");
            }
        }
        
        // Настройка WallColorizer
        WallColorizer wallColorizer = GetCachedComponent<WallColorizer>("WallColorizer");
        RawImage arDisplay = GetCachedComponent<RawImage>("ARDisplay");
        Camera arCamera = arCameraManager?.GetComponent<Camera>();
        
        if (wallColorizer != null)
        {
            if (arDisplay != null)
            {
                Reflection.SetPropertyValue(wallColorizer, "displayImage", arDisplay);
                Debug.Log("Установлена ссылка WallColorizer.displayImage");
            }
            
            if (arCamera != null)
            {
                Reflection.SetPropertyValue(wallColorizer, "arCamera", arCamera);
                Debug.Log("Установлена ссылка WallColorizer.arCamera");
            }
            
            if (wallMaterial != null)
            {
                Reflection.SetPropertyValue(wallColorizer, "wallMaterial", wallMaterial);
                Debug.Log("Установлена ссылка WallColorizer.wallMaterial");
            }
        }
        
        // Настройка FixARMLController
        ARMLController armlController = GetCachedComponent<ARMLController>("ARMLController");
        FixARMLController fixARMLController = GetCachedComponent<FixARMLController>("FixARMLController");
        ARSession arSession = GetCachedComponent<ARSession>("ARSession");
        
        if (fixARMLController != null)
        {
            if (arSession != null)
            {
                Reflection.SetPropertyValue(fixARMLController, "arSession", arSession);
                Debug.Log("Установлена ссылка FixARMLController.arSession");
            }
            
            if (armlController != null)
            {
                Reflection.SetPropertyValue(fixARMLController, "armlController", armlController);
                Debug.Log("Установлена ссылка FixARMLController.armlController");
            }
        }
        
        // Настройка ARMLController
        if (armlController != null)
        {
            ARManager arManager = GetCachedComponent<ARManager>("ARManager");
            if (arManager != null)
            {
                Reflection.SetPropertyValue(armlController, "arManager", arManager);
                Debug.Log("Установлена ссылка ARMLController.arManager");
            }
        }
    }

    /// <summary>
    /// Настраивает ссылки в Wall System
    /// </summary>
    private void SetupWallSystem()
    {
        // Настройка EnhancedWallRenderer
        EnhancedWallRenderer enhancedWallRenderer = GetCachedComponent<EnhancedWallRenderer>("EnhancedWallRenderer");
        ARCameraManager arCameraManager = GetCachedComponent<ARCameraManager>("ARCameraManager");
        EnhancedDeepLabPredictor enhancedDeepLabPredictor = GetCachedComponent<EnhancedDeepLabPredictor>("EnhancedDeepLabPredictor");
        
        if (enhancedWallRenderer != null)
        {
            if (arCameraManager != null)
            {
                Reflection.SetPropertyValue(enhancedWallRenderer, "ARCameraManager", arCameraManager);
                Debug.Log("Установлена ссылка EnhancedWallRenderer.ARCameraManager");
            }
            
            if (enhancedDeepLabPredictor != null)
            {
                Reflection.SetPropertyValue(enhancedWallRenderer, "Predictor", enhancedDeepLabPredictor);
                Debug.Log("Установлена ссылка EnhancedWallRenderer.Predictor");
            }
            
            if (wallMaterial != null)
            {
                Reflection.SetPropertyValue(enhancedWallRenderer, "WallMaterial", wallMaterial);
                Debug.Log("Установлена ссылка EnhancedWallRenderer.WallMaterial");
            }
        }
        
        // Настройка WallDetectionTuner
        WallDetectionTuner wallDetectionTuner = GetCachedComponent<WallDetectionTuner>("WallDetectionTuner");
        WallOptimizer wallOptimizer = GetCachedComponent<WallOptimizer>("WallOptimizer");
        
        if (wallDetectionTuner != null)
        {
            if (wallOptimizer != null)
            {
                Reflection.SetPropertyValue(wallDetectionTuner, "wallOptimizer", wallOptimizer);
                Debug.Log("Установлена ссылка WallDetectionTuner.wallOptimizer");
            }
            
            if (enhancedWallRenderer != null)
            {
                Reflection.SetPropertyValue(wallDetectionTuner, "wallRenderer", enhancedWallRenderer);
                Debug.Log("Установлена ссылка WallDetectionTuner.wallRenderer");
            }
        }
    }

    /// <summary>
    /// Настраивает ссылки в Wall Optimization Manager
    /// </summary>
    private void SetupWallOptimizationManager()
    {
        WallOptimizationManager wallOptimizationMgr = GetCachedComponent<WallOptimizationManager>("WallOptimizationManager");
        WallOptimizer wallOptimizer = GetCachedComponent<WallOptimizer>("WallOptimizer");
        EnhancedDeepLabPredictor enhancedDeepLabPredictor = GetCachedComponent<EnhancedDeepLabPredictor>("EnhancedDeepLabPredictor");
        ARRaycastManager arRaycastManager = GetCachedComponent<ARRaycastManager>("ARRaycastManager");
        ARPlaneManager arPlaneManager = GetCachedComponent<ARPlaneManager>("ARPlaneManager");
        ARAnchorManager arAnchorManager = GetCachedComponent<ARAnchorManager>("ARAnchorManager");
        
        if (wallOptimizationMgr != null)
        {
            if (wallOptimizer != null)
            {
                Reflection.SetPropertyValue(wallOptimizationMgr, "WallOptimizer", wallOptimizer);
                Debug.Log("Установлена ссылка WallOptimizationManager.WallOptimizer");
            }
            
            if (enhancedDeepLabPredictor != null)
            {
                Reflection.SetPropertyValue(wallOptimizationMgr, "EnhancedPredictor", enhancedDeepLabPredictor);
                Debug.Log("Установлена ссылка WallOptimizationManager.EnhancedPredictor");
            }
            
            if (arRaycastManager != null)
            {
                Reflection.SetPropertyValue(wallOptimizationMgr, "RaycastManager", arRaycastManager);
                Debug.Log("Установлена ссылка WallOptimizationManager.RaycastManager");
            }
            
            if (arPlaneManager != null)
            {
                Reflection.SetPropertyValue(wallOptimizationMgr, "PlaneManager", arPlaneManager);
                Debug.Log("Установлена ссылка WallOptimizationManager.PlaneManager");
            }
            
            if (arAnchorManager != null)
            {
                Reflection.SetPropertyValue(wallOptimizationMgr, "AnchorManager", arAnchorManager);
                Debug.Log("Установлена ссылка WallOptimizationManager.AnchorManager");
            }
        }
    }

    /// <summary>
    /// Исправляет дублирующиеся компоненты
    /// </summary>
    private void FixDuplicateComponents()
    {
        // Исправление дублирования Wall Aligner
        GameObject arMeshManagerObj = FindGameObjectInChildren(arSystem, "AR Mesh Manager");
        if (arMeshManagerObj != null)
        {
            WallAligner[] wallAligners = arMeshManagerObj.GetComponents<WallAligner>();
            if (wallAligners.Length > 1)
            {
                Debug.LogWarning("Обнаружены дублирующиеся компоненты Wall Aligner. Оставляем только один с материалом.");
                
                // Находим Wall Aligner с материалом
                WallAligner wallAlignerWithMaterial = null;
                foreach (WallAligner aligner in wallAligners)
                {
                    if (Reflection.GetPropertyValue<Material>(aligner, "wallMaterial") != null)
                    {
                        wallAlignerWithMaterial = aligner;
                        break;
                    }
                }
                
                // Если нет Wall Aligner с материалом, берем первый и устанавливаем материал
                if (wallAlignerWithMaterial == null)
                {
                    wallAlignerWithMaterial = wallAligners[0];
                    Reflection.SetPropertyValue(wallAlignerWithMaterial, "wallMaterial", wallMaterial);
                }
                
                // Удаляем остальные Wall Aligner
                for (int i = 0; i < wallAligners.Length; i++)
                {
                    if (wallAligners[i] != wallAlignerWithMaterial)
                    {
                        DestroyImmediate(wallAligners[i]);
                    }
                }
                
                // Обновляем кеш
                CacheComponent<WallAligner>("WallAligner", wallAlignerWithMaterial);
            }
        }
    }

    /// <summary>
    /// Добавляет отсутствующие компоненты
    /// </summary>
    private void AddMissingComponents()
    {
        // Добавление AR Anchor Manager если его нет
        GameObject xrOriginObj = FindGameObjectInChildren(arSystem, "XR Origin");
        if (xrOriginObj != null)
        {
            ARAnchorManager anchorManager = xrOriginObj.GetComponent<ARAnchorManager>();
            if (anchorManager == null)
            {
                Debug.Log("Добавление отсутствующего компонента AR Anchor Manager на XR Origin");
                anchorManager = xrOriginObj.AddComponent<ARAnchorManager>();
                CacheComponent<ARAnchorManager>("ARAnchorManager", anchorManager);
            }
        }
    }

    #region Вспомогательные методы

    private T FindComponentInChildren<T>(GameObject parent, string nameFilter = "") where T : Component
    {
        if (parent == null) return null;
        
        // Сначала проверяем самого родителя
        T component = parent.GetComponent<T>();
        if (component != null && (string.IsNullOrEmpty(nameFilter) || parent.name.Contains(nameFilter)))
        {
            return component;
        }
        
        // Затем проверяем детей
        T[] components = parent.GetComponentsInChildren<T>();
        if (components != null && components.Length > 0)
        {
            // Если задан фильтр по имени, ищем подходящий по имени
            if (!string.IsNullOrEmpty(nameFilter))
            {
                foreach (T comp in components)
                {
                    if (comp.gameObject.name.Contains(nameFilter))
                    {
                        return comp;
                    }
                }
            }
            
            // Иначе возвращаем первый найденный
            return components[0];
        }
        
        return null;
    }
    
    private GameObject FindGameObjectInChildren(GameObject parent, string nameFilter)
    {
        if (parent == null) return null;
        
        // Проверяем самого родителя
        if (parent.name.Contains(nameFilter))
        {
            return parent;
        }
        
        // Рекурсивно ищем среди детей
        Transform[] children = parent.GetComponentsInChildren<Transform>();
        foreach (Transform child in children)
        {
            if (child.gameObject.name.Contains(nameFilter))
            {
                return child.gameObject;
            }
        }
        
        return null;
    }
    
    private void CacheComponent<T>(string key, T component) where T : Component
    {
        if (component != null)
        {
            cachedComponents[key] = component;
        }
    }
    
    private T GetCachedComponent<T>(string key) where T : Component
    {
        if (cachedComponents.TryGetValue(key, out Component component))
        {
            return component as T;
        }
        return null;
    }

    #endregion
}

/// <summary>
/// Вспомогательный класс для работы с отражением
/// </summary>
public static class Reflection
{
    /// <summary>
    /// Устанавливает значение свойства или поля через отражение
    /// </summary>
    public static void SetPropertyValue(object target, string propertyName, object value)
    {
        if (target == null) return;
        
        var targetType = target.GetType();
        
        // Сначала пробуем найти публичное свойство
        var propertyInfo = targetType.GetProperty(propertyName);
        if (propertyInfo != null && propertyInfo.CanWrite)
        {
            propertyInfo.SetValue(target, value);
            return;
        }
        
        // Затем пробуем найти публичное поле
        var fieldInfo = targetType.GetField(propertyName);
        if (fieldInfo != null)
        {
            fieldInfo.SetValue(target, value);
            return;
        }
        
        // Если не нашли публичное, ищем непубличное свойство или поле
        propertyInfo = targetType.GetProperty(propertyName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (propertyInfo != null && propertyInfo.CanWrite)
        {
            propertyInfo.SetValue(target, value);
            return;
        }
        
        fieldInfo = targetType.GetField(propertyName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (fieldInfo != null)
        {
            fieldInfo.SetValue(target, value);
            return;
        }
        
        Debug.LogWarning($"Не удалось найти свойство или поле {propertyName} в {targetType.Name}");
    }
    
    /// <summary>
    /// Получает значение свойства или поля через отражение
    /// </summary>
    public static T GetPropertyValue<T>(object target, string propertyName)
    {
        if (target == null) return default(T);
        
        var targetType = target.GetType();
        
        // Сначала пробуем найти публичное свойство
        var propertyInfo = targetType.GetProperty(propertyName);
        if (propertyInfo != null)
        {
            return (T)propertyInfo.GetValue(target);
        }
        
        // Затем пробуем найти публичное поле
        var fieldInfo = targetType.GetField(propertyName);
        if (fieldInfo != null)
        {
            return (T)fieldInfo.GetValue(target);
        }
        
        // Если не нашли публичное, ищем непубличное свойство или поле
        propertyInfo = targetType.GetProperty(propertyName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (propertyInfo != null)
        {
            return (T)propertyInfo.GetValue(target);
        }
        
        fieldInfo = targetType.GetField(propertyName, 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
            
        if (fieldInfo != null)
        {
            return (T)fieldInfo.GetValue(target);
        }
        
        Debug.LogWarning($"Не удалось найти свойство или поле {propertyName} в {targetType.Name}");
        return default(T);
    }
} 