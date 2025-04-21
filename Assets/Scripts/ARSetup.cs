using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARSetup : MonoBehaviour
{
    private void Awake()
    {
        // Проверяем и создаем AR Session если его нет
        if (FindObjectOfType<ARSession>() == null)
        {
            var arSession = new GameObject("AR Session");
            arSession.AddComponent<ARSession>();
            arSession.AddComponent<ARInputManager>();
        }

        // Проверяем и создаем AR Session Origin если его нет
        if (FindObjectOfType<ARSessionOrigin>() == null)
        {
            var arSessionOrigin = new GameObject("AR Session Origin");
            var sessionOrigin = arSessionOrigin.AddComponent<ARSessionOrigin>();

            // Создаем AR Camera
            var arCamera = new GameObject("AR Camera");
            arCamera.transform.SetParent(arSessionOrigin.transform);
            var camera = arCamera.AddComponent<Camera>();
            arCamera.AddComponent<ARCameraManager>();
            arCamera.AddComponent<ARCameraBackground>();

            // Настраиваем камеру
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = Color.black;

            sessionOrigin.camera = camera;

            // Добавляем необходимые AR компоненты
            var arSessionOriginGO = sessionOrigin.gameObject;
            arSessionOriginGO.AddComponent<ARPlaneManager>();
            arSessionOriginGO.AddComponent<ARRaycastManager>();
        }

        // Добавляем наш детектор поверхностей
        var originObject = FindObjectOfType<ARSessionOrigin>().gameObject;
        if (originObject.GetComponent<SurfaceDetector>() == null)
        {
            originObject.AddComponent<SurfaceDetector>();
        }

        // Включаем автофокус для камеры (обновленный код для новой версии ARFoundation)
        var cameraManager = FindObjectOfType<ARCameraManager>();
        if (cameraManager != null)
        {
            // Современные версии AR Foundation используют другой подход к настройке фокуса
            #if UNITY_IOS
            cameraManager.autoFocusRequested = true;
            #endif
        }

        Debug.Log("AR Setup completed. Required components have been added to the scene.");
    }
} 