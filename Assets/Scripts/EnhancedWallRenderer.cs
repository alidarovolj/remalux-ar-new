using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ML.DeepLab;

/// <summary>
/// Улучшенный визуализатор стен с возможностью настройки параметров отображения и фильтрации
/// </summary>
public class EnhancedWallRenderer : MonoBehaviour
{
    [Header("Компоненты")]
    [Tooltip("Ссылка на ARCameraManager для получения текстуры")]
    public ARCameraManager ARCameraManager;
    
    [Tooltip("Ссылка на EnhancedDeepLabPredictor для получения сегментации")]
    public EnhancedDeepLabPredictor Predictor;
    
    [Header("Параметры визуализации")]
    [Tooltip("Материал для визуализации стен")]
    public Material WallMaterial;
    
    [Tooltip("Цвет стен по умолчанию")]
    public Color WallColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
    
    [Tooltip("Прозрачность стен (0-1)")]
    [Range(0.0f, 1.0f)]
    public float WallOpacity = 0.7f;
    
    [Header("Параметры фильтрации")]
    [Tooltip("Минимальная площадь стены в кв. метрах")]
    public float MinWallArea = 1.5f;
    
    [Tooltip("Порог вертикальности для плоскостей (0-1)")]
    [Range(0.0f, 1.0f)]
    public float VerticalThreshold = 0.6f;
    
    [Tooltip("Порог уверенности для распознавания стен (0-1)")]
    [Range(0.0f, 1.0f)]
    public float WallConfidenceThreshold = 0.4f;
    
    [Tooltip("ID класса стены в сегментационной модели")]
    public byte WallClassId = 9;
    
    [Header("Отладка")]
    [Tooltip("Показывать отладочную информацию")]
    public bool ShowDebugInfo = true;
    
    [Tooltip("Показывать все меши (включая отфильтрованные)")]
    public bool ShowAllMeshes = false;
    
    // Список созданных стен
    private List<GameObject> walls = new List<GameObject>();
    
    // Ссылка на оптимизатор стен
    private WallOptimizer wallOptimizer;
    
    private void Start()
    {
        // Если ссылки не назначены, пытаемся найти компоненты автоматически
        if (ARCameraManager == null)
        {
            ARCameraManager = FindObjectOfType<ARCameraManager>();
        }
        
        if (Predictor == null)
        {
            Predictor = FindObjectOfType<EnhancedDeepLabPredictor>();
        }
        
        // Создаем материал для стен, если он не назначен
        if (WallMaterial == null)
        {
            // Попробуем найти стандартный шейдер
            Shader standardShader = Shader.Find("Standard");
            
            if (standardShader != null)
            {
                WallMaterial = new Material(standardShader);
            }
            else
            {
                // Попробуем найти URP шейдер, который обычно доступен на мобильных устройствах
                Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                if (urpShader != null)
                {
                    WallMaterial = new Material(urpShader);
                }
                else
                {
                    // Попробуем любой доступный шейдер
                    Shader unlitShader = Shader.Find("Unlit/Color");
                    if (unlitShader != null)
                    {
                        WallMaterial = new Material(unlitShader);
                    }
                    else
                    {
                        // Создаем материал со встроенным шейдером ошибки
                        Debug.LogWarning("EnhancedWallRenderer: Не удалось найти подходящий шейдер, используем встроенный шейдер ошибки.");
                        WallMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                    }
                }
            }
            
            // Применяем цвет и прозрачность
            WallMaterial.color = WallColor;
            SetMaterialTransparency(WallMaterial, WallOpacity);
        }
        
        // Настраиваем или создаем оптимизатор стен
        wallOptimizer = GetComponent<WallOptimizer>();
        if (wallOptimizer == null)
        {
            wallOptimizer = gameObject.AddComponent<WallOptimizer>();
        }
        
        // Синхронизируем параметры
        SyncOptimizerParameters();
    }
    
    /// <summary>
    /// Синхронизируем параметры визуализатора с оптимизатором
    /// </summary>
    private void SyncOptimizerParameters()
    {
        if (wallOptimizer != null)
        {
            // Always use the ADE20K wall class ID (9) regardless of what's set in the inspector
            if (WallClassId != 9)
            {
                WallClassId = 9; // Update our own value to match
            }
            
            wallOptimizer.wallClassId = 9; // Force correct wall class ID
            wallOptimizer.confidenceThreshold = WallConfidenceThreshold;
            wallOptimizer.minWallArea = MinWallArea;
            wallOptimizer.showDebugInfo = ShowDebugInfo;
        }
    }
    
    /// <summary>
    /// Обновление каждый кадр
    /// </summary>
    private void Update()
    {
        // Если параметры изменились, синхронизируем их
        SyncOptimizerParameters();
        
        // Обновляем материал с текущими параметрами прозрачности
        if (WallMaterial != null)
        {
            WallMaterial.color = new Color(WallColor.r, WallColor.g, WallColor.b, WallOpacity);
        }
        
        // Запускаем обработку текущего кадра
        if (wallOptimizer != null)
        {
            wallOptimizer.ProcessCurrentFrame();
        }
    }
    
    /// <summary>
    /// Создает стену в указанной позиции с указанным размером
    /// </summary>
    public void CreateWallMesh(Vector3 position, Vector3 size)
    {
        // Проверка минимальной площади
        float area = size.x * size.y;
        if (!ShowAllMeshes && area < MinWallArea)
        {
            return;
        }
        
        // Создаем игровой объект для стены
        GameObject wallObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallObj.name = "Wall_" + walls.Count;
        wallObj.transform.position = position;
        wallObj.transform.localScale = size;
        wallObj.transform.SetParent(transform);
        
        // Применяем материал
        MeshRenderer renderer = wallObj.GetComponent<MeshRenderer>();
        if (renderer != null && WallMaterial != null)
        {
            renderer.material = new Material(WallMaterial);
            
            // Применяем настраиваемые свойства к материалу
            renderer.material.color = new Color(WallColor.r, WallColor.g, WallColor.b, WallOpacity);
        }
        
        // Добавляем в список стен
        walls.Add(wallObj);
        
        if (ShowDebugInfo)
        {
            Debug.Log($"Created wall at {position}, size: {size}, area: {area:F2}m²");
        }
    }
    
    /// <summary>
    /// Удаляет все созданные стены
    /// </summary>
    public void ClearWalls()
    {
        foreach (var wall in walls)
        {
            if (wall != null)
            {
                Destroy(wall);
            }
        }
        
        walls.Clear();
    }
    
    /// <summary>
    /// Устанавливает параметры материала для прозрачности
    /// </summary>
    private void SetMaterialTransparency(Material material, float opacity)
    {
        if (material == null) return;
        
        // Настраиваем материал для прозрачности
        material.SetFloat("_Mode", 3); // Transparent
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        
        // Устанавливаем прозрачность
        Color color = material.color;
        material.color = new Color(color.r, color.g, color.b, opacity);
    }
    
    /// <summary>
    /// Установка цвета стен
    /// </summary>
    public void SetWallColor(Color color)
    {
        WallColor = color;
        
        // Обновляем цвет для всех существующих стен
        foreach (var wall in walls)
        {
            if (wall != null)
            {
                MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.color = new Color(color.r, color.g, color.b, WallOpacity);
                }
            }
        }
    }
} 