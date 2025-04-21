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
            planeMaterial = new Material(Shader.Find("Standard"));
            planeMaterial.color = new Color(0.0f, 0.8f, 1.0f, 0.5f);
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