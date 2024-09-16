using System.Collections.Generic;
using UnityEngine;
using Quaternion = UnityEngine.Quaternion;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

namespace ObjectAligner
{
    public class ObjectAligner : MonoBehaviour
    {
        [Header("Reference Gaussian Splat Scene:")]
        public Transform gaussianSplatA; // Reference object

        [Header("Gaussian Splat Scenes to Align:")]
        public List<Transform> gaussianSplatsToAlign; // List of objects to align

        public bool applyScaleAlignment;

        private Transform FindPlane(Transform parent)
        {
            foreach (Transform child in parent)
            {
                if (child.name.StartsWith("PLANE", System.StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
            return null;
        }

        public Vector3 ComputeDifference(Vector3 objB, Vector3 objA)
        {
            return objB - objA;
        }

        public void ApplyTransformation()
        {
            Transform alignedPlaneA = FindPlane(gaussianSplatA);
            if (alignedPlaneA == null)
            {
                Debug.LogError("No plane found for reference object. Ensure there's a child object with a name starting with 'PLANE'.");
                return;
            }

            foreach (Transform gaussianSplatB in gaussianSplatsToAlign)
            {
                Transform alignedPlaneB = FindPlane(gaussianSplatB);
                if (alignedPlaneB == null)
                {
                    Debug.LogWarning($"No plane found for {gaussianSplatB.name}. Skipping this object.");
                    continue;
                }

                GameObject pivotB = CreatePivot(gaussianSplatB, alignedPlaneB);

                // Align position
                pivotB.transform.position += alignedPlaneA.position - alignedPlaneB.position;

                // Align rotation
                pivotB.transform.rotation = alignedPlaneA.rotation * Quaternion.Inverse(alignedPlaneB.rotation) * pivotB.transform.rotation;

                // Scaling if needed
                if (applyScaleAlignment)
                {
                    pivotB.transform.localScale *= ApplyScaling(alignedPlaneA.localScale, alignedPlaneB.localScale);
                }
            }
        }

        private GameObject CreatePivot(Transform gaussianSplat, Transform alignedPlane)
        {
            GameObject pivot = new GameObject($"PIVOT_{gaussianSplat.name}");

            pivot.transform.position = gaussianSplat.position;
            pivot.transform.rotation = gaussianSplat.rotation;
            pivot.transform.localScale = gaussianSplat.localScale;

            gaussianSplat.SetParent(pivot.transform);

            return pivot;
        }

        private float ApplyScaling(Vector3 scaleA, Vector3 scaleB)
        {
            Vector2 scaleA2D = new Vector2(scaleA.x, scaleA.z);
            Vector2 scaleB2D = new Vector2(scaleB.x, scaleB.z);

            return (Vector2.Dot(scaleB2D, scaleA2D)) / (Vector2.Dot(scaleB2D, scaleB2D));
        }
    }
}