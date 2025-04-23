using UnityEngine;

/// <summary>
/// Simple marker component to identify walls when tagging is not available
/// </summary>
public class WallIdentifier : MonoBehaviour
{
    // This is an empty marker component
    // Its presence on a GameObject indicates it's a wall

    private void Awake()
    {
        // Attempt to tag this as a wall if the tag exists
        try 
        {
            if (!gameObject.CompareTag("Wall"))
            {
                gameObject.tag = "Wall";
            }
        }
        catch (System.Exception)
        {
            // Tag doesn't exist, keep using this component as a marker
        }
    }
} 