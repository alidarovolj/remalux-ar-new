using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Компонент для добавления UI-элементов управления оптимизацией
/// распознавания стен к существующему интерфейсу.
/// </summary>
public class WallDetectionUI : MonoBehaviour
{
    [Header("Ссылки на компоненты")]
    [Tooltip("Холст, на котором размещены все UI элементы")]
    public Canvas mainCanvas;
    
    [Tooltip("Оптимизатор распознавания стен")]
    public WallDetectionOptimizer optimizer;
    
    [Header("Настройки панели")]
    [Tooltip("Создать UI оптимизатора автоматически")]
    public bool createOptimizerUI = true;
    
    [Tooltip("Показывать панель при старте")]
    public bool showPanelAtStart = true;
    
    [Tooltip("Позиция панели (0-3: верх, право, низ, лево)")]
    [Range(0, 3)]
    public int panelPosition = 0;
    
    // Созданные UI элементы
    private GameObject optimizerPanel;
    private Slider thresholdSlider;
    private Text thresholdText;
    private Dropdown wallClassDropdown;
    private Toggle argMaxToggle;
    private Button analyzeButton;
    
    void Start()
    {
        // Находим компоненты
        if (mainCanvas == null)
            mainCanvas = FindFirstObjectByType<Canvas>();
            
        if (optimizer == null)
            optimizer = FindFirstObjectByType<WallDetectionOptimizer>();
            
        // Если оптимизатора нет, создаем его
        if (optimizer == null)
        {
            GameObject optimizerObj = new GameObject("WallDetectionOptimizer");
            optimizer = optimizerObj.AddComponent<WallDetectionOptimizer>();
            optimizerObj.transform.SetParent(transform);
        }
        
        // Создаем UI элементы
        if (createOptimizerUI && mainCanvas != null)
        {
            CreateOptimizerUI();
        }
    }
    
    void CreateOptimizerUI()
    {
        // Создаем родительскую панель
        optimizerPanel = CreatePanel("OptimizerPanel");
        SetPanelPosition(optimizerPanel.GetComponent<RectTransform>(), panelPosition);
        
        // Заголовок панели
        GameObject titleObj = CreateText("Title", "Wall Detection Optimizer", 18);
        titleObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0, 0.85f);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.offsetMin = new Vector2(5, 0);
        titleRect.offsetMax = new Vector2(-5, 0);
        
        // Слайдер порога
        GameObject sliderObj = CreateSlider("ThresholdSlider", 0.01f, 0.99f, 0.3f);
        sliderObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform sliderRect = sliderObj.GetComponent<RectTransform>();
        sliderRect.anchorMin = new Vector2(0.05f, 0.7f);
        sliderRect.anchorMax = new Vector2(0.95f, 0.8f);
        sliderRect.offsetMin = Vector2.zero;
        sliderRect.offsetMax = Vector2.zero;
        
        thresholdSlider = sliderObj.GetComponent<Slider>();
        
        // Текст порога
        GameObject thresholdTextObj = CreateText("ThresholdText", "Threshold: 0.30", 14);
        thresholdTextObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform thresholdTextRect = thresholdTextObj.GetComponent<RectTransform>();
        thresholdTextRect.anchorMin = new Vector2(0, 0.8f);
        thresholdTextRect.anchorMax = new Vector2(1, 0.85f);
        thresholdTextRect.offsetMin = new Vector2(5, 0);
        thresholdTextRect.offsetMax = new Vector2(-5, 0);
        
        thresholdText = thresholdTextObj.GetComponent<Text>();
        
        // Выпадающий список классов
        GameObject dropdownObj = CreateDropdown("WallClassDropdown", new string[] { 
            "Class 0", "Class 1", "Class 2", "Class 3", 
            "Class 4", "Class 5", "Class 6", "Class 7" 
        });
        dropdownObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform dropdownRect = dropdownObj.GetComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(0.05f, 0.5f);
        dropdownRect.anchorMax = new Vector2(0.95f, 0.65f);
        dropdownRect.offsetMin = Vector2.zero;
        dropdownRect.offsetMax = Vector2.zero;
        
        wallClassDropdown = dropdownObj.GetComponent<Dropdown>();
        wallClassDropdown.value = 1; // По умолчанию Class 1
        
        // Текст для класса стен
        GameObject classLabelObj = CreateText("ClassLabel", "Wall Class ID:", 14);
        classLabelObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform classLabelRect = classLabelObj.GetComponent<RectTransform>();
        classLabelRect.anchorMin = new Vector2(0, 0.65f);
        classLabelRect.anchorMax = new Vector2(1, 0.7f);
        classLabelRect.offsetMin = new Vector2(5, 0);
        classLabelRect.offsetMax = new Vector2(-5, 0);
        
        // Тогл ArgMax
        GameObject toggleObj = CreateToggle("ArgMaxToggle", "Use ArgMax Mode", true);
        toggleObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0.05f, 0.4f);
        toggleRect.anchorMax = new Vector2(0.95f, 0.5f);
        toggleRect.offsetMin = Vector2.zero;
        toggleRect.offsetMax = Vector2.zero;
        
        argMaxToggle = toggleObj.GetComponent<Toggle>();
        
        // Кнопка анализа классов
        GameObject analyzeButtonObj = CreateButton("AnalyzeButton", "Analyze Classes");
        analyzeButtonObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform analyzeButtonRect = analyzeButtonObj.GetComponent<RectTransform>();
        analyzeButtonRect.anchorMin = new Vector2(0.05f, 0.25f);
        analyzeButtonRect.anchorMax = new Vector2(0.95f, 0.35f);
        analyzeButtonRect.offsetMin = Vector2.zero;
        analyzeButtonRect.offsetMax = Vector2.zero;
        
        analyzeButton = analyzeButtonObj.GetComponent<Button>();
        
        // Кнопка сброса
        GameObject resetButtonObj = CreateButton("ResetButton", "Reset to Default");
        resetButtonObj.transform.SetParent(optimizerPanel.transform, false);
        RectTransform resetButtonRect = resetButtonObj.GetComponent<RectTransform>();
        resetButtonRect.anchorMin = new Vector2(0.05f, 0.1f);
        resetButtonRect.anchorMax = new Vector2(0.95f, 0.2f);
        resetButtonRect.offsetMin = Vector2.zero;
        resetButtonRect.offsetMax = Vector2.zero;
        
        Button resetButton = resetButtonObj.GetComponent<Button>();
        
        // Кнопка переключения панели
        GameObject togglePanelObj = CreateButton("TogglePanelButton", ">");
        togglePanelObj.transform.SetParent(mainCanvas.transform, false);
        RectTransform togglePanelRect = togglePanelObj.GetComponent<RectTransform>();
        togglePanelRect.anchorMin = new Vector2(0.95f, 0.9f);
        togglePanelRect.anchorMax = new Vector2(1, 0.95f);
        togglePanelRect.offsetMin = new Vector2(-50, 0);
        togglePanelRect.offsetMax = new Vector2(-5, 0);
        
        Button togglePanelButton = togglePanelObj.GetComponent<Button>();
        
        // Настраиваем события
        thresholdSlider.onValueChanged.AddListener(OnThresholdChanged);
        wallClassDropdown.onValueChanged.AddListener(OnWallClassChanged);
        argMaxToggle.onValueChanged.AddListener(OnArgMaxToggled);
        analyzeButton.onClick.AddListener(OnAnalyzeClicked);
        resetButton.onClick.AddListener(OnResetClicked);
        togglePanelButton.onClick.AddListener(OnTogglePanelClicked);
        
        // Связываем созданные элементы с оптимизатором
        optimizer.thresholdSlider = thresholdSlider;
        optimizer.thresholdText = thresholdText;
        optimizer.wallClassDropdown = wallClassDropdown;
        optimizer.argMaxToggle = argMaxToggle;
        optimizer.optimizerPanel = optimizerPanel;
        
        // Устанавливаем видимость панели
        optimizerPanel.SetActive(showPanelAtStart);
        optimizer.showOptimizationPanel = showPanelAtStart;
    }
    
    #region UI Event Handlers
    
    void OnThresholdChanged(float value)
    {
        // Обновляем текст
        if (thresholdText != null)
            thresholdText.text = $"Threshold: {value:F2}";
            
        // Вызываем метод оптимизатора
        if (optimizer != null)
            optimizer.OnThresholdChanged(value);
    }
    
    void OnWallClassChanged(int value)
    {
        if (optimizer != null)
            optimizer.OnWallClassChanged(value);
    }
    
    void OnArgMaxToggled(bool value)
    {
        if (optimizer != null)
            optimizer.OnArgMaxToggled(value);
    }
    
    void OnAnalyzeClicked()
    {
        if (optimizer != null)
            optimizer.ShowClassDistribution();
    }
    
    void OnResetClicked()
    {
        if (optimizer != null)
            optimizer.RestoreDefaultSettings();
    }
    
    void OnTogglePanelClicked()
    {
        if (optimizer != null)
            optimizer.ToggleOptimizationPanel();
    }
    
    #endregion
    
    #region UI Helper Methods
    
    GameObject CreatePanel(string name)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(mainCanvas.transform, false);
        
        RectTransform rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0.65f);
        rect.anchorMax = new Vector2(0.25f, 0.95f);
        rect.offsetMin = new Vector2(10, 10);
        rect.offsetMax = new Vector2(-10, -10);
        
        Image image = panelObj.AddComponent<Image>();
        image.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        
        return panelObj;
    }
    
    void SetPanelPosition(RectTransform panelRect, int position)
    {
        switch (position)
        {
            case 0: // Верх
                panelRect.anchorMin = new Vector2(0.05f, 0.65f);
                panelRect.anchorMax = new Vector2(0.3f, 0.95f);
                break;
                
            case 1: // Право
                panelRect.anchorMin = new Vector2(0.7f, 0.3f);
                panelRect.anchorMax = new Vector2(0.95f, 0.7f);
                break;
                
            case 2: // Низ
                panelRect.anchorMin = new Vector2(0.05f, 0.05f);
                panelRect.anchorMax = new Vector2(0.3f, 0.35f);
                break;
                
            case 3: // Лево
                panelRect.anchorMin = new Vector2(0.05f, 0.3f);
                panelRect.anchorMax = new Vector2(0.3f, 0.7f);
                break;
        }
        
        panelRect.offsetMin = new Vector2(10, 10);
        panelRect.offsetMax = new Vector2(-10, -10);
    }
    
    GameObject CreateText(string name, string content, int fontSize)
    {
        GameObject textObj = new GameObject(name);
        
        Text text = textObj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        
        return textObj;
    }
    
    GameObject CreateSlider(string name, float min, float max, float value)
    {
        GameObject sliderObj = new GameObject(name);
        
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        
        // Фон слайдера
        GameObject background = new GameObject("Background");
        background.transform.SetParent(sliderObj.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        // Заполнение слайдера
        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(sliderObj.transform, false);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.4f, 0.7f, 1f, 1f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0.5f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        
        // Ручка слайдера
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(sliderObj.transform, false);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = new Vector2(0.5f, 0.5f);
        handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(20, 30);
        
        // Настраиваем слайдер
        slider.fillRect = fillRect;
        slider.handleRect = handleRect;
        slider.transition = Selectable.Transition.ColorTint;
        
        return sliderObj;
    }
    
    GameObject CreateDropdown(string name, string[] options)
    {
        GameObject dropdownObj = new GameObject(name);
        
        Dropdown dropdown = dropdownObj.AddComponent<Dropdown>();
        
        // Фон выпадающего списка
        GameObject background = new GameObject("Background");
        background.transform.SetParent(dropdownObj.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        // Текст значения
        GameObject valueText = CreateText("Value", "Option", 14);
        valueText.transform.SetParent(background.transform, false);
        RectTransform valueRect = valueText.GetComponent<RectTransform>();
        valueRect.anchorMin = new Vector2(0, 0);
        valueRect.anchorMax = new Vector2(1, 1);
        valueRect.offsetMin = new Vector2(10, 0);
        valueRect.offsetMax = new Vector2(-10, 0);
        
        // Добавляем опции
        dropdown.options.Clear();
        foreach (string option in options)
        {
            dropdown.options.Add(new Dropdown.OptionData(option));
        }
        
        dropdown.captionText = valueText.GetComponent<Text>();
        dropdown.RefreshShownValue();
        
        return dropdownObj;
    }
    
    GameObject CreateToggle(string name, string label, bool isOn)
    {
        GameObject toggleObj = new GameObject(name);
        
        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = isOn;
        
        // Фон
        GameObject background = new GameObject("Background");
        background.transform.SetParent(toggleObj.transform, false);
        Image bgImage = background.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.5f);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        // Чекбокс
        GameObject checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(toggleObj.transform, false);
        RectTransform checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0, 0.5f);
        checkRect.anchorMax = new Vector2(0, 0.5f);
        checkRect.sizeDelta = new Vector2(20, 20);
        checkRect.anchoredPosition = new Vector2(20, 0);
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.color = new Color(0.4f, 0.7f, 1f, 1f);
        
        // Текст
        GameObject labelObj = CreateText("Label", label, 14);
        labelObj.transform.SetParent(toggleObj.transform, false);
        Text labelText = labelObj.GetComponent<Text>();
        labelText.alignment = TextAnchor.MiddleLeft;
        RectTransform labelRect = labelObj.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0, 0);
        labelRect.anchorMax = new Vector2(1, 1);
        labelRect.offsetMin = new Vector2(45, 0);
        labelRect.offsetMax = new Vector2(-5, 0);
        
        // Настраиваем toggle
        toggle.graphic = checkImage;
        toggle.targetGraphic = checkImage;
        
        return toggleObj;
    }
    
    GameObject CreateButton(string name, string label)
    {
        GameObject buttonObj = new GameObject(name);
        
        Button button = buttonObj.AddComponent<Button>();
        
        // Фон кнопки
        Image image = buttonObj.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        // Текст кнопки
        GameObject textObj = CreateText("Text", label, 14);
        textObj.transform.SetParent(buttonObj.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        // Настраиваем цвета
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        colors.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        button.colors = colors;
        
        return buttonObj;
    }
    
    #endregion
} 