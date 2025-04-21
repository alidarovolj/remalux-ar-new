using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

[RequireComponent(typeof(ARPlaneManager))]
public class WallPlaneFilter : MonoBehaviour
{
    [Tooltip("Минимальная площадь плоскости (в м²) для считания 'стеной'")]
    public float minPlaneArea = 0.5f;

    [Tooltip("Минимальный косинус угла между нормалью плоскости и Vector3.up.\n" +
             "Например, 0.8 → отклонение от вертикали ≤ 36°")]
    [Range(0f, 1f)]
    public float minVerticalCos = 0.8f;

    ARPlaneManager planeManager;

    void OnEnable()
    {
        planeManager = GetComponent<ARPlaneManager>();
        planeManager.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Проверяем добавленные и обновлённые плоскости
        foreach (var plane in args.added)
            FilterPlane(plane);
        foreach (var plane in args.updated)
            FilterPlane(plane);
    }

    void FilterPlane(ARPlane plane)
    {
        // 1) Убираем subsumed-плоскости
        if (plane.subsumedBy != null)
        {
            plane.gameObject.SetActive(false);
            return;
        }

        // 2) Оцениваем угол наклона
        // нормаль плоскости plane.normal в ARPlane.transform.up
        var normal = plane.transform.up;
        float cos = Vector3.Dot(normal, Vector3.up);
        // вертикальные плоскости имеют малый косинус с up, поэтому проверим обратное:
        if (Mathf.Abs(cos) > (1 - minVerticalCos))
        {
            // если косинус слишком близок к 1 или -1 → горизонталь, не стена
            plane.gameObject.SetActive(false);
            return;
        }

        // 3) Оцениваем площадь
        // plane.size даёт размеры вдоль локальных X и Z
        float area = plane.size.x * plane.size.y;
        if (area < minPlaneArea)
        {
            plane.gameObject.SetActive(false);
            return;
        }

        // Всё ок — это «стена», оставляем активным
        plane.gameObject.SetActive(true);
    }
} 