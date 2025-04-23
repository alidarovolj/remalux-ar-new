using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(ARPlaneManager))]
public class PlaneVisualizer : MonoBehaviour
{
    [SerializeField]
    private Material planeMaterial;

    private ARPlaneManager planeManager;

    private void Awake()
    {
        planeManager = GetComponent<ARPlaneManager>();

        // Создаем базовый материал, если не назначен
        if (planeMaterial == null)
        {
            Shader standardShader = Shader.Find("Standard");
            
            // Check if shader was found
            if (standardShader != null)
            {
                planeMaterial = new Material(standardShader);
                planeMaterial.color = new Color(0.0f, 0.8f, 1.0f, 0.5f);
            }
            else
            {
                // Try alternative shaders
                Shader alternativeShader = Shader.Find("Universal Render Pipeline/Lit");
                if (alternativeShader != null)
                {
                    planeMaterial = new Material(alternativeShader);
                    planeMaterial.color = new Color(0.0f, 0.8f, 1.0f, 0.5f);
                }
                else
                {
                    // Last resort - create a simple color material
                    planeMaterial = new Material(Shader.Find("Unlit/Color"));
                    if (planeMaterial.shader != null)
                    {
                        planeMaterial.color = new Color(0.0f, 0.8f, 1.0f, 0.5f);
                    }
                    else
                    {
                        // Create a default material without a custom shader
                        planeMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                        Debug.LogWarning("PlaneVisualizer: Could not find any suitable shader. Using default fallback shader.");
                    }
                }
            }
        }
    }

    private void OnEnable()
    {
        planeManager.planesChanged += OnPlanesChanged;
    }

    private void OnDisable()
    {
        planeManager.planesChanged -= OnPlanesChanged;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        foreach (var plane in args.added)
        {
            UpdatePlaneMaterial(plane);
        }

        foreach (var plane in args.updated)
        {
            UpdatePlaneMaterial(plane);
        }
    }

    private void UpdatePlaneMaterial(ARPlane plane)
    {
        var planeVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (planeVisualizer != null)
        {
            var meshRenderer = planeVisualizer.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.material = planeMaterial;
            }
        }
    }
} 