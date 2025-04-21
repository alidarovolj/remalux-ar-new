using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARManager : MonoBehaviour
{
    [SerializeField] private ARSession arSession;
    [SerializeField] private ARPlaneManager planeManager;
    
    private bool isSessionInitialized = false;

    private void Start()
    {
        if (ARSession.state == ARSessionState.None || ARSession.state == ARSessionState.CheckingAvailability)
        {
            StartCoroutine(CheckARSupport());
        }
        else if (ARSession.state == ARSessionState.Ready || ARSession.state == ARSessionState.SessionInitializing || ARSession.state == ARSessionState.SessionTracking)
        {
            isSessionInitialized = true;
        }
    }

    private System.Collections.IEnumerator CheckARSupport()
    {
        yield return ARSession.CheckAvailability();

        if (ARSession.state == ARSessionState.Unsupported)
        {
            Debug.LogError("AR is not supported on this device");
            // Handle unsupported case
        }
        else
        {
            arSession.enabled = true;
            isSessionInitialized = true;
        }
    }

    public void StartAR()
    {
        if (!isSessionInitialized)
        {
            Debug.LogWarning("ARManager: AR Session is not initialized yet");
            return;
        }
        
        if (arSession != null && !arSession.enabled)
        {
            arSession.enabled = true;
        }
        
        TogglePlaneDetection(true);
        
        Debug.Log("ARManager: AR Session started");
    }
    
    public void StopAR()
    {
        TogglePlaneDetection(false);
        
        Debug.Log("ARManager: AR Scanning stopped");
    }

    public void TogglePlaneDetection(bool enable)
    {
        if (planeManager != null)
        {
            planeManager.enabled = enable;
        }
    }

    public bool IsSessionReady()
    {
        // Check if the session is initialized AND in a ready/tracking state
        return isSessionInitialized && 
               (ARSession.state == ARSessionState.Ready || 
                ARSession.state == ARSessionState.SessionInitializing || 
                ARSession.state == ARSessionState.SessionTracking);
    }
} 