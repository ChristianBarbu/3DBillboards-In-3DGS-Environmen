using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// This class is attached to the object, where the Gizmos should be displayed.
/// </summary>
public class ShowGizmos : MonoBehaviour
{
    [SerializeField] private bool enableGizmos;
    [SerializeField] private Color gizmosColor;
    
    private void OnDrawGizmos()
    {
        if (!enableGizmos) return;
        Gizmos.DrawIcon(transform.position, "video-camera-gizmo", true);
    }
}
