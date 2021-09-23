using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.IO.LowLevel.Unsafe;
using UnityEngine;

public class PauseMenu : MonoBehaviour
{
    public GameObject _menu;
    
    private KeyCode _menuKey = KeyCode.Escape;
    private static bool _isPaused;
    public static bool IsPaused() => _isPaused;


    // Start is called before the first frame update
    void Start()
    {
        _menu.SetActive(false);
        _isPaused = false;
        #if UNITY_EDITOR
        _menuKey = KeyCode.M;
        #endif
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(_menuKey))
        {
            if (!_isPaused)
                Pause();
            else
                Resume();
        }
    }

    private void Pause()
    {
        _menu.SetActive(true);
        Time.timeScale = 0f;
        _isPaused = true;
        Cursor.lockState = CursorLockMode.Confined;
    }
    
    private void Resume()
    {
        _menu.SetActive(false);
        Time.timeScale = 1f;
        _isPaused = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void Quit()
    {
        using (StreamWriter sw = File.AppendText(GameLogic.GetLogPath()))
        {
            sw.WriteLine(DateTime.Now.ToShortTimeString() + " ---- APPLICATION QUIT ----");
        }
        Application.Quit();
    }
}
