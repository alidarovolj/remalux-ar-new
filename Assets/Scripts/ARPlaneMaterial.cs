using UnityEngine;

/// <summary>
/// Класс для создания и настройки материалов для AR плоскостей
/// </summary>
public static class ARPlaneMaterial
{
    /// <summary>
    /// Создает сетчатый материал с прозрачностью для AR плоскостей
    /// </summary>
    /// <param name="baseColor">Основной цвет материала</param>
    /// <param name="alpha">Прозрачность от 0 до 1</param>
    /// <param name="gridSize">Размер ячейки сетки в метрах</param>
    /// <returns>Новый материал с сеткой</returns>
    public static Material CreateGridMaterial(Color baseColor, float alpha = 0.5f, float gridSize = 0.1f)
    {
        // Ищем подходящий шейдер
        Shader shader = Shader.Find("Transparent/Diffuse");
        if (shader == null)
        {
            // Запасной вариант
            shader = Shader.Find("Unlit/Transparent");
        }
        
        if (shader == null)
        {
            // Крайний случай - используем стандартный шейдер
            shader = Shader.Find("Standard");
            Debug.LogWarning("ARPlaneMaterial: Could not find transparent shader, using Standard instead");
        }
        
        // Создаем материал
        Material material = new Material(shader);
        
        // Настраиваем цвет с прозрачностью
        baseColor.a = alpha;
        material.color = baseColor;
        
        // Создаем текстуру сетки программно
        Texture2D gridTexture = CreateGridTexture(baseColor, 64);
        if (gridTexture != null)
        {
            material.mainTexture = gridTexture;
            material.mainTextureScale = new Vector2(1f / gridSize, 1f / gridSize);
        }
        
        // Настраиваем режим прозрачности для стандартного шейдера
        if (shader.name == "Standard")
        {
            material.SetFloat("_Mode", 3); // Transparent mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
        
        return material;
    }
    
    /// <summary>
    /// Создает текстуру с сеткой программно
    /// </summary>
    /// <param name="baseColor">Основной цвет сетки</param>
    /// <param name="textureSize">Размер текстуры в пикселях</param>
    /// <returns>Текстура с сеткой</returns>
    private static Texture2D CreateGridTexture(Color baseColor, int textureSize = 256)
    {
        try
        {
            Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Repeat;
            
            // Создаем цвета для фона и линий
            Color backgroundColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.1f);
            Color lineColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.8f);
            
            // Заполняем текстуру
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    // Рисуем линии сетки
                    if (x == 0 || y == 0 || x == textureSize - 1 || y == textureSize - 1)
                    {
                        texture.SetPixel(x, y, lineColor);
                    }
                    else if (x % (textureSize / 8) == 0 || y % (textureSize / 8) == 0)
                    {
                        texture.SetPixel(x, y, lineColor);
                    }
                    else
                    {
                        texture.SetPixel(x, y, backgroundColor);
                    }
                }
            }
            
            texture.Apply();
            return texture;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"ARPlaneMaterial: Error creating grid texture: {e.Message}");
            return null;
        }
    }
} 