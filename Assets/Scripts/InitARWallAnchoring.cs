using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils; // Добавляем для XROrigin

/// <summary>
/// Автоматически инициализирует систему якорения стен
/// Добавьте этот скрипт на ARScene объект
/// </summary>
public class InitARWallAnchoring : MonoBehaviour
{
    [SerializeField] private Material _anchoredWallMaterial;
    [SerializeField] private bool _startAutomatically = true;
    
    private ARAnchorManager _anchorManager;
    private ARWallAnchor _wallAnchor;
    private WallAnchorManager _wallAnchorManager;
    
    private void Start()
    {
        if (_startAutomatically)
        {
            InitializeAnchorSystem();
        }
    }
    
    /// <summary>
    /// Инициализирует систему якорения стен
    /// </summary>
    public void InitializeAnchorSystem()
    {
        // Находим XR Origin
        GameObject xrOrigin = null;
        
        XROrigin origin = FindObjectOfType<XROrigin>();
        if (origin != null)
        {
            xrOrigin = origin.gameObject;
            Debug.Log($"Found XR Origin: {xrOrigin.name}");
        }
        else
        {
            ARSessionOrigin sessionOrigin = FindObjectOfType<ARSessionOrigin>();
            if (sessionOrigin != null)
            {
                xrOrigin = sessionOrigin.gameObject;
                Debug.Log($"Found AR Session Origin: {xrOrigin.name}");
            }
        }
        
        if (xrOrigin == null)
        {
            Debug.LogError("Cannot find XR Origin or AR Session Origin in the scene!");
            return;
        }
        
        // Добавляем ARAnchorManager если его нет
        _anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
        if (_anchorManager == null)
        {
            _anchorManager = xrOrigin.AddComponent<ARAnchorManager>();
            Debug.Log("Added ARAnchorManager component");
        }
        
        // Добавляем ARWallAnchor если его нет
        _wallAnchor = xrOrigin.GetComponent<ARWallAnchor>();
        if (_wallAnchor == null)
        {
            _wallAnchor = xrOrigin.AddComponent<ARWallAnchor>();
            Debug.Log("Added ARWallAnchor component");
        }
        
        // Добавляем WallAnchorManager к текущему объекту (ARScene)
        _wallAnchorManager = gameObject.GetComponent<WallAnchorManager>();
        if (_wallAnchorManager == null)
        {
            _wallAnchorManager = gameObject.AddComponent<WallAnchorManager>();
            Debug.Log("Added WallAnchorManager component");
            
            // Устанавливаем материал для стен через публичное свойство
            if (_anchoredWallMaterial != null)
            {
                _wallAnchorManager.AnchoredWallMaterial = _anchoredWallMaterial;
                Debug.Log("Set anchored wall material");
            }
        }
        
        Debug.Log("Wall anchoring system initialized successfully!");
    }
    
    /// <summary>
    /// Обновляет якори стен (можно вызвать через UI кнопку)
    /// </summary>
    public void RefreshWallAnchors()
    {
        if (_wallAnchorManager != null)
        {
            _wallAnchorManager.RefreshWalls();
            Debug.Log("Wall anchors refreshed");
        }
        else
        {
            Debug.LogWarning("WallAnchorManager not found! Initializing first...");
            InitializeAnchorSystem();
            
            // Повторяем попытку обновления
            if (_wallAnchorManager != null)
            {
                _wallAnchorManager.RefreshWalls();
            }
        }
    }
} 