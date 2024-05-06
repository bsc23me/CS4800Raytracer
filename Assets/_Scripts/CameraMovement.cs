using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMovement : MonoBehaviour
{

	[SerializeField] private bool trackCam;

	[SerializeField] private float sens = 4f;
	[SerializeField] private float speed = 4f;

	[SerializeField] private float radius = 10f;

    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update()
    {
		if (trackCam) // Set if camera is on a set track with fixed speed
		{
			Vector3 center = Vector3.zero;
			
			// Move camera in a circle around the center
			Vector3 target = new Vector3(radius * Mathf.Cos(Time.time),1, radius * Mathf.Sin(Time.time)) + center;
			transform.position = target;
			transform.LookAt(center);
		}
		else // Camera is free to move around the scene as the player dictates
		{
			// Look direction
			Vector2 mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sens;
			transform.eulerAngles += new Vector3(-mouse.y, mouse.x);
			
			// Movement
			Vector3 move = Vector3.forward * Input.GetAxisRaw("Vertical") + Vector3.right * Input.GetAxisRaw("Horizontal");
			float sp = speed;
			sp *= Input.GetKey(KeyCode.LeftShift) ? 3f : 1f;
			transform.Translate(move * sp * Time.deltaTime);
		}
	}
}
