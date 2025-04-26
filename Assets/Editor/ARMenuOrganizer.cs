using UnityEditor;
using UnityEngine;

/// <summary>
/// Организует пункты меню AR в Unity Editor
/// </summary>
[InitializeOnLoad]
public static class ARMenuOrganizer
{
    // InitializeOnLoad гарантирует, что статический конструктор вызывается при загрузке Unity
    static ARMenuOrganizer()
    {
        Debug.Log("AR Menu организация завершена. Теперь используйте только 'Setup AR Scene (All-in-one)'");
    }
    
    // Единственный пункт меню для настройки AR + ML
    [MenuItem("AR/Setup AR Scene (All-in-one)", false, 0)]
    private static void SetupARScene()
    {
        EditorWindow.GetWindow<ARSceneSetupAllInOne>("AR Scene Setup");
    }
    
    // Скрываем оригинальные пункты меню, добавляя их дублеры с приоритетом 100
    // В Unity меню с одинаковым названием и разным приоритетом удаляет меню с низким приоритетом
    
    [MenuItem("AR/Setup AR Scene (Basic)", true, 100)]
    private static bool ValidateSetupARScene() { return false; }
    
    [MenuItem("AR/Setup AR Scene (Native Camera)", true, 100)] 
    private static bool ValidateSetupARSceneNative() { return false; }
    
    [MenuItem("AR/ML Setup Wizard", true, 100)]
    private static bool ValidateMLSetupWizard() { return false; }
    
    [MenuItem("AR/Setup ML System", true, 100)]
    private static bool ValidateSetupMLSystem() { return false; }
    
    [MenuItem("AR/Add ML Debug Viewer", true, 100)]
    private static bool ValidateAddMLDebugViewer() { return false; }
    
    [MenuItem("AR/Fix AR Camera Setup", true, 100)]
    private static bool ValidateFixARCamera() { return false; }
    
    [MenuItem("AR/Fix Camera Display (Use Native AR Background)", true, 100)]
    private static bool ValidateFixCameraDisplay() { return false; }
} 