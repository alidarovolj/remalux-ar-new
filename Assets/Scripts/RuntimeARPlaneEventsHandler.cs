using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

/// <summary>
/// Обработчик событий для плоскостей AR, который работает во время выполнения игры.
/// Обеспечивает корректное отображение вертикальных плоскостей (стен).
/// </summary>
public class RuntimeARPlaneEventsHandler : MonoBehaviour
{
    [SerializeField] 
    private ARPlaneManager planeManager;
    
    [SerializeField]
    private Color horizontalPlaneColor = Color.green;
    
    [SerializeField]
    private Color verticalPlaneColor = Color.blue;
    
    [SerializeField]
    private Color otherPlaneColor = Color.yellow;
    
    [SerializeField]
    private float horizontalPlaneOpacity = 0.3f;
    
    [SerializeField]
    private float verticalPlaneOpacity = 0.5f;
    
    [SerializeField]
    private float otherPlaneOpacity = 0.4f;
    
    [SerializeField]
    private Transform customTrackablesParent;
    
    private void Awake()
    {
        // Если planeManager не назначен, ищем его в сцене
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
            
            if (planeManager == null)
            {
                Debug.LogError("RuntimeARPlaneEventsHandler: ARPlaneManager не найден. Компонент будет отключен.");
                enabled = false;
                return;
            }
        }
        
        // Если не указан родительский объект для плоскостей, ищем GameObject с именем "Trackables"
        if (customTrackablesParent == null)
        {
            GameObject trackablesObj = GameObject.Find("AR Plane Visualizer/Trackables");
            if (trackablesObj != null)
            {
                customTrackablesParent = trackablesObj.transform;
            }
            else
            {
                // Создаем новый, если не нашли
                GameObject newTrackables = new GameObject("Trackables");
                customTrackablesParent = newTrackables.transform;
            }
        }
    }
    
    private void OnEnable()
    {
        if (planeManager != null)
        {
            // Убедимся, что для обнаружения включены оба типа плоскостей
            #if UNITY_2020_1_OR_NEWER
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            #else
            planeManager.detectionMode = PlaneDetectionFlags.Horizontal | PlaneDetectionFlags.Vertical;
            #endif
            
            // Подписываемся на события
            planeManager.planesChanged += OnPlanesChanged;
            
            Debug.Log("RuntimeARPlaneEventsHandler: Подписка на события обнаружения плоскостей");
        }
    }
    
    private void OnDisable()
    {
        if (planeManager != null)
        {
            // Отписываемся от событий
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обработка добавленных плоскостей
        if (args.added != null && args.added.Count > 0)
        {
            foreach (ARPlane plane in args.added)
            {
                ConfigurePlane(plane);
            }
        }
        
        // Обработка обновленных плоскостей
        if (args.updated != null && args.updated.Count > 0)
        {
            foreach (ARPlane plane in args.updated)
            {
                // Обновляем конфигурацию при каждом обновлении
                ConfigurePlane(plane);
            }
        }
    }
    
    /// <summary>
    /// Настраивает внешний вид и свойства AR плоскости в зависимости от ее ориентации
    /// </summary>
    private void ConfigurePlane(ARPlane plane)
    {
        if (plane == null)
            return;
        
        // Перемещаем объект плоскости в наш контейнер, если нужно
        if (customTrackablesParent != null && plane.transform.parent != customTrackablesParent)
        {
            plane.transform.SetParent(customTrackablesParent, true);
        }
        
        // Настраиваем внешний вид в зависимости от ориентации
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            // Вертикальные плоскости (стены)
            SetPlaneAppearance(plane, verticalPlaneColor, verticalPlaneOpacity);
            plane.gameObject.name = $"ARWall_{GetPlaneIdShort(plane)}";
        }
        else if (plane.alignment == PlaneAlignment.HorizontalUp || 
                 plane.alignment == PlaneAlignment.HorizontalDown)
        {
            // Горизонтальные плоскости (пол, столы)
            SetPlaneAppearance(plane, horizontalPlaneColor, horizontalPlaneOpacity);
            plane.gameObject.name = $"ARFloor_{GetPlaneIdShort(plane)}";
        }
        else
        {
            // Наклонные плоскости
            SetPlaneAppearance(plane, otherPlaneColor, otherPlaneOpacity);
            plane.gameObject.name = $"ARPlane_{GetPlaneIdShort(plane)}";
        }
    }
    
    /// <summary>
    /// Настраивает визуальное представление плоскости
    /// </summary>
    private void SetPlaneAppearance(ARPlane plane, Color color, float opacity)
    {
        // Ищем MeshRenderer в дочерних объектах
        var meshRenderer = plane.GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            // Создаем новый материал, чтобы не изменять общий материал
            Material newMaterial = new Material(meshRenderer.material);
            
            // Задаем цвет с прозрачностью
            Color planeColor = color;
            planeColor.a = opacity;
            newMaterial.color = planeColor;
            
            // Применяем новый материал
            meshRenderer.material = newMaterial;
        }
    }
    
    /// <summary>
    /// Получает короткую версию ID плоскости для использования в названии
    /// </summary>
    private string GetPlaneIdShort(ARPlane plane)
    {
        string fullId = plane.trackableId.ToString();
        
        // Берем первые 8 символов или меньше, если ID короче
        int length = Mathf.Min(8, fullId.Length);
        return fullId.Substring(0, length);
    }
} 