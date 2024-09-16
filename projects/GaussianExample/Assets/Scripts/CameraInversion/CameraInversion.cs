using UnityEngine;

namespace CameraInversion
{
    /// <summary>
    /// For some reason the loaded models got inverted around their y axis in game view, compared to the scene view.
    /// This was a temporary solution, but this locks the FOV and other properties of the camera after executing the function.
    /// Thus (random working solution...), we check the Camera's "Physical Camera" property and set the Rendering path to "Deferred" - that seems to fix it.
    ///
    /// (This script got obsolete after applying the solution mentioned above.)
    /// 
    /// </summary>
    public class CameraInversion : MonoBehaviour
    {
        [SerializeField] private bool invertX;
        [SerializeField] private bool invertY;
        [SerializeField] private bool invertZ;
        
        public void ApplyInversion()
        {
            var cam = GetComponent<Camera>();
            
            Matrix4x4 mat = cam.projectionMatrix;
            mat *= Matrix4x4.Scale(new Vector3(BoolToSign(invertX), BoolToSign(invertY), BoolToSign(invertZ)));
            cam.projectionMatrix = mat;
        }

        /// <summary>
        /// Returns 1 if the input is true and -1 if false. 
        /// </summary>
        private int BoolToSign(bool input)
        {
            return input ? -1 : 1;
        }
    }
}