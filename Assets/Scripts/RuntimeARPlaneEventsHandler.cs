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
    
    // Флаг для отслеживания, подписаны ли мы на события
    private bool isSubscribed = false;
    
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
                // Если уже есть плеймаменджер и мы подписаны на события, отписываемся
                UnsubscribeFromEvents();
                
                // Устанавливаем новый плейнменеджер
                planeManager = value;
                
                // Если компонент активен, подписываемся на события
                if (isActiveAndEnabled && planeManager != null)
                {
                    SubscribeToEvents();
                }
            }
        }
    }
    
    /// <summary>
    /// Публичное свойство для установки родительского объекта trackables
    /// </summary>
    public Transform CustomTrackablesParent
    {
        get { return customTrackablesParent; }
        set { customTrackablesParent = value; }
    }
    
    /// <summary>
    /// Публичный метод для установки ARPlaneManager
    /// </summary>
    public void SetPlaneManager(ARPlaneManager manager)
    {
        PlaneManager = manager;
    }
    
    /// <summary>
    /// Публичный метод для установки родительского объекта trackables
    /// </summary>
    public void SetTrackablesParent(Transform parent)
    {
        customTrackablesParent = parent;
    }
    
    /// <summary>
    /// Публичные свойства для настройки цветов
    /// </summary>
    public Color HorizontalPlaneColor
    {
        get { return horizontalPlaneColor; }
        set { horizontalPlaneColor = value; }
    }
    
    public Color VerticalPlaneColor
    {
        get { return verticalPlaneColor; }
        set { verticalPlaneColor = value; }
    }
    
    public Color OtherPlaneColor
    {
        get { return otherPlaneColor; }
        set { otherPlaneColor = value; }
    }
    
    /// <summary>
    /// Публичные свойства для настройки прозрачности
    /// </summary>
    public float HorizontalPlaneOpacity
    {
        get { return horizontalPlaneOpacity; }
        set { horizontalPlaneOpacity = Mathf.Clamp01(value); }
    }
    
    public float VerticalPlaneOpacity
    {
        get { return verticalPlaneOpacity; }
        set { verticalPlaneOpacity = Mathf.Clamp01(value); }
    }
    
    public float OtherPlaneOpacity
    {
        get { return otherPlaneOpacity; }
        set { otherPlaneOpacity = Mathf.Clamp01(value); }
    }
    
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
        SubscribeToEvents();
    }
    
    private void OnDisable()
    {
        UnsubscribeFromEvents();
    }
    
    private void OnDestroy()
    {
        // Убедимся, что мы отписались при уничтожении объекта
        UnsubscribeFromEvents();
    }
    
    /// <summary>
    /// Подписка на события ARPlaneManager
    /// </summary>
    private void SubscribeToEvents()
    {
        if (planeManager != null && !isSubscribed)
        {
            // Убедимся, что для обнаружения включены оба типа плоскостей
            #if UNITY_2020_1_OR_NEWER || UNITY_2021_1_OR_NEWER || UNITY_2022_1_OR_NEWER
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            #else
            planeManager.detectionMode = PlaneDetectionFlags.Horizontal | PlaneDetectionFlags.Vertical;
            #endif
            
            // Подписываемся на события
            planeManager.planesChanged += OnPlanesChanged;
            isSubscribed = true;
            
            Debug.Log("RuntimeARPlaneEventsHandler: Подписка на события обнаружения плоскостей");
        }
    }
    
    /// <summary>
    /// Отписка от событий ARPlaneManager
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (planeManager != null && isSubscribed)
        {
            // Отписываемся от событий
            planeManager.planesChanged -= OnPlanesChanged;
            isSubscribed = false;
            
            Debug.Log("RuntimeARPlaneEventsHandler: Отписка от событий обнаружения плоскостей");
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
        
        // Также можно обрабатывать удаленные плоскости, если нужно
        // if (args.removed != null && args.removed.Count > 0) { ... }
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