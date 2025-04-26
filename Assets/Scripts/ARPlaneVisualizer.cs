using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Компонент для визуализации AR плоскостей с использованием сетчатых материалов
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneVisualizer : MonoBehaviour
{
    [Tooltip("Включить или выключить отображение плоскостей")]
    public bool showPlanes = true;
    
    [Tooltip("Цвет для горизонтальных плоскостей")]
    public Color horizontalPlaneColor = new Color(0.0f, 0.7f, 1.0f, 0.5f);
    
    [Tooltip("Цвет для вертикальных плоскостей")]
    public Color verticalPlaneColor = new Color(1.0f, 0.5f, 0.0f, 0.5f);
    
    [Tooltip("Размер ячейки сетки материала в метрах")]
    public float gridSize = 0.1f;
    
    [Tooltip("Прозрачность материала от 0 до 1")]
    [Range(0.0f, 1.0f)]
    public float materialAlpha = 0.5f;
    
    private ARPlaneManager planeManager;
    private Material horizontalPlaneMaterial;
    private Material verticalPlaneMaterial;
    private Dictionary<TrackableId, Material> planeToMaterial = new Dictionary<TrackableId, Material>();
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
    }
    
    private void OnEnable()
    {
        if (planeManager != null)
        {
            // Создаем материалы для разных типов плоскостей
            horizontalPlaneMaterial = ARPlaneMaterial.CreateGridMaterial(horizontalPlaneColor, materialAlpha, gridSize);
            verticalPlaneMaterial = ARPlaneMaterial.CreateGridMaterial(verticalPlaneColor, materialAlpha, gridSize);
            
            // Подписываемся на события добавления и изменения плоскостей
            planeManager.planesChanged += OnPlanesChanged;
            
            // Применяем материалы к существующим плоскостям
            UpdatePlanesVisibility();
        }
        else
        {
            Debug.LogError("ARPlaneVisualizer: ARPlaneManager component not found");
        }
    }
    
    private void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обрабатываем новые плоскости
        foreach (ARPlane plane in args.added)
        {
            ApplyMaterialToPlane(plane);
        }
        
        // Обрабатываем измененные плоскости
        foreach (ARPlane plane in args.updated)
        {
            ApplyMaterialToPlane(plane);
        }
        
        // Очищаем удаленные плоскости
        foreach (ARPlane plane in args.removed)
        {
            if (planeToMaterial.ContainsKey(plane.trackableId))
            {
                planeToMaterial.Remove(plane.trackableId);
            }
        }
    }
    
    private void ApplyMaterialToPlane(ARPlane plane)
    {
        if (plane == null) return;
        
        // Получаем визуализатор сетки плоскости и рендерер
        ARPlaneMeshVisualizer meshVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        
        if (meshVisualizer == null || meshRenderer == null) return;
        
        // Выбор материала в зависимости от ориентации плоскости
        Material material = null;
        if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalUp || 
            plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.HorizontalDown)
        {
            material = horizontalPlaneMaterial;
        }
        else
        {
            material = verticalPlaneMaterial;
        }
        
        // Применяем материал и сохраняем ассоциацию
        meshRenderer.material = material;
        meshRenderer.enabled = showPlanes;
        
        // Сохраняем соответствие плоскости и материала
        if (!planeToMaterial.ContainsKey(plane.trackableId))
        {
            planeToMaterial[plane.trackableId] = material;
        }
    }
    
    /// <summary>
    /// Включает или выключает отображение всех плоскостей
    /// </summary>
    /// <param name="visible">Видимость плоскостей</param>
    public void SetPlanesVisible(bool visible)
    {
        showPlanes = visible;
        UpdatePlanesVisibility();
    }
    
    /// <summary>
    /// Обновляет видимость всех плоскостей на основе текущих настроек
    /// </summary>
    public void UpdatePlanesVisibility()
    {
        if (planeManager == null || planeManager.trackables == null) return;
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = showPlanes;
            }
        }
    }
    
    /// <summary>
    /// Переключает видимость плоскостей
    /// </summary>
    public void TogglePlanesVisibility()
    {
        SetPlanesVisible(!showPlanes);
    }
} 