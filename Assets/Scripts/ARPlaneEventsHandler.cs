using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

/// <summary>
/// Обработчик событий для плоскостей AR. Обеспечивает корректное обнаружение вертикальных плоскостей (стен).
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneEventsHandler : MonoBehaviour
{
    private ARPlaneManager planeManager;
    private Transform trackablesParent;
    private Transform customTrackablesParent;
    
    private bool isInitialized = false;
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
            if (planeManager == null)
            {
                Debug.LogError("ARPlaneEventsHandler: ARPlaneManager not found");
                enabled = false;
                return;
            }
        }
        
        // Найдем или создадим контейнер для хранения обнаруженных плоскостей
        GameObject trackablesContainer = GameObject.Find("AR Plane Visualizer/Trackables");
        if (trackablesContainer != null)
        {
            customTrackablesParent = trackablesContainer.transform;
        }
    }
    
    private void OnEnable()
    {
        if (planeManager != null)
        {
            // Включаем обнаружение вертикальных плоскостей
            #if UNITY_2020_1_OR_NEWER
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            #else
            planeManager.detectionMode = PlaneDetectionFlags.Horizontal | PlaneDetectionFlags.Vertical;
            #endif
            
            // Подписываемся на события
            planeManager.planesChanged += OnPlanesChanged;
            
            Debug.Log("ARPlaneEventsHandler: Enabled detection of vertical planes (walls)");
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
        // Обработка добавленных плоскостей
        ProcessAddedPlanes(args.added);
        
        // Обработка обновленных плоскостей
        ProcessUpdatedPlanes(args.updated);
    }
    
    private void ProcessAddedPlanes(List<ARPlane> addedPlanes)
    {
        if (addedPlanes == null || addedPlanes.Count == 0)
            return;
        
        foreach (ARPlane plane in addedPlanes)
        {
            ConfigurePlane(plane);
        }
    }
    
    private void ProcessUpdatedPlanes(List<ARPlane> updatedPlanes)
    {
        if (updatedPlanes == null || updatedPlanes.Count == 0)
            return;
        
        foreach (ARPlane plane in updatedPlanes)
        {
            // При необходимости можно обновить конфигурацию плоскости
            // ConfigurePlane(plane);
        }
    }
    
    private void ConfigurePlane(ARPlane plane)
    {
        if (plane == null)
            return;
        
        // Переместим объект плоскости в наш визуализатор, если нужно
        if (customTrackablesParent != null && plane.transform.parent != customTrackablesParent)
        {
            plane.transform.SetParent(customTrackablesParent, true);
        }
        
        // Настроим внешний вид плоскости в зависимости от ее ориентации
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            // Настройка для вертикальных плоскостей (стен)
            SetPlaneAppearance(plane, Color.blue, 0.5f);
            plane.gameObject.name = $"ARWall_{plane.trackableId.ToString().Substring(0, 8)}";
        }
        else if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
        {
            // Настройка для горизонтальных плоскостей (пол, потолок, столы)
            SetPlaneAppearance(plane, Color.green, 0.3f);
            plane.gameObject.name = $"ARFloor_{plane.trackableId.ToString().Substring(0, 8)}";
        }
        else
        {
            // Настройка для наклонных плоскостей
            SetPlaneAppearance(plane, Color.yellow, 0.4f);
            plane.gameObject.name = $"ARPlane_{plane.trackableId.ToString().Substring(0, 8)}";
        }
    }
    
    private void SetPlaneAppearance(ARPlane plane, Color color, float opacity)
    {
        // Найдем визуализатор на объекте плоскости
        var meshRenderer = plane.GetComponentInChildren<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.material != null)
        {
            // Установим цвет материала в зависимости от типа плоскости
            Color planeColor = color;
            planeColor.a = opacity;
            
            // Создадим новый материал, чтобы избежать изменения общего материала
            Material newMaterial = new Material(meshRenderer.material);
            newMaterial.color = planeColor;
            
            meshRenderer.material = newMaterial;
        }
    }
} 