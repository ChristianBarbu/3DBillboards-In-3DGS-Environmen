using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    /// <summary>
    /// We interpolate between N GaussianSplatRenderer objects.
    /// The interpolation should be done on a separate GaussianSplatRenderer, to avoid major changes
    /// in the initial GaussianSplatRenderer class.
    ///
    /// This class holds as big buffer for all other buffers, so from here we set the "InterpolationBuffer".
    /// </summary>
    [ExecuteInEditMode]
    public class GaussianSplatInterpolator : MonoBehaviour
    {
        public GaussianSplatRenderer m_GaussianSplatRendererA;
        public GaussianSplatRenderer m_GaussianSplatRendererB;
        public GaussianSplatRenderer m_GaussianSplatRendererInterpolate; // values are changed here
        [Range(0f, 1f)] public float m_Value;
        
        internal GaussianSplatRenderer.SplatViewData[] m_GpuViewA;
        internal GaussianSplatRenderer.SplatViewData[] m_GpuViewB;
        
        private void OnEnable()
        {
            var gsA = m_GaussianSplatRendererA;
            var gsB = m_GaussianSplatRendererB;
            
            m_GpuViewA = gsA.GetSplatViewData();
            m_GpuViewB = gsB.GetSplatViewData();
            
            Debug.Log($"m_GpuViewA.Length = {m_GpuViewA.Length}");
            Debug.Log($"m_GpuViewA[0].color = {GaussianSplatRenderer.UnpackColor(m_GpuViewA[0].color)}");
        }

        /*
        private void Interpolate()
        {
            GaussianSplatRenderer.SplatViewData[] splatViewDataA = m_gaussianSplatRenderers[0].GetSplatViewData();
            GaussianSplatRenderer.SplatViewData[] splatViewDataB = m_gaussianSplatRenderers[1].GetSplatViewData();
            
            GaussianSplatRenderer gs = GetComponent<GaussianSplatRenderer>();
            GraphicsBuffer gb = gs.m_GpuView;
            
            for (int i = 0; i < splatViewDataA.Length; i++)
            {
                Vector4 colorA = GaussianSplatRenderer.UnpackColor(splatViewDataA[i].color);
                Vector4 colorB = GaussianSplatRenderer.UnpackColor(splatViewDataB[i].color);

                Vector2 axis1A = splatViewDataA[i].axis1;
                Vector2 axis2A = splatViewDataA[i].axis2;

                Vector2 axis1B = splatViewDataB[i].axis1;
                Vector2 axis2B = splatViewDataB[i].axis2;
                
                Vector4 resColor = Vector4.Lerp(colorA, colorB, interpolateValue);
                Vector2 resAxis1 = Vector2.Lerp(axis1A, axis1B, interpolateValue);
                Vector2 resAxis2 = Vector2.Lerp(axis2A, axis2B, interpolateValue);
                
            }  
        }
        */
        
    }
}
