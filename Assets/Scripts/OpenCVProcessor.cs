using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;

// Используем алиасы для разрешения конфликтов
using OpenCVRect = OpenCVForUnity.CoreModule.Rect;
using UnityRect = UnityEngine.Rect;

public static class OpenCVProcessor
{
    public static RenderTexture PostProcessMask(RenderTexture inputMask)
    {
        // Convert RenderTexture to Mat
        Texture2D tempTex = RenderTextureToTexture2D(inputMask);
        Mat sourceMat = new Mat(tempTex.height, tempTex.width, CvType.CV_8UC4);
        Utils.texture2DToMat(tempTex, sourceMat);
        Object.Destroy(tempTex);

        // Convert to grayscale
        Mat grayMat = new Mat();
        Imgproc.cvtColor(sourceMat, grayMat, Imgproc.COLOR_RGBA2GRAY);
        sourceMat.Dispose();

        // Apply threshold to get binary mask
        Mat binaryMat = new Mat();
        Imgproc.threshold(grayMat, binaryMat, 127, 255, Imgproc.THRESH_BINARY);
        grayMat.Dispose();

        // Remove noise with morphological operations
        Mat kernel = Imgproc.getStructuringElement(Imgproc.MORPH_RECT, new Size(3, 3));
        Mat cleanedMat = new Mat();
        
        // Opening operation (erosion followed by dilation) to remove small noise
        Imgproc.morphologyEx(binaryMat, cleanedMat, Imgproc.MORPH_OPEN, kernel);
        binaryMat.Dispose();

        // Closing operation (dilation followed by erosion) to fill small holes
        Imgproc.morphologyEx(cleanedMat, cleanedMat, Imgproc.MORPH_CLOSE, kernel);
        kernel.Dispose();

        // Find contours
        List<MatOfPoint> contours = new List<MatOfPoint>();
        Mat hierarchy = new Mat();
        Imgproc.findContours(cleanedMat.clone(), contours, hierarchy, Imgproc.RETR_EXTERNAL, Imgproc.CHAIN_APPROX_SIMPLE);
        hierarchy.Dispose();

        // Filter contours by area and create mask
        Mat finalMask = Mat.zeros(cleanedMat.size(), CvType.CV_8UC1);
        cleanedMat.Dispose();

        foreach (MatOfPoint contour in contours)
        {
            double area = Imgproc.contourArea(contour);
            if (area > 1000) // Adjust this threshold based on your needs
            {
                // Check if the contour is roughly rectangular (like a wall)
                MatOfPoint2f contour2f = new MatOfPoint2f();
                contour.convertTo(contour2f, CvType.CV_32F);
                
                // Approximate the contour to reduce number of points
                MatOfPoint2f approx = new MatOfPoint2f();
                double epsilon = 0.02 * Imgproc.arcLength(contour2f, true);
                Imgproc.approxPolyDP(contour2f, approx, epsilon, true);

                // Convert back to MatOfPoint for drawing
                MatOfPoint approxContour = new MatOfPoint();
                approx.convertTo(approxContour, CvType.CV_32S);

                // Draw the contour if it passes our criteria
                Imgproc.drawContours(finalMask, new List<MatOfPoint> { approxContour }, 0, new Scalar(255), -1);

                contour2f.Dispose();
                approx.Dispose();
                approxContour.Dispose();
            }
            contour.Dispose();
        }

        // Convert back to RenderTexture
        Texture2D resultTex = new Texture2D(finalMask.cols(), finalMask.rows(), TextureFormat.RGBA32, false);
        Utils.matToTexture2D(finalMask, resultTex);
        finalMask.Dispose();

        RenderTexture result = new RenderTexture(resultTex.width, resultTex.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(resultTex, result);
        Object.Destroy(resultTex);

        return result;
    }

    private static Texture2D RenderTextureToTexture2D(RenderTexture rt)
    {
        RenderTexture currentRT = RenderTexture.active;
        RenderTexture.active = rt;
        
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityRect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        
        RenderTexture.active = currentRT;
        return tex;
    }

    private static bool IsWallLike(MatOfPoint contour)
    {
        OpenCVRect boundingRect = Imgproc.boundingRect(contour);
        float aspectRatio = boundingRect.width / (float)boundingRect.height;
        
        // Wall criteria: either very tall or very wide
        return aspectRatio < 0.3f || aspectRatio > 3.0f;
    }
} 