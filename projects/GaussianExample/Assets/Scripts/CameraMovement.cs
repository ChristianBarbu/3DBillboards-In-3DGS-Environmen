using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraMovement : MonoBehaviour
{
    private CameraController controller;
    private Vector2 moveXY; // done by the left joystick
    private Vector2 rotate; // done by the right joystick
    private float moveZ; // done by the arrows
    
    private void Awake()
    {
        controller = new CameraController();

        controller.Control.MoveInXYAxis.performed += ctx => moveXY = ctx.ReadValue<Vector2>();
        controller.Control.MoveInXYAxis.canceled += ctx => moveXY = Vector2.zero;

        controller.Control.MoveInZAxis.performed += ctx => moveZ = ctx.ReadValue<float>();
        controller.Control.MoveInZAxis.canceled += ctx => moveZ = ctx.ReadValue<float>();
        
        controller.Control.Rotate.performed += ctx => rotate = ctx.ReadValue<Vector2>();
        controller.Control.Rotate.canceled += ctx => rotate = Vector2.zero;
    }

    private void Update()
    {
        // movement:
        // in x,z axis
        Vector2 m = new Vector2(moveXY.x, moveXY.y) * Time.deltaTime;
        float mZ = moveZ * Time.deltaTime;
        transform.Translate(m.x, m.y, mZ, Space.World);
        
        // does not work as expected...
        //Vector2 r = new Vector2(-rotate.x,-rotate.y) * 50f * Time.deltaTime;
        //transform.Rotate(r.x,r.y,0,Space.Self);
    }

    private void OnEnable()
    {
        controller.Control.Enable();
    }

    private void OnDisable()
    {
        controller.Control.Disable();
    }
}
