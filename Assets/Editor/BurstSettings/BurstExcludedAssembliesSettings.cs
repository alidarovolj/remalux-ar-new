using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace EditorExtensions.BurstSettings
{
    // Class to define excluded assemblies for Burst Compilation
    public class BurstExcludedAssembliesSettings : ScriptableObject
    {
        public List<string> m_ExcludedAssemblies = new List<string>();
        
        // Method to create settings asset if it doesn't exist
        public static BurstExcludedAssembliesSettings GetOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BurstExcludedAssembliesSettings>(
                "Assets/Editor/BurstSettings/BurstExcludedAssemblies.asset");
                
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<BurstExcludedAssembliesSettings>();
                settings.m_ExcludedAssemblies.Add("Assembly-CSharp-Editor");
                
                if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                    AssetDatabase.CreateFolder("Assets", "Editor");
                    
                if (!AssetDatabase.IsValidFolder("Assets/Editor/BurstSettings"))
                    AssetDatabase.CreateFolder("Assets/Editor", "BurstSettings");
                    
                AssetDatabase.CreateAsset(settings, "Assets/Editor/BurstSettings/BurstExcludedAssemblies.asset");
                AssetDatabase.SaveAssets();
            }
            
            return settings;
        }
    }
    
    // Register settings provider for Burst compilation settings
    static class BurstRegisterSettings
    {
        [InitializeOnLoadMethod]
        static void RegisterBurstSettings()
        {
            // Make sure settings exist
            BurstExcludedAssembliesSettings.GetOrCreateSettings();
            
            // Set up Project Settings entry if needed
            // This is optional but helps with visibility
            
            // This is the manual way to exclude assemblies from Burst compilation
            // Unity.Burst.BurstCompiler.Options.ExcludedAssemblies = new string[] { "Assembly-CSharp-Editor" };
        }
    }
} 