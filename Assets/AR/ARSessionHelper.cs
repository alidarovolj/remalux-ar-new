using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using UnityEngine.UI;

/// <summary>
/// Helper class to manage AR session initialization and status
/// </summary>
public class ARSessionHelper : MonoBehaviour
{
    [SerializeField] private ARSession arSession;
    [SerializeField] private Text statusText;
    [SerializeField] private float initializationTimeout = 10f;
    
    private bool isInitialized = false;
    public bool IsInitialized => isInitialized;

    private void Awake()
    {
        // Auto-find components if not set
        if (arSession == null)
            arSession = FindFirstObjectByType<ARSession>();
            
        if (statusText == null)
            statusText = GameObject.Find("Status Text")?.GetComponent<Text>();
    }
    
    private void Start()
    {
        // Make sure AR Session is initially disabled to avoid auto-start issues
        if (arSession != null)
            arSession.enabled = false;
            
        StartCoroutine(InitializeARSession());
    }
    
    /// <summary>
    /// Initialize AR Session with proper error handling
    /// </summary>
    private IEnumerator InitializeARSession()
    {
        UpdateStatus("Checking AR Support...");
        
        // Wait for AR subsystems to initialize
        yield return ARSession.CheckAvailability();
        
        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            UpdateStatus("AR software update required. Installing...");
            yield return ARSession.Install();
        }
        
        if (ARSession.state == ARSessionState.Unsupported)
        {
            UpdateStatus("AR is not supported on this device");
            yield break;
        }
        
        // Enable AR Session
        if (arSession != null)
        {
            arSession.enabled = true;
            UpdateStatus("AR session initializing...");
            
            // Wait for session to initialize with timeout
            float timer = 0f;
            while (ARSession.state != ARSessionState.Ready && timer < initializationTimeout)
            {
                yield return null;
                timer += Time.deltaTime;
                
                if (timer > initializationTimeout)
                {
                    UpdateStatus("AR initialization timed out. Please restart.");
                    yield break;
                }
            }
            
            if (ARSession.state == ARSessionState.Ready)
            {
                isInitialized = true;
                UpdateStatus("AR Ready - Processing automatically");
                Debug.Log("AR Session initialized successfully");
            }
            else
            {
                UpdateStatus($"AR not ready: {ARSession.state}");
                Debug.LogWarning($"AR Session initialization failed: {ARSession.state}");
            }
        }
        else
        {
            UpdateStatus("AR Session component not found");
            Debug.LogError("ARSession component not found");
        }
    }
    
    /// <summary>
    /// Manually start AR session
    /// </summary>
    public void StartSession()
    {
        if (arSession != null && !arSession.enabled)
        {
            arSession.enabled = true;
            Debug.Log("AR Session manually started");
        }
    }
    
    /// <summary>
    /// Update the UI status text
    /// </summary>
    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        
        Debug.Log("AR Status: " + message);
    }
    
    /// <summary>
    /// Check if the AR session is ready
    /// </summary>
    public bool IsSessionReady()
    {
        return isInitialized && ARSession.state == ARSessionState.Ready;
    }

    /// <summary>
    /// Alias method for backwards compatibility
    /// </summary>
    public bool IsARSessionReady()
    {
        return IsSessionReady();
    }
} 