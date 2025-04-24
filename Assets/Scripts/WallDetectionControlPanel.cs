using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Скрипт для управления панелью настройки параметров обнаружения стен
/// </summary>
public class WallDetectionControlPanel : MonoBehaviour
{
    [Header("Компоненты")]
    [Tooltip("Ссылка на WallOptimizer")]
    public WallOptimizer wallOptimizer;
    
    [Tooltip("Ссылка на EnhancedWallRenderer")]
    public EnhancedWallRenderer wallRenderer;
    
    [Header("UI элементы")]
    [Tooltip("Панель настроек")]
    public GameObject settingsPanel;
    
    [Tooltip("Слайдер для порога уверенности")]
    public Slider confidenceSlider;
    
    [Tooltip("Слайдер для минимальной площади контура")]
    public Slider contourAreaSlider;
    
    [Tooltip("Слайдер для минимальной площади стены")]
    public Slider wallAreaSlider;
    
    [Tooltip("Toggle для морфологии")]
    public Toggle morphologyToggle;
    
    [Tooltip("Dropdown для выбора класса стены")]
    public Dropdown wallClassDropdown;
    
    [Tooltip("Текст для отображения статистики")]
    public Text statsText;
    
    [Tooltip("Текст для отображения количества стен")]
    public Text wallCountText;
    
    private void Start()
    {
        // Автоматически находим компоненты, если они не заданы
        if (wallOptimizer == null)
        {
            wallOptimizer = FindObjectOfType<WallOptimizer>();
        }
        
        if (wallRenderer == null)
        {
            wallRenderer = FindObjectOfType<EnhancedWallRenderer>();
        }
        
        // Настраиваем UI элементы
        SetupUI();
    }
    
    /// <summary>
    /// Настройка элементов пользовательского интерфейса
    /// </summary>
    private void SetupUI()
    {
        if (wallOptimizer == null) return;
        
        // Настраиваем слайдеры
        if (confidenceSlider != null)
        {
            confidenceSlider.minValue = 0.1f;
            confidenceSlider.maxValue = 0.9f;
            confidenceSlider.value = wallOptimizer.confidenceThreshold;
            confidenceSlider.onValueChanged.AddListener(SetConfidenceThreshold);
        }
        
        if (contourAreaSlider != null)
        {
            contourAreaSlider.minValue = 500f;
            contourAreaSlider.maxValue = 10000f;
            contourAreaSlider.value = wallOptimizer.minContourArea;
            contourAreaSlider.onValueChanged.AddListener(SetMinContourArea);
        }
        
        if (wallAreaSlider != null)
        {
            wallAreaSlider.minValue = 0.5f;
            wallAreaSlider.maxValue = 5.0f;
            wallAreaSlider.value = wallOptimizer.minWallArea;
            wallAreaSlider.onValueChanged.AddListener(SetMinWallArea);
        }
        
        if (morphologyToggle != null)
        {
            morphologyToggle.isOn = wallOptimizer.useMorphology;
            morphologyToggle.onValueChanged.AddListener(SetUseMorphology);
        }
        
        // Настраиваем выпадающий список для выбора класса стены
        if (wallClassDropdown != null)
        {
            wallClassDropdown.ClearOptions();
            
            // Добавляем возможные классы стен
            for (int i = 0; i <= 20; i++)
            {
                wallClassDropdown.options.Add(new Dropdown.OptionData($"Класс {i}"));
            }
            
            wallClassDropdown.value = wallOptimizer.wallClassId;
            wallClassDropdown.onValueChanged.AddListener(SetWallClass);
            wallClassDropdown.RefreshShownValue();
        }
    }
    
    private void Update()
    {
        // Обновляем статистику
        UpdateStats();
        
        // Переключение видимости панели настроек по клавише Tab
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame && settingsPanel != null)
        {
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        }
    }
    
    /// <summary>
    /// Обновление отображаемой статистики
    /// </summary>
    private void UpdateStats()
    {
        if (statsText != null && wallOptimizer != null)
        {
            statsText.text = string.Format(
                "Порог: {0:F2}\n" +
                "Контур: {1:F0} пикс.\n" +
                "Площадь: {2:F1} м²\n" +
                "Класс: {3}\n" +
                "Морфология: {4}",
                wallOptimizer.confidenceThreshold,
                wallOptimizer.minContourArea,
                wallOptimizer.minWallArea,
                wallOptimizer.wallClassId,
                wallOptimizer.useMorphology ? "Вкл" : "Выкл"
            );
        }
        
        if (wallCountText != null && wallRenderer != null)
        {
            // Считаем количество стен (дочерних объектов)
            int wallCount = 0;
            if (wallRenderer.transform.childCount > 0)
            {
                foreach (Transform child in wallRenderer.transform)
                {
                    if (child.gameObject.activeSelf && child.name.StartsWith("Wall_"))
                    {
                        wallCount++;
                    }
                }
            }
            
            wallCountText.text = "Стен: " + wallCount;
        }
    }
    
    // Обработчики событий UI
    
    /// <summary>
    /// Установка порога уверенности
    /// </summary>
    public void SetConfidenceThreshold(float value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.confidenceThreshold = value;
            
            if (wallRenderer != null)
            {
                wallRenderer.WallConfidenceThreshold = value;
            }
        }
    }
    
    /// <summary>
    /// Установка минимальной площади контура
    /// </summary>
    public void SetMinContourArea(float value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.minContourArea = value;
        }
    }
    
    /// <summary>
    /// Установка минимальной площади стены
    /// </summary>
    public void SetMinWallArea(float value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.minWallArea = value;
            
            if (wallRenderer != null)
            {
                wallRenderer.MinWallArea = value;
            }
        }
    }
    
    /// <summary>
    /// Включение/отключение морфологии
    /// </summary>
    public void SetUseMorphology(bool value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.useMorphology = value;
        }
    }
    
    /// <summary>
    /// Установка класса стены
    /// </summary>
    public void SetWallClass(int classId)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.wallClassId = (byte)classId;
            
            if (wallRenderer != null)
            {
                wallRenderer.WallClassId = (byte)classId;
            }
        }
    }
    
    /// <summary>
    /// Сброс параметров к значениям по умолчанию
    /// </summary>
    public void ResetToDefaults()
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.confidenceThreshold = 0.4f;
            wallOptimizer.minContourArea = 3000f;
            wallOptimizer.minWallArea = 1.5f;
            wallOptimizer.useMorphology = true;
            wallOptimizer.morphKernelSize = 3;
            wallOptimizer.minAspectRatio = 0.3f;
            wallOptimizer.maxAspectRatio = 4.0f;
            wallOptimizer.wallClassId = 9;
            
            // Обновляем UI элементы
            if (confidenceSlider != null)
            {
                confidenceSlider.value = wallOptimizer.confidenceThreshold;
            }
            
            if (contourAreaSlider != null)
            {
                contourAreaSlider.value = wallOptimizer.minContourArea;
            }
            
            if (wallAreaSlider != null)
            {
                wallAreaSlider.value = wallOptimizer.minWallArea;
            }
            
            if (morphologyToggle != null)
            {
                morphologyToggle.isOn = wallOptimizer.useMorphology;
            }
            
            if (wallClassDropdown != null)
            {
                wallClassDropdown.value = wallOptimizer.wallClassId;
            }
            
            // Синхронизируем с визуализатором
            if (wallRenderer != null)
            {
                wallRenderer.WallConfidenceThreshold = wallOptimizer.confidenceThreshold;
                wallRenderer.MinWallArea = wallOptimizer.minWallArea;
                wallRenderer.WallClassId = (byte)wallOptimizer.wallClassId;
            }
        }
    }
    
    /// <summary>
    /// Очистка всех стен
    /// </summary>
    public void ClearWalls()
    {
        if (wallRenderer != null)
        {
            wallRenderer.ClearWalls();
        }
    }
    
    /// <summary>
    /// Переключение видимости панели настроек
    /// </summary>
    public void ToggleSettingsPanel()
    {
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(!settingsPanel.activeSelf);
        }
    }
} 