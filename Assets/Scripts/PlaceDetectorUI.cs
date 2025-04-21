using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for the Place Detector functionality
/// </summary>
public class PlaceDetectorUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlaceDetector placeDetector;
    [SerializeField] private ARSessionHelper sessionHelper;
    
    [Header("UI Elements")]
    [SerializeField] private Button scanButton;
    [SerializeField] private Button placeButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button changePrefabButton;
    [SerializeField] private TMP_InputField labelInput;
    [SerializeField] private TextMeshProUGUI statusText;
    
    [Header("UI Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject scanningPanel;
    [SerializeField] private GameObject placementPanel;
    
    [Header("Settings")]
    [SerializeField] private bool autoStartEnabled = true; // По умолчанию включен автоматический запуск
    [SerializeField] private bool hideScanButtonOnAutoStart = false; // Полностью скрывать кнопку SCAN при автозапуске
    
    private bool isScanning = false;
    private bool isAutoStarted = false;
    
    private void Awake()
    {
        // Auto-find components if not set
        if (placeDetector == null)
            placeDetector = FindFirstObjectByType<PlaceDetector>();
            
        if (sessionHelper == null && placeDetector != null)
            sessionHelper = placeDetector.GetComponent<ARSessionHelper>();
            
        if (sessionHelper == null)
            sessionHelper = FindFirstObjectByType<ARSessionHelper>();
    }
    
    private void Start()
    {
        // Set up button listeners
        if (scanButton != null)
        {
            scanButton.onClick.AddListener(ToggleScanning);
            
            // Если включен автозапуск, сделаем кнопку SCAN менее заметной или скроем её
            if (autoStartEnabled)
            {
                if (hideScanButtonOnAutoStart)
                {
                    // Полностью скрываем кнопку SCAN
                    scanButton.gameObject.SetActive(false);
                }
                else
                {
                    // Уменьшаем размер или прозрачность текста
                    TextMeshProUGUI buttonText = scanButton.GetComponentInChildren<TextMeshProUGUI>();
                    if (buttonText != null)
                    {
                        buttonText.text = "RESTART SCAN";
                        Color color = buttonText.color;
                        color.a = 0.7f; // Делаем полупрозрачным
                        buttonText.color = color;
                    }
                }
            }
        }
            
        if (placeButton != null)
            placeButton.onClick.AddListener(PlaceObject);
            
        if (clearButton != null)
            clearButton.onClick.AddListener(ClearObjects);
            
        if (changePrefabButton != null)
            changePrefabButton.onClick.AddListener(CyclePrefab);
            
        // Initial UI state - при автозапуске сразу показываем панель размещения
        if (autoStartEnabled)
        {
            // Покажем панель размещения и статус инициализации
            ShowPlacementPanel();
            UpdateStatus("Инициализация AR...");
        }
        else
        {
            // Стандартное поведение при отключенном автозапуске
            ShowMainPanel();
            UpdateStatus("Initializing AR...");
        }
        
        // Disable buttons until session ready
        SetButtonsInteractable(false);
    }
    
    private void Update()
    {
        // Check AR session status
        if (sessionHelper != null)
        {
            if (sessionHelper.IsSessionReady() && !placeButton.interactable)
            {
                SetButtonsInteractable(true);
                
                if (autoStartEnabled && !isAutoStarted)
                {
                    // При автозапуске меняем статус без упоминания кнопки SCAN
                    UpdateStatus("AR запущен. Можно размещать объекты");
                    isAutoStarted = true;
                }
                else if (!autoStartEnabled)
                {
                    // Стандартное сообщение для ручного запуска
                    UpdateStatus("AR Ready - Tap SCAN to start");
                }
            }
        }
    }
    
    /// <summary>
    /// Toggle scanning mode
    /// </summary>
    public void ToggleScanning()
    {
        isScanning = !isScanning;
        
        if (isScanning)
        {
            // Start scanning
            placeDetector.StartScanning();
            ShowScanningPanel();
            if (scanButton != null)
            {
                TextMeshProUGUI buttonText = scanButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                    buttonText.text = "STOP SCAN";
            }
            UpdateStatus("Scanning for surfaces...");
        }
        else
        {
            // Stop scanning
            placeDetector.StopScanning();
            ShowPlacementPanel();
            if (scanButton != null)
            {
                TextMeshProUGUI buttonText = scanButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                    buttonText.text = autoStartEnabled ? "RESTART SCAN" : "SCAN";
            }
            placeDetector.TogglePlacementMode(true);
            UpdateStatus("Place AR objects on detected surfaces");
        }
    }
    
    /// <summary>
    /// Place an object at the current position
    /// </summary>
    public void PlaceObject()
    {
        string label = string.Empty;
        if (labelInput != null)
            label = labelInput.text;
            
        placeDetector.PlaceObject(label);
        
        // Clear input field
        if (labelInput != null)
            labelInput.text = string.Empty;
    }
    
    /// <summary>
    /// Clear all placed objects
    /// </summary>
    public void ClearObjects()
    {
        placeDetector.ClearPlacedObjects();
        UpdateStatus("All objects cleared");
    }
    
    /// <summary>
    /// Cycle through available prefabs
    /// </summary>
    public void CyclePrefab()
    {
        placeDetector.CyclePrefab();
    }
    
    /// <summary>
    /// Set buttons to interactive or non-interactive state
    /// </summary>
    private void SetButtonsInteractable(bool interactable)
    {
        if (scanButton != null) scanButton.interactable = interactable;
        if (placeButton != null) placeButton.interactable = interactable;
        if (clearButton != null) clearButton.interactable = interactable;
        if (changePrefabButton != null) changePrefabButton.interactable = interactable;
    }
    
    /// <summary>
    /// Update the status text
    /// </summary>
    public void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        Debug.Log("PlaceDetector Status: " + message);
    }
    
    /// <summary>
    /// Show the main panel
    /// </summary>
    private void ShowMainPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (scanningPanel != null) scanningPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(false);
    }
    
    /// <summary>
    /// Show the scanning panel
    /// </summary>
    private void ShowScanningPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (scanningPanel != null) scanningPanel.SetActive(true);
        if (placementPanel != null) placementPanel.SetActive(false);
    }
    
    /// <summary>
    /// Show the placement panel
    /// </summary>
    private void ShowPlacementPanel()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (scanningPanel != null) scanningPanel.SetActive(false);
        if (placementPanel != null) placementPanel.SetActive(true);
    }
    
    /// <summary>
    /// Программно переключает интерфейс в режим размещения объектов
    /// </summary>
    public void SwitchToPlacementMode()
    {
        if (isScanning)
        {
            // Если сканирование активно, отключаем его
            isScanning = false;
            
            if (placeDetector != null)
            {
                placeDetector.StopScanning();
                placeDetector.TogglePlacementMode(true);
            }
            
            if (scanButton != null)
            {
                TextMeshProUGUI buttonText = scanButton.GetComponentInChildren<TextMeshProUGUI>();
                if (buttonText != null)
                    buttonText.text = autoStartEnabled ? "RESTART SCAN" : "SCAN";
            }
        }
        else if (!isScanning && placeDetector != null)
        {
            // Если сканирование не активно, просто включаем режим размещения
            placeDetector.TogglePlacementMode(true);
        }
        
        // Показываем панель размещения
        ShowPlacementPanel();
        UpdateStatus("Можно размещать AR объекты");
    }
} 