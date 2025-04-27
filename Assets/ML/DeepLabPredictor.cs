using UnityEngine;
using Unity.Barracuda;
using System;

namespace ML
{
    public class SegmentationPredictor : MonoBehaviour
    {
        [Header("Model Configuration")]
        [SerializeField] private NNModel modelAsset;
        [SerializeField] public int inputWidth = 513;
        [SerializeField] public int inputHeight = 513;
        [SerializeField] private string inputName = "ImageTensor";
        [SerializeField] private string outputName = "SemanticPredictions";
        [SerializeField] private WorkerFactory.Type backend = WorkerFactory.Type.Auto;
        
        private Model runtimeModel;
        private IWorker worker;
        private Texture2D resultTexture;
        
        private void Awake()
        {
            InitializeModel();
        }
        
        private void OnDestroy()
        {
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }
            
            if (resultTexture != null)
            {
                Destroy(resultTexture);
                resultTexture = null;
            }
        }
        
        private bool InitializeModel()
        {
            if (modelAsset == null)
            {
                Debug.LogError("Model asset is not assigned!");
                return false;
            }
            
            try
            {
                // Load model
                runtimeModel = ModelLoader.Load(modelAsset);
                
                // Create worker
                worker = WorkerFactory.CreateWorker(backend, runtimeModel);
                
                Debug.Log("SegmentationPredictor model initialized successfully");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error initializing model: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Process a frame from the camera
        /// </summary>
        /// <param name="texture">The camera texture to process</param>
        /// <param name="targetResolution">Optional target resolution</param>
        /// <returns>True if processing was successful</returns>
        public bool ProcessCameraFrame(Texture2D texture, Vector2Int targetResolution)
        {
            if (texture == null)
            {
                Debug.LogError("Cannot process null texture");
                return false;
            }

            try
            {
                // Resize texture if needed
                Texture2D processTexture = PrepareInputTexture(texture, targetResolution);
                if (processTexture == null) return false;
                
                // Create input tensor
                Tensor inputTensor = null;
                Tensor outputTensor = null;
                
                try
                {
                    // Convert texture to tensor
                    inputTensor = TextureToTensor(processTexture);
                    
                    // Execute the model
                    if (worker != null && inputTensor != null)
                    {
                        worker.Execute(inputTensor);
                        
                        // Get output tensor
                        outputTensor = worker.PeekOutput() as Tensor;
                        if (outputTensor != null)
                        {
                            // Process the output tensor
                            ProcessOutputTensor(outputTensor);
                            return true;
                        }
                        else
                        {
                            Debug.LogError("Failed to get output tensor");
                            return false;
                        }
                    }
                    else
                    {
                        Debug.LogError("Model worker or input tensor is null");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error during model execution: {e.Message}");
                    return false;
                }
                finally
                {
                    // Clean up resources
                    if (inputTensor != null)
                    {
                        inputTensor.Dispose();
                    }
                    
                    if (outputTensor != null)
                    {
                        outputTensor.Dispose();
                    }
                    
                    // Clean up the processed texture if it's different from the input
                    if (processTexture != null && processTexture != texture)
                    {
                        Destroy(processTexture);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in ProcessCameraFrame: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prepare the input texture for processing
        /// </summary>
        private Texture2D PrepareInputTexture(Texture2D source, Vector2Int targetSize)
        {
            // Use default model input size if target size is zero
            if (targetSize == Vector2Int.zero)
            {
                targetSize = new Vector2Int(inputWidth, inputHeight);
            }
            
            // Resize if needed
            if (source.width != targetSize.x || source.height != targetSize.y)
            {
                try
                {
                    Texture2D resized = new Texture2D(targetSize.x, targetSize.y, TextureFormat.RGBA32, false);
                    Graphics.ConvertTexture(source, resized);
                    return resized;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error resizing texture: {e.Message}");
                    return null;
                }
            }
            
            return source;
        }
        
        /// <summary>
        /// Convert a texture to a Barracuda tensor
        /// </summary>
        private Tensor TextureToTensor(Texture2D texture)
        {
            if (texture == null) return null;
            
            try
            {
                // Create tensor of appropriate shape (NHWC format by default)
                Tensor tensor = new Tensor(1, texture.height, texture.width, 3);
                
                // Get pixel data from texture
                Color32[] pixels = texture.GetPixels32();
                
                // Copy pixels to tensor
                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        int pixelIndex = y * texture.width + x;
                        
                        if (pixelIndex < pixels.Length)
                        {
                            Color32 pixel = pixels[pixelIndex];
                            
                            // Set RGB channels (normalized to 0-1)
                            tensor[0, y, x, 0] = pixel.r / 255.0f;
                            tensor[0, y, x, 1] = pixel.g / 255.0f; 
                            tensor[0, y, x, 2] = pixel.b / 255.0f;
                        }
                    }
                }
                
                return tensor;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error converting texture to tensor: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process the output tensor from the model
        /// </summary>
        private void ProcessOutputTensor(Tensor outputTensor)
        {
            if (outputTensor == null) return;
            
            // Get dimensions
            int height = outputTensor.shape.height;
            int width = outputTensor.shape.width;
            
            // Create result texture if needed
            if (resultTexture == null || resultTexture.width != width || resultTexture.height != height)
            {
                if (resultTexture != null)
                {
                    Destroy(resultTexture);
                }
                
                resultTexture = new Texture2D(width, height, TextureFormat.R8, false);
            }
            
            // Process output tensor - this implementation assumes a simple semantic segmentation output
            // This might need to be adjusted based on your model's actual output format
            Color32[] pixelData = new Color32[width * height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = y * width + x;
                    
                    // For simple segmentation, get the highest probability class
                    int maxClass = 0;
                    float maxValue = 0;
                    
                    // For a typical segmentation model with multiple class outputs
                    // This assumes NHWC format with the last dimension being class probabilities
                    // Adjust based on your model's output format
                    for (int c = 0; c < outputTensor.shape.channels; c++)
                    {
                        float value = outputTensor[0, y, x, c];
                        if (value > maxValue)
                        {
                            maxValue = value;
                            maxClass = c;
                        }
                    }
                    
                    // Set pixel based on class
                    // You might want a more sophisticated color mapping for visualization
                    byte intensity = (byte)(maxClass * 255 / Mathf.Max(1, outputTensor.shape.channels - 1));
                    pixelData[pixelIndex] = new Color32(intensity, intensity, intensity, 255);
                }
            }
            
            // Apply pixel data to texture
            resultTexture.SetPixels32(pixelData);
            resultTexture.Apply();
        }
    }
} 