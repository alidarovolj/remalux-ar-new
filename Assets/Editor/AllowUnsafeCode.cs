using UnityEditor;
using System.Reflection;
using System.IO;
using UnityEngine;

[InitializeOnLoad]
public class AllowUnsafeCode
{
    static AllowUnsafeCode()
    {
        // Путь к файлу ответов компилятора
        string cscPath = Path.Combine(Application.dataPath, "csc.rsp");
        
        // Проверяем, существует ли файл и содержит ли он параметр -unsafe
        bool fileExists = File.Exists(cscPath);
        bool hasUnsafeFlag = false;
        
        if (fileExists)
        {
            string content = File.ReadAllText(cscPath);
            hasUnsafeFlag = content.Contains("-unsafe");
        }
        
        // Создаем файл, если он не существует или не содержит -unsafe
        if (!fileExists || !hasUnsafeFlag)
        {
            File.WriteAllText(cscPath, "-unsafe");
            UnityEngine.Debug.Log("AllowUnsafeCode: Created or updated csc.rsp with -unsafe flag");
            
            // Запрос на перекомпиляцию
            AssetDatabase.Refresh();
        }
    }
}
