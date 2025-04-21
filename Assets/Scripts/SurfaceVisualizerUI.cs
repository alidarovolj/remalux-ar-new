using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(SurfaceVisualizer))]
public class SurfaceVisualizerUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private Slider opacitySlider;
    [SerializeField] private Text statusText;

    private SurfaceVisualizer surfaceVisualizer;
    private bool planesVisible = true;

    private void Awake()
    {
        surfaceVisualizer = GetComponent<SurfaceVisualizer>();
    }

    private void Start()
    {
        // Настраиваем кнопку переключения
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(OnToggleButtonClicked);
            UpdateToggleButtonText();
        }

        // Настраиваем слайдер прозрачности
        if (opacitySlider != null)
        {
            opacitySlider.value = 0.5f; // Начальное значение
            opacitySlider.onValueChanged.AddListener(OnOpacityChanged);
        }

        UpdateStatusText();
    }

    private void OnToggleButtonClicked()
    {
        planesVisible = !planesVisible;
        surfaceVisualizer.SetPlanesActive(planesVisible);
        UpdateToggleButtonText();
        UpdateStatusText();
    }

    private void OnOpacityChanged(float value)
    {
        surfaceVisualizer.SetPlaneOpacity(value);
        UpdateStatusText();
    }

    private void UpdateToggleButtonText()
    {
        if (toggleButton != null && toggleButton.GetComponentInChildren<Text>() != null)
        {
            toggleButton.GetComponentInChildren<Text>().text = 
                planesVisible ? "Скрыть поверхности" : "Показать поверхности";
        }
    }

    private void UpdateStatusText()
    {
        if (statusText != null)
        {
            statusText.text = planesVisible ? 
                $"Поверхности видимы (прозрачность: {opacitySlider.value:F2})" : 
                "Поверхности скрыты";
        }
    }

    private void OnDestroy()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(OnToggleButtonClicked);
        }

        if (opacitySlider != null)
        {
            opacitySlider.onValueChanged.RemoveListener(OnOpacityChanged);
        }
    }
} 