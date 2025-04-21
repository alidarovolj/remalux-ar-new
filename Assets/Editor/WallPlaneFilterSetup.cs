using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

public class WallPlaneFilterSetup : Editor
{
    [MenuItem("AR Wall Detection/Add Wall Plane Filter")]
    public static void AddWallPlaneFilter()
    {
        // Пытаемся найти ARPlaneManager
        ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
        
        if (planeManager == null)
        {
            // Если не нашли, проверяем XR Origin и создаем на нем
            XROrigin xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogError("XR Origin not found in scene! Cannot add WallPlaneFilter.");
                EditorUtility.DisplayDialog("Setup Error", 
                    "XR Origin not found in scene! Please add XR Origin first.", "OK");
                return;
            }
            
            // Добавляем ARPlaneManager на XR Origin, если его нет
            planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            Debug.Log("Added ARPlaneManager to XR Origin with Vertical detection mode");
        }
        
        // Проверяем, существует ли уже компонент WallPlaneFilter
        WallPlaneFilter existingFilter = planeManager.gameObject.GetComponent<WallPlaneFilter>();
        if (existingFilter != null)
        {
            Debug.Log("WallPlaneFilter already exists on " + planeManager.gameObject.name);
            
            // Обновляем настройки фильтра
            existingFilter.minPlaneArea = 0.5f;
            existingFilter.minVerticalCos = 0.8f;
            
            EditorUtility.SetDirty(existingFilter);
            Debug.Log("Updated existing WallPlaneFilter settings");
            
            EditorUtility.DisplayDialog("WallPlaneFilter Setup", 
                "WallPlaneFilter already exists on " + planeManager.gameObject.name + ". Settings have been updated.", "OK");
            return;
        }
        
        // Добавляем WallPlaneFilter
        WallPlaneFilter filter = planeManager.gameObject.AddComponent<WallPlaneFilter>();
        filter.minPlaneArea = 0.5f;
        filter.minVerticalCos = 0.8f;
        
        // Уточняем режим обнаружения плоскостей для ARPlaneManager
        planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
        
        Debug.Log("Added WallPlaneFilter to " + planeManager.gameObject.name);
        EditorUtility.SetDirty(planeManager.gameObject);
        
        EditorUtility.DisplayDialog("WallPlaneFilter Setup", 
            "WallPlaneFilter added to " + planeManager.gameObject.name + " successfully!", "OK");
    }
    
    // Проверяем доступность команды меню
    [MenuItem("AR Wall Detection/Add Wall Plane Filter", true)]
    public static bool ValidateAddWallPlaneFilter()
    {
        // Команда доступна, если есть XR Origin или ARPlaneManager
        return FindObjectOfType<XROrigin>() != null || FindObjectOfType<ARPlaneManager>() != null;
    }
} 