using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

[RequireComponent(typeof(ARPlaneManager))]
public class MeshSurfaceVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private Material surfaceMaterial;
    [SerializeField] private bool showPlanes = true;
    [SerializeField] private float planeOpacity = 0.5f;
    [SerializeField] private Color planeColor = new Color(0, 0.8f, 1f, 0.5f);

    private ARPlaneManager planeManager;
    private Dictionary<ARPlane, GameObject> planeVisualizers = new Dictionary<ARPlane, GameObject>();

    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
    }

    private void OnEnable()
    {
        planeManager.planesChanged += OnPlanesChanged;
        
        // Включаем отображение плоскостей
        SetPlanesActive(showPlanes);
    }

    private void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обработка добавленных плоскостей
        foreach (var plane in args.added)
        {
            CreatePlaneVisualizer(plane);
        }

        // Обработка обновленных плоскостей
        foreach (var plane in args.updated)
        {
            UpdatePlaneVisualizer(plane);
        }

        // Обработка удаленных плоскостей
        foreach (var plane in args.removed)
        {
            RemovePlaneVisualizer(plane);
        }
    }

    private void CreatePlaneVisualizer(ARPlane plane)
    {
        // Создаем объект для визуализации
        GameObject visualizer = new GameObject($"PlaneVisualizer_{plane.trackableId}");
        visualizer.transform.parent = plane.transform;
        visualizer.transform.localPosition = Vector3.zero;
        visualizer.transform.localRotation = Quaternion.identity;

        // Добавляем компоненты для отображения
        MeshFilter meshFilter = visualizer.AddComponent<MeshFilter>();
        MeshRenderer renderer = visualizer.AddComponent<MeshRenderer>();

        // Настраиваем материал
        if (surfaceMaterial != null)
        {
            Material instanceMaterial = new Material(surfaceMaterial);
            instanceMaterial.color = planeColor;
            renderer.material = instanceMaterial;
        }

        // Обновляем меш из компонента MeshFilter плоскости
        var planeMeshFilter = plane.GetComponent<MeshFilter>();
        if (planeMeshFilter != null && planeMeshFilter.mesh != null)
        {
            meshFilter.mesh = planeMeshFilter.mesh;
        }

        // Сохраняем в словарь
        planeVisualizers[plane] = visualizer;
    }

    private void UpdatePlaneVisualizer(ARPlane plane)
    {
        if (planeVisualizers.TryGetValue(plane, out GameObject visualizer))
        {
            // Обновляем меш
            MeshFilter meshFilter = visualizer.GetComponent<MeshFilter>();
            var planeMeshFilter = plane.GetComponent<MeshFilter>();
            if (meshFilter != null && planeMeshFilter != null && planeMeshFilter.mesh != null)
            {
                meshFilter.mesh = planeMeshFilter.mesh;
            }
        }
    }

    private void RemovePlaneVisualizer(ARPlane plane)
    {
        if (planeVisualizers.TryGetValue(plane, out GameObject visualizer))
        {
            Destroy(visualizer);
            planeVisualizers.Remove(plane);
        }
    }

    public void SetPlanesActive(bool active)
    {
        showPlanes = active;
        foreach (var visualizer in planeVisualizers.Values)
        {
            if (visualizer != null)
            {
                visualizer.SetActive(active);
            }
        }
    }

    public void SetPlaneOpacity(float opacity)
    {
        planeOpacity = Mathf.Clamp01(opacity);
        Color newColor = planeColor;
        newColor.a = planeOpacity;

        foreach (var visualizer in planeVisualizers.Values)
        {
            if (visualizer != null)
            {
                MeshRenderer renderer = visualizer.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.color = newColor;
                }
            }
        }
    }

    public void TogglePlanes()
    {
        SetPlanesActive(!showPlanes);
    }
} 