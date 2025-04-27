using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// Компонент для кнопки, которая обновляет якори стен
/// </summary>
[RequireComponent(typeof(Button))]
public class WallAnchorButton : MonoBehaviour
{
    private Button _button;
    private InitARWallAnchoring _wallAnchoring;
    
    private void Start()
    {
        // Получаем компонент кнопки
        _button = GetComponent<Button>();
        
        // Ищем компонент InitARWallAnchoring
        _wallAnchoring = FindObjectOfType<InitARWallAnchoring>();
        
        // Подписываемся на событие клика
        _button.onClick.AddListener(OnButtonClick);
        
        if (_wallAnchoring == null)
        {
            Debug.LogWarning("WallAnchorButton: InitARWallAnchoring not found in scene! Button won't work.");
        }
    }
    
    private void OnButtonClick()
    {
        if (_wallAnchoring != null)
        {
            _wallAnchoring.RefreshWallAnchors();
        }
        else
        {
            // Повторяем попытку найти компонент
            _wallAnchoring = FindObjectOfType<InitARWallAnchoring>();
            
            if (_wallAnchoring != null)
            {
                _wallAnchoring.RefreshWallAnchors();
            }
            else
            {
                // Если всё еще не найден, попробуем найти WallAnchorManager напрямую
                WallAnchorManager manager = FindObjectOfType<WallAnchorManager>();
                if (manager != null)
                {
                    manager.RefreshWalls();
                    Debug.Log("Refreshed walls directly through WallAnchorManager");
                }
                else
                {
                    Debug.LogError("No anchoring components found! Cannot refresh wall anchors.");
                }
            }
        }
    }
    
    private void OnDestroy()
    {
        // Отписываемся от события клика
        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClick);
        }
    }
} 