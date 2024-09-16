// SPDX-License-Identifier: MIT

using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;
using System.Collections;

namespace GaussianSplatting.Editor.Utils
{
    public class CaptureScreenshot : MonoBehaviour
    {
        private static GaussianSplatRenderer m_gs;

        private static int[] m_cameraIndexSequence = new int[] { 0, 7, 15, 23, 31, 39, 47, 52 };

        private static void InitGSRenderer()
        {
            // set the correct gaussian for snapshotting
            if (m_gs == null)
                m_gs = GameObject.Find("GaussianSplats_E").GetComponent<GaussianSplatRenderer>();
        }

        [MenuItem("Tools/Gaussian Splats/Debug/Capture Screenshot %g")]
        public static void CaptureShot()
        {
            //InitGSRenderer();
            string path;
            int counter = 0;
            while(true)
            {
                path = $"image_{counter}.jpg";
                if (!System.IO.File.Exists(path))
                    break;
                ++counter;
            }
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"Captured {path}");
        }
        
    }
}