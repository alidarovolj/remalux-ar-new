using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Скрипт, который делает AR плоскости экстремально заметными, полностью переопределяя их материалы
/// с очень яркими, непрозрачными цветами.
/// </summary>
[RequireComponent(typeof(ARPlaneManager))]
public class ARPlaneHighlighter : MonoBehaviour
{
    [SerializeField]
    private Color verticalPlaneColor = new Color(1f, 0f, 1f, 1f); // Яркий розовый
    
    [SerializeField]
    private Color horizontalPlaneColor = new Color(0f, 1f, 1f, 1f); // Яркий голубой
    
    [SerializeField]
    private bool useEmission = true;
    
    [SerializeField]
    private float emissionIntensity = 0.5f;
    
    private ARPlaneManager planeManager;
    private Material verticalMaterial;
    private Material horizontalMaterial;
    
    private readonly Dictionary<ARPlane, Material> originalMaterials = new Dictionary<ARPlane, Material>();
    
    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();
        CreateMaterials();
    }
    
    private void OnEnable()
    {
        planeManager.planesChanged += OnPlanesChanged;
        StartCoroutine(HighlightExistingPlanesAfterDelay());
    }
    
    private void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
    }
    
    private void CreateMaterials()
    {
        // Вертикальный материал (для стен)
        verticalMaterial = new Material(Shader.Find("Standard"));
        verticalMaterial.color = verticalPlaneColor;
        verticalMaterial.SetFloat("_Mode", 0); // Opaque
        verticalMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        verticalMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        verticalMaterial.SetInt("_ZWrite", 1);
        verticalMaterial.DisableKeyword("_ALPHATEST_ON");
        verticalMaterial.DisableKeyword("_ALPHABLEND_ON");
        verticalMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        verticalMaterial.renderQueue = -1;
        
        if (useEmission)
        {
            verticalMaterial.EnableKeyword("_EMISSION");
            verticalMaterial.SetColor("_EmissionColor", verticalPlaneColor * emissionIntensity);
            verticalMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        
        // Горизонтальный материал (для пола/потолка)
        horizontalMaterial = new Material(Shader.Find("Standard"));
        horizontalMaterial.color = horizontalPlaneColor;
        horizontalMaterial.SetFloat("_Mode", 0); // Opaque
        horizontalMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        horizontalMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        horizontalMaterial.SetInt("_ZWrite", 1);
        horizontalMaterial.DisableKeyword("_ALPHATEST_ON");
        horizontalMaterial.DisableKeyword("_ALPHABLEND_ON");
        horizontalMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        horizontalMaterial.renderQueue = -1;
        
        if (useEmission)
        {
            horizontalMaterial.EnableKeyword("_EMISSION");
            horizontalMaterial.SetColor("_EmissionColor", horizontalPlaneColor * emissionIntensity);
            horizontalMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        
        Debug.Log("Созданы высококонтрастные материалы для AR плоскостей");
    }
    
    private IEnumerator HighlightExistingPlanesAfterDelay()
    {
        // Задержка для обеспечения корректной инициализации AR плоскостей
        yield return new WaitForSeconds(1.0f);
        
        foreach (ARPlane plane in planeManager.trackables)
        {
            HighlightPlane(plane);
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обработка добавленных плоскостей
        foreach (ARPlane plane in args.added)
        {
            HighlightPlane(plane);
        }
        
        // Обработка обновленных плоскостей
        foreach (ARPlane plane in args.updated)
        {
            // Проверка, не изменилась ли ориентация плоскости
            HighlightPlane(plane);
        }
        
        // Обработка удаленных плоскостей
        foreach (ARPlane plane in args.removed)
        {
            if (originalMaterials.ContainsKey(plane))
            {
                originalMaterials.Remove(plane);
            }
        }
    }
    
    private void HighlightPlane(ARPlane plane)
    {
        if (plane == null) return;
        
        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            // Сохраняем оригинальный материал, если еще не сохранен
            if (!originalMaterials.ContainsKey(plane))
            {
                originalMaterials[plane] = meshRenderer.material;
            }
            
            // Назначаем соответствующий материал в зависимости от ориентации
            if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
            {
                meshRenderer.material = verticalMaterial;
                Debug.Log("Выделена вертикальная плоскость: " + plane.trackableId);
            }
            else
            {
                meshRenderer.material = horizontalMaterial;
                Debug.Log("Выделена горизонтальная плоскость: " + plane.trackableId);
            }
            
            // Принудительно делаем плоскость видимой
            meshRenderer.enabled = true;
        }
    }
    
    [ContextMenu("Highlight All Planes Now")]
    public void HighlightAllPlanesNow()
    {
        foreach (ARPlane plane in planeManager.trackables)
        {
            HighlightPlane(plane);
        }
    }
    
    [ContextMenu("Restore Original Materials")]
    public void RestoreOriginalMaterials()
    {
        foreach (var entry in originalMaterials)
        {
            ARPlane plane = entry.Key;
            if (plane != null)
            {
                MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.material = entry.Value;
                }
            }
        }
        
        originalMaterials.Clear();
    }
} 