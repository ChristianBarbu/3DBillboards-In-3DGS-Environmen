using System;
using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEngine;

namespace GaussianSplatting.Editor
{
    [CustomEditor(typeof(GaussianSplatInterpolator))]
    public class GaussianSplatInterpolatorEditor : UnityEditor.Editor
    {
        SerializedProperty m_GaussianSplatRendererA;
        SerializedProperty m_GaussianSplatRendererB;
        SerializedProperty m_GaussianSplatRendererInterpolator;
        SerializedProperty m_Value;

        private void OnEnable()
        {
            m_GaussianSplatRendererA = serializedObject.FindProperty("m_GaussianSplatRendererA");
            m_GaussianSplatRendererB = serializedObject.FindProperty("m_GaussianSplatRendererB");
            m_GaussianSplatRendererInterpolator = serializedObject.FindProperty("m_GaussianSplatRendererInterpolator");
            m_Value = serializedObject.FindProperty("m_Value");
        }

        public override void OnInspectorGUI()
        {
            var gsInterpolator = target as GaussianSplatInterpolator;

            serializedObject.Update();
            
            //EditorGUILayout.PropertyField(m_gaussianSplatRenderers);
            EditorGUILayout.PropertyField(m_GaussianSplatRendererA);
            EditorGUILayout.PropertyField(m_GaussianSplatRendererB);

            serializedObject.ApplyModifiedProperties();
            
        }
    }
}
