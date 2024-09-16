using UnityEngine;

namespace DepthBasedTransparentRenderer
{
    public class DepthBasedTransparentRenderer : MonoBehaviour
    {
        public Camera targetCamera;
        public Material targetMaterial;
        public int textureWidth = 1920;
        public int textureHeight = 1080;

        private RenderTexture colorRT;
        private RenderTexture depthRT;

        private static class Props
        {
            public static readonly int DepthTex = Shader.PropertyToID("_DepthTex");
            public static readonly int FarPlane = Shader.PropertyToID("_FarPlane");
        }
    
        private void Start()
        {
            if (targetCamera == null || targetMaterial == null)
            {
                Debug.LogError("Please assign target camera and material in the inspector.");
                return;
            }   
        
            // Create color and depth render textures
            colorRT = new RenderTexture(textureWidth, textureHeight, 24);
            depthRT = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.Depth);
        
            // Set up the camera to render to both textures
            targetCamera.SetTargetBuffers(colorRT.colorBuffer, depthRT.depthBuffer);

            // Assign textures to the material
            targetMaterial.mainTexture = colorRT;
            targetMaterial.SetTexture(Props.DepthTex, depthRT);
        
            // Set the far plane value in the shader
            targetMaterial.SetFloat(Props.FarPlane, targetCamera.farClipPlane);

        }

        private void OnDisable()
        {
            if(colorRT != null)
                colorRT.Release();
            if (depthRT != null)
                depthRT.Release();
        }
    }
}
