using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ObjectAligner.ObjectAligner))]
public class ObjectAlignmentEditor : Editor
{
    private ObjectAligner.ObjectAligner target;

    void OnEnable()
    {
        target = (ObjectAligner.ObjectAligner)base.target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        if (GUILayout.Button("Align"))
        {
            if (!target.gaussianSplatA || target.gaussianSplatsToAlign.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "Reference object and at least one object to align must be assigned.", "OK");
            }
            else
            {
                target.ApplyTransformation();
            }
        }
    }
}