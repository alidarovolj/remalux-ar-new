using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Настройщик параметров обнаружения стен с пользовательским интерфейсом
/// </summary>
public class WallDetectionTuner : MonoBehaviour
{
    [Header("Ссылки на компоненты")]
    [Tooltip("Ссылка на компонент WallOptimizer")]
    public WallOptimizer wallOptimizer;
    
    [Tooltip("Ссылка на компонент EnhancedWallRenderer")]
    public EnhancedWallRenderer wallRenderer;
    
    [Header("UI элементы")]
    [Tooltip("Слайдер для настройки порога уверенности")]
    public Slider confidenceSlider;
    
    [Tooltip("Слайдер для настройки минимальной площади контура")]
    public Slider contourAreaSlider;
    
    [Tooltip("Слайдер для настройки минимальной площади стены")]
    public Slider wallAreaSlider;
    
    [Tooltip("Текст для отображения текущих настроек")]
    public Text statsText;
    
    [Tooltip("Текст для отображения счетчика стен")]
    public Text wallCountText;
    
    [Tooltip("Toggle для включения/отключения морфологии")]
    public Toggle morphologyToggle;
    
    [Header("Настройки UI")]
    [Tooltip("Показывать панель настройки")]
    public bool showTunerPanel = true;
    
    private bool isPanelVisible = true;
    private Rect windowRect = new Rect(20, 20, 300, 400);
    
    private void Start()
    {
        // Автоматический поиск компонентов, если они не назначены
        if (wallOptimizer == null)
        {
            wallOptimizer = FindObjectOfType<WallOptimizer>();
        }
        
        if (wallRenderer == null)
        {
            wallRenderer = FindObjectOfType<EnhancedWallRenderer>();
        }
        
        // Инициализация слайдеров, если они назначены
        if (confidenceSlider != null && wallOptimizer != null)
        {
            confidenceSlider.value = wallOptimizer.confidenceThreshold;
            confidenceSlider.onValueChanged.AddListener(SetConfidenceThreshold);
        }
        
        if (contourAreaSlider != null && wallOptimizer != null)
        {
            contourAreaSlider.value = wallOptimizer.minContourArea;
            contourAreaSlider.onValueChanged.AddListener(SetMinContourArea);
        }
        
        if (wallAreaSlider != null && wallOptimizer != null)
        {
            wallAreaSlider.value = wallOptimizer.minWallArea;
            wallAreaSlider.onValueChanged.AddListener(SetMinWallArea);
        }
        
        if (morphologyToggle != null && wallOptimizer != null)
        {
            morphologyToggle.isOn = wallOptimizer.useMorphology;
            morphologyToggle.onValueChanged.AddListener(SetUseMorphology);
        }
        
        isPanelVisible = showTunerPanel;
    }
    
    private void Update()
    {
        // Обновление отображаемой статистики
        UpdateStats();
        
        // Переключение видимости панели по клавише Tab
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
        {
            isPanelVisible = !isPanelVisible;
        }
    }
    
    private void UpdateStats()
    {
        if (statsText != null && wallOptimizer != null)
        {
            statsText.text = $"Настройки:\n" +
                $"Порог уверенности: {wallOptimizer.confidenceThreshold:F2}\n" +
                $"Мин. площадь контура: {wallOptimizer.minContourArea}\n" +
                $"Мин. площадь стены: {wallOptimizer.minWallArea:F2}м²\n" +
                $"Морфология: {(wallOptimizer.useMorphology ? "Вкл" : "Выкл")}\n" +
                $"Класс стены: {wallOptimizer.wallClassId}";
        }
        
        // Обновление счетчика стен, если доступны компоненты
        if (wallCountText != null)
        {
            int wallCount = 0;
            if (transform.childCount > 0)
            {
                // Считаем количество дочерних объектов, имена которых начинаются с "Wall_"
                foreach (Transform child in transform)
                {
                    if (child.name.StartsWith("Wall_"))
                    {
                        wallCount++;
                    }
                }
            }
            
            wallCountText.text = $"Стен: {wallCount}";
        }
    }
    
    /// <summary>
    /// Обработчик OnGUI для отображения настроек в режиме редактора
    /// </summary>
    private void OnGUI()
    {
        if (!isPanelVisible || wallOptimizer == null) return;
        
        // Показываем панель с настройками, если нет UI слайдеров
        if (confidenceSlider == null || contourAreaSlider == null || wallAreaSlider == null)
        {
            windowRect = GUI.Window(0, windowRect, DrawTunerWindow, "Настройка обнаружения стен");
        }
    }
    
    /// <summary>
    /// Отрисовка панели настройки параметров
    /// </summary>
    private void DrawTunerWindow(int windowID)
    {
        float y = 30;
        float labelWidth = 150;
        float controlWidth = 120;
        float sliderHeight = 20;
        float spacing = 10;
        
        GUI.Label(new Rect(10, y, labelWidth, sliderHeight), "Порог уверенности:");
        wallOptimizer.confidenceThreshold = GUI.HorizontalSlider(
            new Rect(labelWidth + 10, y, controlWidth, sliderHeight),
            wallOptimizer.confidenceThreshold, 0.1f, 0.9f);
        GUI.Label(new Rect(labelWidth + controlWidth + 15, y, 50, sliderHeight), 
            wallOptimizer.confidenceThreshold.ToString("F2"));
        y += sliderHeight + spacing;
        
        GUI.Label(new Rect(10, y, labelWidth, sliderHeight), "Мин. площадь контура:");
        wallOptimizer.minContourArea = GUI.HorizontalSlider(
            new Rect(labelWidth + 10, y, controlWidth, sliderHeight),
            wallOptimizer.minContourArea, 500f, 10000f);
        GUI.Label(new Rect(labelWidth + controlWidth + 15, y, 50, sliderHeight), 
            wallOptimizer.minContourArea.ToString("F0"));
        y += sliderHeight + spacing;
        
        GUI.Label(new Rect(10, y, labelWidth, sliderHeight), "Мин. площадь стены (м²):");
        wallOptimizer.minWallArea = GUI.HorizontalSlider(
            new Rect(labelWidth + 10, y, controlWidth, sliderHeight),
            wallOptimizer.minWallArea, 0.5f, 5.0f);
        GUI.Label(new Rect(labelWidth + controlWidth + 15, y, 50, sliderHeight), 
            wallOptimizer.minWallArea.ToString("F1"));
        y += sliderHeight + spacing;
        
        bool useMorphology = GUI.Toggle(
            new Rect(10, y, windowRect.width - 20, sliderHeight),
            wallOptimizer.useMorphology, "Применять морфологию");
        wallOptimizer.useMorphology = useMorphology;
        y += sliderHeight + spacing;
        
        if (wallOptimizer.useMorphology)
        {
            GUI.Label(new Rect(10, y, labelWidth, sliderHeight), "Размер ядра морфологии:");
            wallOptimizer.morphKernelSize = (int)GUI.HorizontalSlider(
                new Rect(labelWidth + 10, y, controlWidth, sliderHeight),
                wallOptimizer.morphKernelSize, 1, 10);
            GUI.Label(new Rect(labelWidth + controlWidth + 15, y, 50, sliderHeight), 
                wallOptimizer.morphKernelSize.ToString());
            y += sliderHeight + spacing;
        }
        
        // Выбор класса стены
        GUI.Label(new Rect(10, y, labelWidth, sliderHeight), "ID класса стены:");
        int classId = wallOptimizer.wallClassId;
        classId = (int)GUI.HorizontalSlider(
            new Rect(labelWidth + 10, y, controlWidth, sliderHeight),
            classId, 0, 20);
        wallOptimizer.wallClassId = (byte)classId;
        GUI.Label(new Rect(labelWidth + controlWidth + 15, y, 50, sliderHeight), 
            classId.ToString());
        y += sliderHeight + spacing;
        
        // Кнопки сброса и перезагрузки
        if (GUI.Button(new Rect(10, y, (windowRect.width - 30) / 2, 30), "Сбросить"))
        {
            ResetToDefaults();
        }
        
        if (GUI.Button(new Rect((windowRect.width - 30) / 2 + 20, y, (windowRect.width - 30) / 2, 30), "Перезагрузить"))
        {
            if (wallRenderer != null)
            {
                wallRenderer.ClearWalls();
            }
        }
        y += 30 + spacing;
        
        // Статистика
        GUI.Label(new Rect(10, y, windowRect.width - 20, 50), 
            $"Стен отображается: {transform.childCount}\n" +
            $"[Tab] - скрыть/показать панель");
        
        // Возможность перетаскивания окна
        GUI.DragWindow();
    }
    
    // Обработчики событий слайдеров
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
    
    public void SetMinContourArea(float value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.minContourArea = value;
        }
    }
    
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
    
    public void SetUseMorphology(bool value)
    {
        if (wallOptimizer != null)
        {
            wallOptimizer.useMorphology = value;
        }
    }
    
    // Сброс параметров к значениям по умолчанию
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
            wallOptimizer.wallClassId = 15;
            
            // Обновляем слайдеры, если они доступны
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
            
            // Синхронизируем с визуализатором
            if (wallRenderer != null)
            {
                wallRenderer.WallConfidenceThreshold = wallOptimizer.confidenceThreshold;
                wallRenderer.MinWallArea = wallOptimizer.minWallArea;
                wallRenderer.WallClassId = (byte)wallOptimizer.wallClassId;
            }
        }
    }
} 