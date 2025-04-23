using UnityEngine;
using UnityEditor;

// This script helps prevent Burst Compiler from failing when trying to resolve Assembly-CSharp-Editor
// by explicitly excluding it from Burst compilation
[InitializeOnLoad]
public class BurstCompileExcludeSetup
{
    static BurstCompileExcludeSetup()
    {
        // On editor load, add Assembly-CSharp-Editor to the excluded assemblies
        // This solves the "Failed to resolve assembly: 'Assembly-CSharp-Editor" error
        try 
        {
            // Use reflection to set the excluded assemblies since the property might not be directly accessible
            var burstCompilerType = System.Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
            if (burstCompilerType != null)
            {
                var optionsField = burstCompilerType.GetProperty("Options", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                
                if (optionsField != null)
                {
                    var options = optionsField.GetValue(null);
                    var excludedAssembliesProperty = options.GetType().GetProperty("ExcludedAssemblies");
                    
                    if (excludedAssembliesProperty != null)
                    {
                        var currentExcluded = excludedAssembliesProperty.GetValue(options) as string[];
                        
                        // Check if Assembly-CSharp-Editor is already excluded
                        bool alreadyExcluded = false;
                        if (currentExcluded != null)
                        {
                            foreach (var assembly in currentExcluded)
                            {
                                if (assembly == "Assembly-CSharp-Editor")
                                {
                                    alreadyExcluded = true;
                                    break;
                                }
                            }
                        }
                        
                        // If not already excluded, add it to the list
                        if (!alreadyExcluded)
                        {
                            string[] newExcluded;
                            if (currentExcluded != null)
                            {
                                newExcluded = new string[currentExcluded.Length + 1];
                                System.Array.Copy(currentExcluded, newExcluded, currentExcluded.Length);
                                newExcluded[currentExcluded.Length] = "Assembly-CSharp-Editor";
                            }
                            else
                            {
                                newExcluded = new string[] { "Assembly-CSharp-Editor" };
                            }
                            
                            // Set the updated excluded assemblies list
                            excludedAssembliesProperty.SetValue(options, newExcluded);
                            
                            Debug.Log("BurstCompileExcludeSetup: Successfully excluded Assembly-CSharp-Editor from Burst compilation");
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"BurstCompileExcludeSetup: Failed to set up excluded assemblies: {e.Message}");
        }
    }
} 