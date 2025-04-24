using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEditor;

#if UNITY_EDITOR
public class FixARStructure : MonoBehaviour
{
    [MenuItem("Tools/AR/Fix AR Components Structure")]
    public static void FixARComponentsStructure()
    {
        // 1. Найти XR Origin (или ARSessionOrigin)
        GameObject xrOrigin = FindXROrigin();
        if (xrOrigin == null)
        {
            Debug.LogError("XR Origin или ARSessionOrigin не найден в сцене!");
            return;
        }
        
        Debug.Log($"Найден XR Origin: {xrOrigin.name}");
        
        // 2. Найти ARMeshManager
        ARMeshManager meshManager = FindObjectOfType<ARMeshManager>();
        if (meshManager == null)
        {
            Debug.LogWarning("ARMeshManager не найден в сцене! Создаем новый...");
            GameObject newMeshManagerObj = new GameObject("AR Mesh Manager");
            meshManager = newMeshManagerObj.AddComponent<ARMeshManager>();
        }
        
        GameObject meshManagerObj = meshManager.gameObject;
        Debug.Log($"Найден ARMeshManager на объекте: {meshManagerObj.name}");
        
        // 3. Найти WallAligner
        Component wallAligner = meshManagerObj.GetComponent("WallAligner");
        if (wallAligner == null)
        {
            Debug.LogWarning("WallAligner не найден на объекте с ARMeshManager!");
        }
        else
        {
            Debug.Log("Найден WallAligner на том же объекте, что и ARMeshManager");
            
            // Проверка на дубликаты WallAligner
            Component[] wallAligners = meshManagerObj.GetComponents("WallAligner");
            if (wallAligners.Length > 1)
            {
                Debug.LogWarning($"Найдено несколько компонентов WallAligner ({wallAligners.Length}). Удаляем лишние...");
                // Оставляем только первый WallAligner
                for (int i = 1; i < wallAligners.Length; i++)
                {
                    DestroyImmediate(wallAligners[i]);
                }
            }
        }
        
        // 4. Переместить ARMeshManager под XR Origin, если он не там
        if (meshManagerObj.transform.parent != xrOrigin.transform)
        {
            Debug.Log($"Перемещаем {meshManagerObj.name} под {xrOrigin.name}");
            
            // Сохраняем мировую позицию/поворот перед перемещением
            Vector3 worldPos = meshManagerObj.transform.position;
            Quaternion worldRot = meshManagerObj.transform.rotation;
            
            meshManagerObj.transform.SetParent(xrOrigin.transform, false);
            
            // Восстанавливаем мировую позицию/поворот после перемещения в иерархии
            meshManagerObj.transform.position = worldPos;
            meshManagerObj.transform.rotation = worldRot;
        }
        
        // 5. Сбросить трансформ и масштаб
        meshManagerObj.transform.localPosition = Vector3.zero;
        meshManagerObj.transform.localRotation = Quaternion.identity;
        meshManagerObj.transform.localScale = Vector3.one;
        
        Debug.Log("Трансформ ARMeshManager сброшен на (0,0,0) с масштабом (1,1,1)");
        
        // 6. Проверка настроек ARMeshManager
        Debug.Log("Проверка настроек ARMeshManager...");
        if (meshManager.meshPrefab == null)
        {
            Debug.LogWarning("Mesh Prefab не назначен для ARMeshManager. Рекомендуется назначить префаб с MeshFilter и MeshRenderer.");
        }
        
        Debug.Log("Структура AR-компонентов исправлена!");
        
        // Для удобства выбираем объект в иерархии
        Selection.activeGameObject = meshManagerObj;
    }
    
    private static GameObject FindXROrigin()
    {
        // Сначала ищем новый XR Origin (AR Foundation 5.0+)
        var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
            return xrOrigin.gameObject;
        
        // Затем ищем ARSessionOrigin (старые версии)
        var arSessionOrigin = FindObjectOfType<ARSessionOrigin>();
        if (arSessionOrigin != null)
            return arSessionOrigin.gameObject;
        
        return null;
    }
}
#endif 