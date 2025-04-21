using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

[RequireComponent(typeof(ARRaycastManager))]
public class SurfaceDetector : MonoBehaviour
{
    [SerializeField]
    private Camera arCamera;
    
    [SerializeField]
    private float detectionInterval = 0.5f; // Интервал между сканированиями в секундах
    
    [SerializeField]
    private int raysPerFrame = 9; // Количество лучей для сканирования (3x3 сетка)
    
    private ARRaycastManager raycastManager;
    private float timer;
    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    
    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        
        // Если камера не назначена, попробуем найти AR камеру
        if (arCamera == null)
        {
            var arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager != null)
            {
                arCamera = arCameraManager.GetComponent<Camera>();
            }
            
            if (arCamera == null)
            {
                Debug.LogError("AR Camera not found! Please assign it in the inspector.");
            }
        }
    }
    
    private void Update()
    {
        timer += Time.deltaTime;
        
        if (timer >= detectionInterval)
        {
            ScanForSurfaces();
            timer = 0;
        }
    }
    
    private void ScanForSurfaces()
    {
        if (arCamera == null) return;
        
        // Создаем сетку 3x3 для сканирования
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                // Вычисляем позицию луча в видовом пространстве (от 0.25 до 0.75 по обеим осям)
                Vector2 viewportPoint = new Vector2(
                    0.25f + (x * 0.25f),
                    0.25f + (y * 0.25f)
                );
                
                Ray ray = arCamera.ViewportPointToRay(viewportPoint);
                
                if (raycastManager.Raycast(ray, hits, TrackableType.PlaneWithinPolygon))
                {
                    foreach (ARRaycastHit hit in hits)
                    {
                        // Здесь можно обработать найденную поверхность
                        Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.green, detectionInterval);
                        OnSurfaceDetected(hit);
                    }
                }
                else
                {
                    Debug.DrawRay(ray.origin, ray.direction * 10f, Color.red, detectionInterval);
                }
            }
        }
    }
    
    private void OnSurfaceDetected(ARRaycastHit hit)
    {
        // Получаем позицию и поворот точки пересечения
        Vector3 hitPosition = hit.pose.position;
        Quaternion hitRotation = hit.pose.rotation;
        
        // Получаем тип поверхности
        TrackableId planeId = hit.trackableId;
        ARPlane plane = GetPlaneFromId(planeId);
        
        if (plane != null)
        {
            Debug.Log($"Surface detected at {hitPosition}. " +
                      $"Plane alignment: {plane.alignment}, " +
                      $"Size: {plane.size}");
        }
    }
    
    private ARPlane GetPlaneFromId(TrackableId id)
    {
        ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager != null)
        {
            return planeManager.GetPlane(id);
        }
        return null;
    }
} 