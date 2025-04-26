using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using System.Collections.Generic;

/// <summary>
/// Script to fix AR camera setup to use native ARCameraBackground for displaying the camera feed
/// instead of relying on a RawImage.
/// </summary>
public static class ARCameraSetupFix
{
    [MenuItem("AR/Fix Camera Display (Use Native AR Background)")]
    public static void FixARCameraDisplay()
    {
        // Step 1: Find the AR Camera and ensure it has the necessary components
        Camera arCamera = FindARCamera();
        if (arCamera == null)
        {
            Debug.LogError("Could not find AR Camera. Make sure your scene has a properly set up XR Origin.");
            return;
        }

        // Step 2: Make sure the AR Camera has ARCameraManager and ARCameraBackground components
        EnsureARCameraComponents(arCamera.gameObject);

        // Step 3: Find and modify Canvas to use Camera Space rendering
        FixCanvas(arCamera);

        // Step 4: Remove AR Display RawImage if it exists
        RemoveARDisplay();
        
        // Step 5: Check Graphics settings
        CheckGraphicsSettings();

        Debug.Log("Camera display setup complete. The AR camera feed will now be rendered natively through ARCameraBackground.");
    }

    /// <summary>
    /// Find the AR Camera in the scene
    /// </summary>
    private static Camera FindARCamera()
    {
        // Try to find camera through XROrigin first
        XROrigin origin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (origin != null && origin.Camera != null)
        {
            return origin.Camera;
        }

        // Then try to find by name
        GameObject arCameraObj = GameObject.Find("AR Camera");
        if (arCameraObj != null)
        {
            return arCameraObj.GetComponent<Camera>();
        }

        // Last resort - check main camera
        return Camera.main;
    }

    /// <summary>
    /// Ensure the AR Camera has the required components
    /// </summary>
    private static void EnsureARCameraComponents(GameObject cameraObject)
    {
        // Add ARCameraManager if not present
        ARCameraManager cameraManager = cameraObject.GetComponent<ARCameraManager>();
        if (cameraManager == null)
        {
            cameraManager = cameraObject.AddComponent<ARCameraManager>();
            Debug.Log("Added ARCameraManager to AR Camera");
        }

        // Add ARCameraBackground if not present
        ARCameraBackground cameraBackground = cameraObject.GetComponent<ARCameraBackground>();
        if (cameraBackground == null)
        {
            cameraBackground = cameraObject.AddComponent<ARCameraBackground>();
            Debug.Log("Added ARCameraBackground to AR Camera");
        }
    }

    /// <summary>
    /// Fix Canvas to use Camera space rendering
    /// </summary>
    private static void FixCanvas(Camera arCamera)
    {
        // Find all canvases in the scene
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        
        foreach (Canvas canvas in canvases)
        {
            // Skip canvases that are not the main UI Canvas
            if (canvas.name != "UI Canvas")
                continue;
                
            // Change render mode
            if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
            {
                Undo.RecordObject(canvas, "Change Canvas Render Mode");
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = arCamera;
                canvas.planeDistance = 1.0f;
                Debug.Log($"Changed Canvas '{canvas.name}' to use Camera rendering mode");
            }
            else if (canvas.worldCamera == null)
            {
                Undo.RecordObject(canvas, "Set Canvas Camera");
                canvas.worldCamera = arCamera;
                Debug.Log($"Set camera reference for Canvas '{canvas.name}'");
            }
        }
    }

    /// <summary>
    /// Remove AR Display RawImage if it exists
    /// </summary>
    private static void RemoveARDisplay()
    {
        // Find "AR Display" by name
        GameObject arDisplay = GameObject.Find("AR Display");
        if (arDisplay != null && arDisplay.GetComponent<RawImage>() != null)
        {
            Undo.DestroyObjectImmediate(arDisplay);
            Debug.Log("Removed AR Display RawImage as it's no longer needed");
        }
        else
        {
            // Try to find through canvas hierarchy
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                Transform displayTransform = canvas.transform.Find("AR Display");
                if (displayTransform != null && displayTransform.GetComponent<RawImage>() != null)
                {
                    Undo.DestroyObjectImmediate(displayTransform.gameObject);
                    Debug.Log("Removed AR Display RawImage from Canvas");
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Check graphics settings and provide guidance
    /// </summary>
    private static void CheckGraphicsSettings()
    {
        // We can't automatically change URP settings, so just provide guidance
        Debug.Log("Important: If using URP, please enable 'Opaque Texture' in your " +
                  "Project Settings → Graphics → Scriptable Render Pipeline Settings");
    }

    [MenuItem("AR/Fix XROrigin Camera Reference", priority = 110)]
    public static void FixXROriginCameraReference()
    {
        // Ищем AR Session в сцене
        ARSession arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
        if (arSession == null)
        {
            EditorUtility.DisplayDialog("AR Camera Setup", "AR Session не найден в сцене. Сначала настройте AR сцену.", "OK");
            return;
        }

        // Ищем XROrigin в сцене
        XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
        if (xrOrigin == null)
        {
            EditorUtility.DisplayDialog("AR Camera Setup", "XROrigin не найден в сцене. Сначала настройте AR сцену.", "OK");
            return;
        }

        // Проверяем ссылку на камеру
        if (xrOrigin.Camera != null)
        {
            // Камера уже настроена
            EditorUtility.DisplayDialog("AR Camera Setup", "Камера в XROrigin уже настроена корректно.", "OK");
            return;
        }

        // Ищем объект Camera Offset
        Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
        if (cameraOffset == null)
        {
            // Создаем Camera Offset, если его нет
            GameObject cameraOffsetObj = new GameObject("Camera Offset");
            cameraOffsetObj.transform.SetParent(xrOrigin.transform, false);
            cameraOffset = cameraOffsetObj.transform;
            Debug.Log("Создан новый Camera Offset объект");
        }

        // Ищем AR камеру под Camera Offset
        Camera arCamera = null;
        foreach (Transform child in cameraOffset)
        {
            arCamera = child.GetComponent<Camera>();
            if (arCamera != null && child.GetComponent<ARCameraManager>() != null)
            {
                break;
            }
            arCamera = null;
        }

        // Если AR камера не найдена, создаем новую
        if (arCamera == null)
        {
            GameObject arCameraObj = new GameObject("AR Camera");
            arCameraObj.transform.SetParent(cameraOffset, false);
            arCamera = arCameraObj.AddComponent<Camera>();
            arCameraObj.AddComponent<ARCameraManager>();
            arCameraObj.AddComponent<ARCameraBackground>();
            Debug.Log("Создана новая AR камера с компонентами");
        }

        // Используем SerializedObject для доступа к приватным полям в XROrigin
        SerializedObject serializedObject = new SerializedObject(xrOrigin);
        SerializedProperty cameraProp = serializedObject.FindProperty("m_Camera");
        SerializedProperty cameraOffsetProp = serializedObject.FindProperty("m_CameraFloorOffsetObject");

        // Устанавливаем ссылки
        cameraProp.objectReferenceValue = arCamera;
        cameraOffsetProp.objectReferenceValue = cameraOffset.gameObject;

        // Применяем изменения
        serializedObject.ApplyModifiedProperties();

        Debug.Log("Камера в XROrigin успешно настроена");
        EditorUtility.DisplayDialog("AR Camera Setup", "Камера в XROrigin успешно настроена.", "OK");
    }
} 