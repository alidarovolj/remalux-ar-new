using UnityEngine;
using OpenCVRect = OpenCVForUnity.CoreModule.Rect;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections;
using Unity.Barracuda;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using System;
using Unity.Collections;
using System.Runtime.InteropServices;

public class WallDetectionManager : MonoBehaviour
{
    [Header("AR Components")]
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private ARSession arSession;
    
    [Header("DeepLabv3 Model")]
    [SerializeField] private NNModel deepLabModel;
    private Model runtimeModel;
    private IWorker engine;
    
    [Header("Visualization")]
    [SerializeField] private Material wallMaterial;
    [SerializeField] private float detectionDistance = 2f;
    [SerializeField] private GameObject wallVisualizerPrefab;
    
    [Header("Camera Movement")]
    [SerializeField] private float movementSpeed = 2.0f;
    [SerializeField] private float rotationSpeed = 100.0f;
    [SerializeField] private bool enableCameraMovement = true;
    
    private Texture2D cameraTexture;
    private Mat currentFrame;
    private bool isProcessing = false;
    private List<GameObject> detectedWalls = new List<GameObject>();
    
    // Add this field to store the rotation
    private Quaternion lastWallRotation;
    
    private void Start()
    {
        if (arCameraManager == null)
        {
            arCameraManager = FindFirstObjectByType<ARCameraManager>();
        }
        
        if (arSession == null)
        {
            arSession = FindFirstObjectByType<ARSession>();
        }
        
        // Initialize DeepLabv3 model
        if (deepLabModel != null)
        {
            runtimeModel = ModelLoader.Load(deepLabModel);
            engine = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);
        }
        
        // Subscribe to AR camera frame events
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnCameraFrameReceived;
        }
        
        // Check AR session status
        StartCoroutine(EnsureARSessionReady());
        
        // Initialize camera texture
        cameraTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
    }
    
    private void OnDestroy()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
        }
        
        if (engine != null)
        {
            engine.Dispose();
        }
        
        // Clean up detected walls
        foreach (var wall in detectedWalls)
        {
            Destroy(wall);
        }
        detectedWalls.Clear();
    }
    
    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (isProcessing) return;
        
        StartCoroutine(ProcessCameraFrame());
    }
    
    private IEnumerator ProcessCameraFrame()
    {
        isProcessing = true;
        
        // Get the current camera frame
        if (arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            using (image)
            {
                // Convert XRCpuImage to Texture2D
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(image.width, image.height),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };
                
                // Create texture if needed
                if (cameraTexture == null || cameraTexture.width != conversionParams.outputDimensions.x || 
                    cameraTexture.height != conversionParams.outputDimensions.y)
                {
                    cameraTexture = new Texture2D(
                        conversionParams.outputDimensions.x,
                        conversionParams.outputDimensions.y,
                        conversionParams.outputFormat, false);
                }
                
                // Calculate buffer size and allocate
                int size = image.GetConvertedDataSize(conversionParams);
                var buffer = new byte[size];
                GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                
                try
                {
                    // Convert to buffer
                    image.Convert(conversionParams, handle.AddrOfPinnedObject(), buffer.Length);
                    
                    // Load into texture
                    cameraTexture.LoadRawTextureData(buffer);
                    cameraTexture.Apply();
                    
                    // Convert Texture2D to OpenCV Mat
                    currentFrame = new Mat(cameraTexture.height, cameraTexture.width, CvType.CV_8UC4);
                    Utils.texture2DToMat(cameraTexture, currentFrame);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Exception during AR image processing: {e.Message}");
                }
                finally
                {
                    handle.Free();
                }
                
                // Process the frame with DeepLabv3 if we have a valid frame
                if (currentFrame != null && !currentFrame.empty())
                {
                    yield return StartCoroutine(ProcessFrameWithDeepLab(currentFrame));
                }
            }
        }
        
        isProcessing = false;
    }
    
    private IEnumerator ProcessFrameWithDeepLab(Mat frame)
    {
        // Preprocess the frame for DeepLabv3
        Mat resizedFrame = new Mat();
        Imgproc.resize(frame, resizedFrame, new Size(513, 513));
        
        // Convert to float and normalize
        Mat floatFrame = new Mat();
        resizedFrame.convertTo(floatFrame, CvType.CV_32F, 1.0 / 255.0);
        
        // Create input tensor - RGB order with normalization to [-1,1] range
        Tensor input = new Tensor(1, 513, 513, 3);
        
        // Fill tensor with frame data
        float[] inputData = new float[513 * 513 * 3];
        int idx = 0;
        
        // Extract RGB values and fill tensor
        for (int y = 0; y < 513; y++)
        {
            for (int x = 0; x < 513; x++)
            {
                float[] pixelData = new float[4];
                floatFrame.get(y, x, pixelData);
                
                // BGR to RGB and normalize to [-1,1]
                inputData[idx++] = (pixelData[2] * 2.0f) - 1.0f; // R
                inputData[idx++] = (pixelData[1] * 2.0f) - 1.0f; // G
                inputData[idx++] = (pixelData[0] * 2.0f) - 1.0f; // B
            }
        }
        
        input.data.Upload(inputData, new TensorShape(1, 513, 513, 3));
        
        // Run inference
        engine.Execute(input);
        Tensor output = engine.PeekOutput();
        
        // Process the output to detect walls
        ProcessWallSegmentation(output);
        
        // Clean up
        input.Dispose();
        output.Dispose();
        resizedFrame.Dispose();
        floatFrame.Dispose();
        
        yield return null;
    }
    
    private void ProcessWallSegmentation(Tensor segmentationOutput)
    {
        // Get the segmentation mask from the output tensor
        float[] maskData = segmentationOutput.data.Download(segmentationOutput.shape);
        
        // Create a binary mask for walls (assuming wall class is 1)
        Mat wallMask = new Mat(513, 513, CvType.CV_8UC1);
        byte[] wallMaskData = new byte[513 * 513];
        
        for (int i = 0; i < maskData.Length; i += 21) // 21 classes in DeepLabv3
        {
            int maxClass = 0;
            float maxValue = float.MinValue;
            
            for (int j = 0; j < 21; j++)
            {
                if (maskData[i + j] > maxValue)
                {
                    maxValue = maskData[i + j];
                    maxClass = j;
                }
            }
            
            // Set wall pixels (class 1) to 255, others to 0
            wallMaskData[i / 21] = (byte)(maxClass == 1 ? 255 : 0);
        }
        
        wallMask.put(0, 0, wallMaskData);
        
        // Find contours in the wall mask
        List<MatOfPoint> contours = new List<MatOfPoint>();
        Mat hierarchy = new Mat();
        Imgproc.findContours(wallMask, contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
        
        // Process each contour
        foreach (var contour in contours)
        {
            // Filter out small contours
            if (Imgproc.contourArea(contour) < 100) continue;
            
            // Get the bounding rectangle
            OpenCVRect rect = Imgproc.boundingRect(contour);
            
            // Create a wall visualizer
            CreateWallVisualizer(rect);
        }
        
        // Clean up
        wallMask.Dispose();
        hierarchy.Dispose();
        foreach (var contour in contours)
        {
            contour.Dispose();
        }
    }
    
    private void CreateWallVisualizer(OpenCVRect wallRect)
    {
        // Calculate wall position in 3D space
        Vector3 wallPosition = CalculateWallPosition(wallRect);
        
        // Create wall visualizer with the calculated rotation
        GameObject wallVisualizer = Instantiate(wallVisualizerPrefab, wallPosition, lastWallRotation);
        wallVisualizer.transform.parent = transform;
        
        // Calculate better wall dimensions based on distance and viewport size
        float distanceScale = detectionDistance * 0.1f; // Scale based on distance
        
        // Set wall dimensions
        wallVisualizer.transform.localScale = new Vector3(
            wallRect.width * distanceScale,
            wallRect.height * distanceScale,
            0.1f // Wall thickness
        );
        
        // Apply material
        MeshRenderer renderer = wallVisualizer.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = wallMaterial;
        }
        
        detectedWalls.Add(wallVisualizer);
    }
    
    private Vector3 CalculateWallPosition(OpenCVRect wallRect)
    {
        // Convert rect center to normalized device coordinates (0-1)
        Vector2 normalizedPoint = new Vector2(
            (float)(wallRect.x + wallRect.width/2) / cameraTexture.width,
            (float)(wallRect.y + wallRect.height/2) / cameraTexture.height
        );
        
        // Create a ray from the camera through this point
        Vector2 screenPoint = new Vector2(
            normalizedPoint.x * Screen.width,
            normalizedPoint.y * Screen.height
        );
        
        Ray ray = Camera.main.ScreenPointToRay(screenPoint);
        
        // Place the wall at the specified detection distance along this ray
        Vector3 worldPoint = ray.origin + ray.direction * detectionDistance;
        
        // Make the wall face the camera
        Quaternion rotation = Quaternion.LookRotation(-ray.direction);
        
        // Store the rotation for use when creating the wall
        lastWallRotation = rotation;
        
        return worldPoint;
    }

    private void Update()
    {
        if (enableCameraMovement)
        {
            HandleCameraMovement();
        }
    }

    private void HandleCameraMovement()
    {
        // Get camera transform
        Transform cameraTransform = Camera.main.transform;
        
        // Movement
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");
        
        Vector3 movement = new Vector3(horizontal, 0, vertical) * movementSpeed * Time.deltaTime;
        cameraTransform.Translate(movement, Space.Self);
        
        // Rotation with mouse
        if (Input.GetMouseButton(1)) // Right mouse button
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed * Time.deltaTime;
            
            cameraTransform.Rotate(Vector3.up, mouseX, Space.World);
            cameraTransform.Rotate(Vector3.right, -mouseY, Space.Self);
        }
        
        // Allow clearing detected walls with a key press
        if (Input.GetKeyDown(KeyCode.C))
        {
            ClearDetectedWalls();
        }
    }
    
    private void ClearDetectedWalls()
    {
        foreach (var wall in detectedWalls)
        {
            Destroy(wall);
        }
        detectedWalls.Clear();
        Debug.Log("Cleared all detected walls");
    }

    private IEnumerator EnsureARSessionReady()
    {
        if (arSession == null)
        {
            Debug.LogError("ARSession not found. Make sure AR components are properly set up.");
            yield break;
        }
        
        // Wait for AR session to initialize
        while (ARSession.state != ARSessionState.Ready)
        {
            if (ARSession.state == ARSessionState.Unsupported)
            {
                Debug.LogError("AR is not supported on this device");
                yield break;
            }
            
            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                Debug.Log("AR software update required. Installing...");
                yield return ARSession.Install();
            }
            
            Debug.Log("Waiting for AR session to be ready: " + ARSession.state);
            yield return new WaitForSeconds(0.5f);
            
            // Time out after 10 seconds
            if (Time.time > 10f)
            {
                Debug.LogWarning("AR session initialization timed out. Proceeding anyway.");
                break;
            }
        }
        
        Debug.Log("AR Session is ready: " + ARSession.state);
        
        // Reset the session if it was previously in an error state
        if (arSession.enabled == false)
        {
            arSession.enabled = true;
        }
    }
} 