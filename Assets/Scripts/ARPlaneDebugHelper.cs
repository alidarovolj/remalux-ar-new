using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;

/// <summary>
/// Скрипт-помощник для отладки AR плоскостей
/// </summary>
public class ARPlaneDebugHelper : MonoBehaviour
{
    [SerializeField]
    private ARPlaneManager planeManager;
    
    [SerializeField] 
    private ARSession arSession;
    
    [SerializeField]
    private float delayBeforeCheck = 3f;
    
    [SerializeField]
    private float checkInterval = 2f;
    
    [SerializeField]
    private bool logInfoToConsole = true;
    
    private GameObject lastCreatedPlane;
    private int planeCount = 0;
    
    void Start()
    {
        // Найти ARPlaneManager если не назначен
        if (planeManager == null)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
        }
        
        // Найти ARSession если не назначен
        if (arSession == null)
        {
            arSession = FindFirstObjectByType<ARSession>();
        }
        
        // Начать проверку с задержкой
        StartCoroutine(DelayedCheck());
        
        if (planeManager != null)
        {
            // Подписаться на событие изменения плоскостей
            planeManager.planesChanged += OnPlanesChanged;
            
            // Установить режим обнаружения с максимальной включенностью
            ForceEnablePlaneDetection();
        }
    }
    
    void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    /// <summary>
    /// Проверка статуса AR с задержкой
    /// </summary>
    private IEnumerator DelayedCheck()
    {
        // Первая задержка
        yield return new WaitForSeconds(delayBeforeCheck);
        
        // Проверить состояние AR
        CheckARStatus();
        
        // Повторять периодически
        while (true)
        {
            yield return new WaitForSeconds(checkInterval);
            CheckARPlanes();
        }
    }
    
    /// <summary>
    /// Проверяет общий статус AR (сессия, инициализация)
    /// </summary>
    private void CheckARStatus()
    {
        LogInfo("=== AR STATUS REPORT ===");
        
        // Проверить состояние AR сессии
        LogInfo($"AR Session state: {ARSession.state}");
        
        // Проверить активность состояние
        if (arSession != null)
        {
            LogInfo($"AR Session enabled: {arSession.enabled}");
        }
        else
        {
            LogInfo("WARNING: ARSession component not found");
        }
        
        // Проверить состояние менеджера плоскостей
        if (planeManager != null)
        {
            LogInfo($"AR Plane Manager enabled: {planeManager.enabled}");
            LogInfo($"Plane detection mode: {planeManager.requestedDetectionMode}");
            
            if (planeManager.planePrefab == null)
            {
                LogInfo("WARNING: Plane prefab not assigned!");
            }
            else
            {
                var meshRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    LogInfo($"Plane prefab has mesh renderer: {meshRenderer != null}, enabled: {meshRenderer.enabled}");
                    if (meshRenderer.sharedMaterial != null)
                    {
                        LogInfo($"Plane material: {meshRenderer.sharedMaterial.name}, shader: {meshRenderer.sharedMaterial.shader.name}");
                    }
                    else
                    {
                        LogInfo("WARNING: Plane prefab has no material assigned!");
                    }
                }
                else
                {
                    LogInfo("WARNING: Plane prefab missing MeshRenderer component!");
                }
            }
        }
        else
        {
            LogInfo("WARNING: ARPlaneManager component not found");
        }
        
        LogInfo("=== END OF STATUS ===");
    }
    
    /// <summary>
    /// Проверяет состояние AR плоскостей
    /// </summary>
    private void CheckARPlanes()
    {
        if (planeManager == null) return;
        
        int currentCount = 0;
        foreach (ARPlane plane in planeManager.trackables)
        {
            currentCount++;
        }
        
        LogInfo($"Current plane count: {currentCount}");
        
        if (currentCount > 0 && currentCount == planeCount)
        {
            // Плоскости обнаружены, но не прибавляются
            LogInfo("AR planes detected but not changing. Check if tracking is still active.");
            
            // Принудительно переприменить материалы
            ApplyCustomMaterials();
        }
        
        planeCount = currentCount;
    }
    
    /// <summary>
    /// Вызывается при изменении плоскостей
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Проверяем добавленные плоскости
        if (args.added != null && args.added.Count > 0)
        {
            foreach (ARPlane plane in args.added)
            {
                LogInfo($"New plane detected: {plane.trackableId}, alignment: {plane.alignment}");
                lastCreatedPlane = plane.gameObject;
                
                // Проверить компоненты плоскости
                CheckPlaneComponents(plane);
            }
        }
        
        // Проверяем изменённые плоскости
        if (args.updated != null && args.updated.Count > 0)
        {
            LogInfo($"Updated planes: {args.updated.Count}");
        }
        
        // Проверяем удалённые плоскости
        if (args.removed != null && args.removed.Count > 0)
        {
            LogInfo($"Removed planes: {args.removed.Count}");
        }
    }
    
    /// <summary>
    /// Проверяет и логирует состояние компонентов плоскости
    /// </summary>
    private void CheckPlaneComponents(ARPlane plane)
    {
        // Проверить наличие MeshRenderer
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            LogInfo($"Plane MeshRenderer enabled: {meshRenderer.enabled}");
            
            if (meshRenderer.material != null)
            {
                LogInfo($"Plane material: {meshRenderer.material.name}, shader: {meshRenderer.material.shader.name}, color: {meshRenderer.material.color}");
            }
            else
            {
                LogInfo("WARNING: Plane has no material assigned!");
            }
        }
        else
        {
            LogInfo("WARNING: Plane missing MeshRenderer component!");
        }
        
        // Проверить наличие MeshFilter
        MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.mesh != null)
        {
            LogInfo($"Plane mesh vertex count: {meshFilter.mesh.vertexCount}");
        }
        else
        {
            LogInfo("WARNING: Plane missing valid mesh!");
        }
    }
    
    /// <summary>
    /// Принудительно применяет кастомные материалы ко всем плоскостям
    /// </summary>
    private void ApplyCustomMaterials()
    {
        // Создать яркий материал для плоскостей
        Material brightMaterial = new Material(Shader.Find("Unlit/Color"));
        if (brightMaterial == null)
        {
            brightMaterial = new Material(Shader.Find("Standard"));
        }
        
        // Установить яркий цвет
        brightMaterial.color = new Color(1f, 0f, 1f, 1f); // Яркий розовый
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                // Применить яркий материал
                meshRenderer.material = brightMaterial;
                
                // Включить рендерер принудительно
                meshRenderer.enabled = true;
                
                LogInfo($"Applied bright material to plane: {plane.trackableId}");
            }
        }
    }
    
    /// <summary>
    /// Принудительно включает обнаружение плоскостей
    /// </summary>
    private void ForceEnablePlaneDetection()
    {
        if (planeManager == null) return;
        
        // Включить компонент
        planeManager.enabled = true;
        
        // Включить обнаружение обоих типов плоскостей
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        
        LogInfo("Forced enable plane detection for both horizontal and vertical planes");
    }
    
    /// <summary>
    /// Логирует информацию, если включено логирование
    /// </summary>
    private void LogInfo(string message)
    {
        if (logInfoToConsole)
        {
            Debug.Log($"<color=cyan>[ARPlaneDebug]</color> {message}");
        }
    }
} 