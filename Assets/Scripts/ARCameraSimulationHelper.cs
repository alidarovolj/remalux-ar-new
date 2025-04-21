using UnityEngine;

[ExecuteInEditMode]
public class ARCameraSimulationHelper : MonoBehaviour 
{
    public Camera arCamera;
    public Camera simulationCamera;
    
    void Update()
    {
        if (Application.isPlaying)
        {
            // In play mode, make sure AR camera is active and simulation is disabled
            if (arCamera != null) arCamera.enabled = true;
            if (simulationCamera != null) simulationCamera.enabled = false;
            return;
        }
        
        // In edit mode, use simulation camera and disable AR camera
        if (arCamera != null) arCamera.enabled = false;
        if (simulationCamera != null) 
        {
            simulationCamera.enabled = true;
            
            // Make simulation camera match the AR camera position initially
            if (arCamera != null)
            {
                if (simulationCamera.transform.position == Vector3.zero)
                {
                    simulationCamera.transform.position = arCamera.transform.position;
                    simulationCamera.transform.rotation = arCamera.transform.rotation;
                }
            }
        }
    }
} 