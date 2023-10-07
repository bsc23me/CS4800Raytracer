using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMovement : MonoBehaviour
{

	[SerializeField] private float sens = 4f;
	[SerializeField] private float speed = 4f;

    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    // Update is called once per frame
    void Update()
    {
		Vector2 mouse = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sens;
		transform.eulerAngles += new Vector3(-mouse.y,mouse.x);
		Vector3 move = Vector3.forward * Input.GetAxisRaw("Vertical") + Vector3.right * Input.GetAxisRaw("Horizontal");
		float sp = speed;
		sp *= Input.GetKey(KeyCode.LeftShift) ? 3f : 1f;
		transform.Translate(move * sp * Time.deltaTime);
	}
}
