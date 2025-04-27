using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// Менеджер для закрепления стен в реальном мире,
/// даже если они постоянно пересоздаются системой обнаружения
/// </summary>
public class WallAnchorManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARWallAnchor _wallAnchor;
    [SerializeField] private Transform _wallSystem;
    [SerializeField] private Transform _wallDetectionSystem;
    
    [Header("Settings")]
    [SerializeField] private float _scanInterval = 0.5f; // Как часто проверять новые стены
    [SerializeField] private float _updateInterval = 0.2f; // Как часто обновлять геометрию
    [SerializeField] private bool _debugMode = true;
    [SerializeField] private Material _anchoredWallMaterial; // Материал для закрепленных стен
    
    // Публичное свойство для доступа к материалу
    public Material AnchoredWallMaterial 
    {
        get { return _anchoredWallMaterial; }
        set { _anchoredWallMaterial = value; }
    }
    
    // Хранит стабильные представители стен
    private List<GameObject> _anchoredWalls = new List<GameObject>();
    private Dictionary<string, GameObject> _wallIDToAnchoredWall = new Dictionary<string, GameObject>();
    
    // Информация о последних обнаруженных стенах
    private class WallInfo
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Mesh mesh;
        public Material material;
        public string id; // Уникальный ID для этой стены
        public bool isActive;
        public float lastUpdateTime;
        
        public WallInfo(Transform wall, MeshFilter meshFilter, MeshRenderer renderer)
        {
            position = wall.position;
            rotation = wall.rotation;
            scale = wall.localScale;
            
            if (meshFilter != null && meshFilter.sharedMesh != null)
                mesh = Object.Instantiate(meshFilter.sharedMesh);
            
            if (renderer != null && renderer.sharedMaterial != null)
                material = renderer.sharedMaterial;
                
            id = GenerateWallID(position, rotation);
            isActive = true;
            lastUpdateTime = Time.time;
        }
        
        // Генерирует уникальный ID для стены на основе позиции и поворота
        private string GenerateWallID(Vector3 pos, Quaternion rot)
        {
            // Округляем значения, чтобы похожие стены получали один ID
            Vector3 roundedPos = new Vector3(
                Mathf.Round(pos.x * 10) / 10f,
                Mathf.Round(pos.y * 10) / 10f,
                Mathf.Round(pos.z * 10) / 10f
            );
            
            // Используем углы Эйлера для упрощения
            Vector3 angles = rot.eulerAngles;
            Vector3 roundedAngles = new Vector3(
                Mathf.Round(angles.x / 5) * 5,
                Mathf.Round(angles.y / 5) * 5,
                Mathf.Round(angles.z / 5) * 5
            );
            
            return $"Wall_{roundedPos.x}_{roundedPos.y}_{roundedPos.z}_{roundedAngles.y}";
        }
    }
    
    // Словарь для отслеживания стен
    private Dictionary<string, WallInfo> _wallInfos = new Dictionary<string, WallInfo>();
    private ARAnchorManager _anchorManager;
    
    private void Awake()
    {
        // Автоматический поиск компонентов, если они не назначены
        if (_wallAnchor == null)
            _wallAnchor = FindObjectOfType<ARWallAnchor>();
            
        if (_wallSystem == null)
            _wallSystem = GameObject.Find("Wall System")?.transform;
            
        if (_wallDetectionSystem == null)
            _wallDetectionSystem = GameObject.Find("Wall Detection System")?.transform;
            
        _anchorManager = FindObjectOfType<ARAnchorManager>();
    }
    
    private void Start()
    {
        // Создаем контейнер для привязанных стен, если его еще нет
        GameObject anchoredWallsContainer = GameObject.Find("Anchored Walls");
        if (anchoredWallsContainer == null)
        {
            anchoredWallsContainer = new GameObject("Anchored Walls");
            anchoredWallsContainer.transform.SetParent(transform);
        }
        
        // Запускаем периодическое сканирование стен
        StartCoroutine(ScanWallsRoutine());
        StartCoroutine(UpdateAnchoredWallsRoutine());
    }
    
    /// <summary>
    /// Периодически сканирует сцену на наличие новых/изменившихся стен
    /// </summary>
    private IEnumerator ScanWallsRoutine()
    {
        while (true)
        {
            ScanWalls();
            yield return new WaitForSeconds(_scanInterval);
        }
    }
    
    /// <summary>
    /// Периодически обновляет закрепленные стены
    /// </summary>
    private IEnumerator UpdateAnchoredWallsRoutine()
    {
        while (true)
        {
            UpdateAnchoredWalls();
            yield return new WaitForSeconds(_updateInterval);
        }
    }
    
    /// <summary>
    /// Сканирует сцену на наличие стен
    /// </summary>
    private void ScanWalls()
    {
        // Помечаем все стены как неактивные перед сканированием
        foreach (var wall in _wallInfos.Values)
        {
            wall.isActive = false;
        }
        
        // Сканируем Wall System
        if (_wallSystem != null)
            ScanTransformForWalls(_wallSystem);
        
        // Сканируем Wall Detection System
        if (_wallDetectionSystem != null)
            ScanTransformForWalls(_wallDetectionSystem);
        
        // Удаляем устаревшие стены (исчезнувшие более 2 секунд назад)
        List<string> wallsToRemove = new List<string>();
        foreach (var kvp in _wallInfos)
        {
            if (!kvp.Value.isActive && Time.time - kvp.Value.lastUpdateTime > 2.0f)
            {
                wallsToRemove.Add(kvp.Key);
            }
        }
        
        foreach (string wallID in wallsToRemove)
        {
            if (_debugMode)
                Debug.Log($"Removing wall info for: {wallID} (no longer active)");
            _wallInfos.Remove(wallID);
        }
    }
    
    /// <summary>
    /// Рекурсивно проверяет объект и его дочерние объекты на наличие стен
    /// </summary>
    private void ScanTransformForWalls(Transform parent)
    {
        // Проверяем текущий объект
        MeshFilter meshFilter = parent.GetComponent<MeshFilter>();
        MeshRenderer renderer = parent.GetComponent<MeshRenderer>();
        
        if (IsWallObject(parent.gameObject, meshFilter, renderer))
        {
            ProcessWallObject(parent, meshFilter, renderer);
        }
        
        // Проверяем дочерние объекты
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            ScanTransformForWalls(child);
        }
    }
    
    /// <summary>
    /// Проверяет, является ли объект стеной
    /// </summary>
    private bool IsWallObject(GameObject obj, MeshFilter meshFilter, MeshRenderer renderer)
    {
        if (meshFilter == null || renderer == null || meshFilter.sharedMesh == null)
            return false;
            
        // Проверка имени объекта
        string objName = obj.name.ToLower();
        if (objName.Contains("wall") || objName.Contains("plane") || objName.Contains("mesh"))
            return true;
            
        // Проверка материала
        if (renderer.sharedMaterial != null)
        {
            string matName = renderer.sharedMaterial.name.ToLower();
            if (matName.Contains("wall") || matName.Contains("plane"))
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Обрабатывает обнаруженную стену
    /// </summary>
    private void ProcessWallObject(Transform wallTransform, MeshFilter meshFilter, MeshRenderer renderer)
    {
        // Создаем информацию о стене
        WallInfo wallInfo = new WallInfo(wallTransform, meshFilter, renderer);
        
        // Если эта стена уже есть в нашем словаре - обновляем информацию
        if (_wallInfos.ContainsKey(wallInfo.id))
        {
            _wallInfos[wallInfo.id].position = wallInfo.position;
            _wallInfos[wallInfo.id].rotation = wallInfo.rotation;
            _wallInfos[wallInfo.id].scale = wallInfo.scale;
            
            // Обновляем меш только если он существенно изменился
            if (wallInfo.mesh != null && IsSignificantlyDifferentMesh(wallInfo.mesh, _wallInfos[wallInfo.id].mesh))
            {
                _wallInfos[wallInfo.id].mesh = wallInfo.mesh;
            }
            
            _wallInfos[wallInfo.id].isActive = true;
            _wallInfos[wallInfo.id].lastUpdateTime = Time.time;
        }
        else
        {
            // Добавляем новую стену
            _wallInfos.Add(wallInfo.id, wallInfo);
            
            if (_debugMode)
                Debug.Log($"Found new wall: {wallInfo.id} at {wallInfo.position}");
                
            // Создаем якорь для новой стены
            CreateAnchoredWall(wallInfo);
        }
    }
    
    /// <summary>
    /// Проверяет, существенно ли изменился меш
    /// </summary>
    private bool IsSignificantlyDifferentMesh(Mesh newMesh, Mesh oldMesh)
    {
        if (newMesh == null || oldMesh == null)
            return true;
            
        // Проверяем количество вершин как индикатор изменений
        if (newMesh.vertexCount != oldMesh.vertexCount)
            return true;
            
        // Можно добавить более сложную проверку при необходимости
        
        return false;
    }
    
    /// <summary>
    /// Создает постоянную привязанную версию стены
    /// </summary>
    private void CreateAnchoredWall(WallInfo wallInfo)
    {
        // Проверяем, есть ли уже закрепленная версия этой стены
        if (_wallIDToAnchoredWall.ContainsKey(wallInfo.id))
            return;
            
        // Создаем новый объект для стены
        GameObject anchoredWall = new GameObject($"Anchored_{wallInfo.id}");
        
        // Устанавливаем позицию и поворот
        anchoredWall.transform.position = wallInfo.position;
        anchoredWall.transform.rotation = wallInfo.rotation;
        anchoredWall.transform.localScale = wallInfo.scale;
        
        // Добавляем компоненты
        MeshFilter meshFilter = anchoredWall.AddComponent<MeshFilter>();
        MeshRenderer renderer = anchoredWall.AddComponent<MeshRenderer>();
        
        // Устанавливаем меш и материал
        if (wallInfo.mesh != null)
            meshFilter.sharedMesh = wallInfo.mesh;
            
        // Используем специальный материал для закрепленных стен, если он назначен
        renderer.sharedMaterial = _anchoredWallMaterial != null ? _anchoredWallMaterial : wallInfo.material;
        
        // Добавляем якорь
        if (_anchorManager != null)
        {
            GameObject anchorObject = new GameObject($"Anchor_{wallInfo.id}");
            anchorObject.transform.position = wallInfo.position;
            anchorObject.transform.rotation = wallInfo.rotation;
            
            ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();
            
            // Прикрепляем стену к якорю
            anchoredWall.transform.SetParent(anchorObject.transform, true);
        }
        
        // Добавляем в наш список
        _anchoredWalls.Add(anchoredWall);
        _wallIDToAnchoredWall[wallInfo.id] = anchoredWall;
        
        if (_debugMode)
            Debug.Log($"Created anchored wall: {anchoredWall.name}");
    }
    
    /// <summary>
    /// Обновляет геометрию закрепленных стен
    /// </summary>
    private void UpdateAnchoredWalls()
    {
        foreach (var kvp in _wallInfos)
        {
            string wallID = kvp.Key;
            WallInfo wallInfo = kvp.Value;
            
            if (_wallIDToAnchoredWall.TryGetValue(wallID, out GameObject anchoredWall))
            {
                // Стена уже привязана - обновляем только меш, если нужно
                if (wallInfo.isActive && wallInfo.mesh != null)
                {
                    MeshFilter meshFilter = anchoredWall.GetComponent<MeshFilter>();
                    if (meshFilter != null && IsSignificantlyDifferentMesh(wallInfo.mesh, meshFilter.sharedMesh))
                    {
                        meshFilter.sharedMesh = wallInfo.mesh;
                        
                        if (_debugMode)
                            Debug.Log($"Updated mesh for anchored wall: {anchoredWall.name}");
                    }
                }
            }
            else if (wallInfo.isActive)
            {
                // Создаем новую привязанную стену
                CreateAnchoredWall(wallInfo);
            }
        }
    }
    
    /// <summary>
    /// Вызывается при включении скрипта
    /// </summary>
    public void RefreshWalls()
    {
        // Очищаем текущие данные
        _wallInfos.Clear();
        
        // Удаляем существующие привязанные стены
        foreach (var wall in _anchoredWalls)
        {
            if (wall != null)
            {
                // Если есть родитель (якорь), удаляем его
                if (wall.transform.parent != null)
                    Destroy(wall.transform.parent.gameObject);
                else
                    Destroy(wall);
            }
        }
        
        _anchoredWalls.Clear();
        _wallIDToAnchoredWall.Clear();
        
        // Выполняем новое сканирование
        ScanWalls();
        UpdateAnchoredWalls();
        
        if (_debugMode)
            Debug.Log("Wall anchoring system refreshed");
    }
} 