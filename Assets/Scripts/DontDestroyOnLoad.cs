using UnityEngine;

/// <summary>
/// Простой компонент, который не позволяет уничтожать GameObject при переходе между сценами
/// </summary>
public class DontDestroyOnLoad : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }
} 