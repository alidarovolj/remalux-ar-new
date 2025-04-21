using UnityEngine;
using UnityEngine.UI;

public class WallPainter : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RawImage outputDisplay;
    [SerializeField] private Material wallMaterial;
    
    [Header("Visualization")]
    [SerializeField] private Color selectedColor = Color.green;
    [SerializeField] [Range(0f, 1f)] private float overlayAlpha = 0.5f;
    
    private RenderTexture m_MaskTexture;
    private bool m_HasNewMask = false;
    
    private void Start()
    {
        if (outputDisplay == null)
        {
            Debug.LogError("Output display reference is missing!");
            enabled = false;
            return;
        }
        
        if (wallMaterial == null)
        {
            Debug.LogError("Wall material reference is missing!");
            enabled = false;
            return;
        }
    }
    
    private void Update()
    {
        if (m_HasNewMask && m_MaskTexture != null)
        {
            UpdateMaskVisualization();
            m_HasNewMask = false;
        }
    }
    
    public void SetMask(Texture2D maskTexture)
    {
        // Create or resize render texture if needed
        if (m_MaskTexture == null || 
            m_MaskTexture.width != maskTexture.width || 
            m_MaskTexture.height != maskTexture.height)
        {
            if (m_MaskTexture != null)
                m_MaskTexture.Release();
                
            m_MaskTexture = new RenderTexture(
                maskTexture.width,
                maskTexture.height,
                0,
                RenderTextureFormat.ARGB32
            );
            m_MaskTexture.enableRandomWrite = true;
            m_MaskTexture.Create();
        }
        
        // Copy mask to render texture
        Graphics.Blit(maskTexture, m_MaskTexture);
        m_HasNewMask = true;
    }
    
    private void UpdateMaskVisualization()
    {
        wallMaterial.SetTexture("_MaskTex", m_MaskTexture);
        wallMaterial.SetColor("_Color", new Color(
            selectedColor.r,
            selectedColor.g,
            selectedColor.b,
            overlayAlpha
        ));
        
        outputDisplay.material = wallMaterial;
    }
    
    public void SetColor(Color color)
    {
        selectedColor = color;
        if (m_MaskTexture != null)
        {
            UpdateMaskVisualization();
        }
    }
    
    private void OnDestroy()
    {
        if (m_MaskTexture != null)
        {
            m_MaskTexture.Release();
            Destroy(m_MaskTexture);
        }
    }
} 