using UnityEditor;
using UnityEngine;

namespace CameraInversion
{
    [CustomEditor(typeof(CameraInversion))]
    public class CameraInversionEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CameraInversion cameraInversion = (CameraInversion)target;

            if (GUILayout.Button("Apply Inversion"))
            {
                cameraInversion.ApplyInversion();
            }
        }
    }
}