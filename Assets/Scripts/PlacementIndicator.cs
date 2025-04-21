using UnityEngine;

/// <summary>
/// Simple script to control the appearance of the AR placement indicator
/// </summary>
public class PlacementIndicator : MonoBehaviour
{
    [SerializeField] private GameObject visualObject;
    [SerializeField] private float rotationSpeed = 50f;
    [SerializeField] private float pulseSpeed = 1f;
    [SerializeField] private float pulseMin = 0.8f;
    [SerializeField] private float pulseMax = 1.2f;
    
    private Vector3 initialScale;
    
    private void Awake()
    {
        if (visualObject == null)
            visualObject = transform.GetChild(0).gameObject;
            
        initialScale = visualObject.transform.localScale;
    }
    
    private void Update()
    {
        // Rotate the indicator
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        // Pulse effect
        float pulse = Mathf.Lerp(pulseMin, pulseMax, (Mathf.Sin(Time.time * pulseSpeed) + 1) / 2);
        visualObject.transform.localScale = initialScale * pulse;
    }
} 