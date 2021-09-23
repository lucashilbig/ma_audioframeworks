using UnityEngine;

public class MouseLookCam : MonoBehaviour
{
    // PUBLIC MEMBER
    public float mouseSensitivity = 100f;
    public Transform playerBody;

    //PRIVATE MEMBER
    float xRotation = 0f;


    // Start is called before the first frame update
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;        
    }

    // Update is called once per frame
    void Update()
    {
        if(!GameLogic.IsGameFocused())
            return;

        if (PauseMenu.IsPaused())
            return;
        
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.fixedDeltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.fixedDeltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}
