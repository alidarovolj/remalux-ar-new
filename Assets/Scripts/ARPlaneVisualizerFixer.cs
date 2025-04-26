using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Класс для исправления настроек визуализатора AR-плоскостей
/// </summary>
public class ARPlaneVisualizerFixer : MonoBehaviour
{
    // Цвет для горизонтальных плоскостей
    public Color horizontalPlaneColor = new Color(0.0f, 0.7f, 1.0f, 0.6f);
    
    // Цвет для вертикальных плоскостей
    public Color verticalPlaneColor = new Color(1.0f, 0.5f, 0.0f, 0.6f);
    
    void Awake()
    {
        Debug.Log("Запуск ARPlaneVisualizerFixer для исправления визуализаторов плоскостей...");
        FixPlaneVisualizers();
    }
    
    void Start()
    {
        // Обработка после небольшой задержки (для обеспечения работы с уже созданными объектами)
        Invoke("FixPlaneVisualizersDelayed", 1.0f);
    }
    
    /// <summary>
    /// Исправляет все визуализаторы AR-плоскостей в сцене
    /// </summary>
    public void FixPlaneVisualizers()
    {
        // Находим ARPlaneManager
        ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager == null)
        {
            Debug.LogWarning("ARPlaneVisualizerFixer: ARPlaneManager не найден в сцене.");
            return;
        }
        
        // Исправляем префаб визуализатора, если он задан
        if (planeManager.planePrefab != null)
        {
            FixPlanePrefab(planeManager.planePrefab);
        }
        else
        {
            Debug.LogWarning("ARPlaneVisualizerFixer: Префаб плоскости не задан в ARPlaneManager.");
            // Создаем новый префаб визуализатора
            CreateAndAssignPlanePrefab(planeManager);
        }
        
        // Включаем PlaneManager если он выключен
        if (!planeManager.enabled)
        {
            planeManager.enabled = true;
            Debug.Log("ARPlaneVisualizerFixer: ARPlaneManager был выключен, теперь включен");
        }
        
        // Исправляем все существующие плоскости
        foreach (ARPlane plane in planeManager.trackables)
        {
            FixPlaneVisualization(plane.gameObject);
        }
        
        Debug.Log("ARPlaneVisualizerFixer: Настройка визуализаторов AR-плоскостей завершена");
    }
    
    /// <summary>
    /// Исправляет настройки отдельного визуализатора плоскости
    /// </summary>
    private void FixPlaneVisualization(GameObject planeObject)
    {
        if (planeObject == null) return;
        
        // Получаем визуализатор
        ARPlaneMeshVisualizer visualizer = planeObject.GetComponent<ARPlaneMeshVisualizer>();
        if (visualizer == null)
        {
            visualizer = planeObject.AddComponent<ARPlaneMeshVisualizer>();
            Debug.Log($"ARPlaneVisualizerFixer: Добавлен ARPlaneMeshVisualizer к {planeObject.name}");
        }
        
        // Устанавливаем видимость всех состояний отслеживания
        var trackingStateVisibilityProperty = visualizer.GetType().GetProperty("trackingStateVisibility");
        if (trackingStateVisibilityProperty != null)
        {
            trackingStateVisibilityProperty.SetValue(visualizer, 0); // 0 = All
            Debug.Log($"ARPlaneVisualizerFixer: Установлена видимость всех состояний для {planeObject.name}");
        }
        
        // Проверяем наличие MeshFilter и MeshRenderer
        MeshFilter meshFilter = planeObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            if (meshFilter == null)
                meshFilter = planeObject.AddComponent<MeshFilter>();
                
            // Создаем меш если его нет
            Mesh mesh = new Mesh();
            mesh.name = "AR Plane Mesh";
            
            // Создаем простой quad
            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(-0.5f, 0, -0.5f),
                new Vector3(0.5f, 0, -0.5f),
                new Vector3(-0.5f, 0, 0.5f),
                new Vector3(0.5f, 0, 0.5f)
            };
            
            int[] triangles = new int[6]
            {
                0, 2, 1,
                1, 2, 3
            };
            
            Vector2[] uv = new Vector2[4]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            
            meshFilter.sharedMesh = mesh;
            Debug.Log($"ARPlaneVisualizerFixer: Создан и назначен новый меш для {planeObject.name}");
        }
        
        MeshRenderer meshRenderer = planeObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = planeObject.AddComponent<MeshRenderer>();
        }
        
        // Создаем материал с подходящими настройками
        ARPlane plane = planeObject.GetComponent<ARPlane>();
        Material material;
        
        if (plane != null && plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
        {
            material = CreatePlaneMaterial(verticalPlaneColor);
        }
        else
        {
            material = CreatePlaneMaterial(horizontalPlaneColor);
        }
        
        meshRenderer.sharedMaterial = material;
        meshRenderer.enabled = true;
        
        Debug.Log($"ARPlaneVisualizerFixer: Обновлен материал для {planeObject.name}");
    }
    
    /// <summary>
    /// Исправляет настройки префаба визуализатора плоскости
    /// </summary>
    private void FixPlanePrefab(GameObject prefab)
    {
        if (prefab == null) return;
        
        // Проверяем наличие всех необходимых компонентов в префабе
        ARPlaneMeshVisualizer visualizer = prefab.GetComponent<ARPlaneMeshVisualizer>();
        if (visualizer == null)
        {
            // В режиме редактора не можем напрямую изменить префаб
            Debug.LogWarning("ARPlaneVisualizerFixer: Префаб не содержит ARPlaneMeshVisualizer, но мы не можем его изменить во время выполнения");
            return;
        }
        
        MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = prefab.GetComponent<MeshRenderer>();
        
        if (meshFilter == null || meshRenderer == null)
        {
            Debug.LogWarning("ARPlaneVisualizerFixer: Префаб не содержит MeshFilter или MeshRenderer, но мы не можем его изменить во время выполнения");
            return;
        }
        
        Debug.Log("ARPlaneVisualizerFixer: Префаб визуализатора проверен");
    }
    
    /// <summary>
    /// Создает и назначает новый префаб плоскости для ARPlaneManager
    /// </summary>
    private void CreateAndAssignPlanePrefab(ARPlaneManager planeManager)
    {
        // Создаем новый GameObject во время выполнения
        GameObject planeVisualizer = new GameObject("AR Plane Visualizer");
        
        // Добавляем необходимые компоненты
        planeVisualizer.AddComponent<ARPlane>();
        planeVisualizer.AddComponent<ARPlaneMeshVisualizer>();
        
        MeshFilter meshFilter = planeVisualizer.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeVisualizer.AddComponent<MeshRenderer>();
        
        // Создаем сетку
        Mesh mesh = new Mesh();
        mesh.name = "AR Plane Mesh";
        
        // Вершины для квадрата размером 1x1
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-0.5f, 0, -0.5f),
            new Vector3(0.5f, 0, -0.5f),
            new Vector3(-0.5f, 0, 0.5f),
            new Vector3(0.5f, 0, 0.5f)
        };
        
        // Треугольники
        int[] triangles = new int[6]
        {
            0, 2, 1,
            1, 2, 3
        };
        
        // UV координаты
        Vector2[] uv = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        
        meshFilter.sharedMesh = mesh;
        
        // Создаем материал
        Material material = CreatePlaneMaterial(horizontalPlaneColor);
        meshRenderer.sharedMaterial = material;
        
        // Назначаем префаб
        planeManager.planePrefab = planeVisualizer;
        
        Debug.Log("ARPlaneVisualizerFixer: Создан и назначен новый префаб визуализатора плоскостей");
    }
    
    /// <summary>
    /// Создает полупрозрачный материал для AR плоскостей
    /// </summary>
    private Material CreatePlaneMaterial(Color color)
    {
        // Создаем подходящий материал
        Material material = new Material(Shader.Find("Transparent/Diffuse"));
        if (material.shader == null)
        {
            // Запасной вариант если шейдер не найден
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            // Настраиваем для URP если используется
            material.SetFloat("_Surface", 1); // Transparent
            material.SetFloat("_Blend", 0); // SrcAlpha
            material.SetInt("_ZWrite", 0); // Disable Z write
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
        
        // Устанавливаем цвет
        material.color = color;
        
        // Включаем двустороннее отображение
        material.SetFloat("_Cull", 0); // Off
        
        return material;
    }
    
    /// <summary>
    /// Отложенная фиксация визуализаторов
    /// </summary>
    private void FixPlaneVisualizersDelayed()
    {
        FixPlaneVisualizers();
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(ARPlaneVisualizerFixer))]
public class ARPlaneVisualizerFixerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        ARPlaneVisualizerFixer fixer = (ARPlaneVisualizerFixer)target;
        
        if (GUILayout.Button("Исправить визуализаторы плоскостей"))
        {
            fixer.FixPlaneVisualizers();
        }
    }
}
#endif 