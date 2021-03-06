using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using GraphAudio;
using SteamAudio;
using UnityEditor;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;

public enum AudioFramework
{
    FMOD, SteamAudio, GraphAudio, ProjectAcoustics
}

public class GameLogic : MonoBehaviour
{
    [Header("Settings")] public AudioFramework _audioFramework; //currently used audio Framework
    public bool _logResults;
    public bool _audioSourceVisible;
    public AudioClip[] _audioClips;

    [Header("References")] public GameObject _graphAudioManager;
    public GameObject _acousticsAudioManager;
    public GameObject _audioPrefab;
    public GameObject _audioAcousticsPrefab;
    public GameObject _guesserPrefab;
    public GameObject _linePrefab;
    public GameObject _playerCamera;
    public Text _audioFrameworkText;
    public Text _sourcePositionText;
    public Text _audioClipText;
    public TextMeshProUGUI _scoreTextTmp;
    public TextMeshProUGUI _timeTextTmp;
    
    private GameObject _currAudioObj; //currently active instance of _audioPrefab or _audioAcousticsPrefab;
    private GameObject _currGuesserObj; //currently active instance of _guesserPrefab
    private GameObject _currLineObj; //currently active instance of _linePrefab
    private Stopwatch _stopwatch;
    private int _idxCurrClip; //index of the currently playing clip from _audioClips. -1 means no clip 
    private int _idxCurrPos; //index of current position in _positionOrder[_idxCurrTest]
    private int _idxCurrTest; //index of the current test variation in _positionOrder
    private int[][] _positionOrder = { //contains the variations for indices of _objPositions for our test run
        new int[] {7, 3, 23, 19, 17, 25, 28, 31, 34, 9}, //10 entries each
        new int[] {13, 11, 6, 4, 26, 24, 16, 20, 33, 36},
        new int[] {33, 30, 25, 5, 10, 15, 17, 19, 21, 22},
        new int[] {22, 20, 1, 14, 16, 29, 27, 5, 8, 35}
    }; 
    private bool _enableGuessing; //if set to false, you cant spawn guess objects with left click
    private bool _guessFinished; //set to true after MakeGuess(), so we only calculate guess once
    private bool _testStarted;
    private static bool _isGameFocused = true;

    public static bool IsGameFocused() => _isGameFocused;

    //Audio Source Object Positions 
    private Vector3[] _objPositions;


    void Awake()
    {
        _idxCurrClip = (_audioClips.Length == 0) ? -1 : 0;
        _idxCurrPos = -1;
        _idxCurrTest = 0;
        _enableGuessing = false;
        _guessFinished = true;
        _stopwatch = new Stopwatch();
        _objPositions = GetSceneSourcePositions(SceneManager.GetActiveScene().name);
    }

    void Start()
    {
        SetAudioFramework(_audioFramework);

        #region UI
        if (_idxCurrClip >= 0)
            _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
        else
            _audioClipText.text = "Audio Clip: no clip";

        
        //Disable Score Text
        _scoreTextTmp.gameObject.SetActive(false);

        #endregion
    }


    // Update is called once per frame
    void Update()
    {
#if UNITY_EDITOR
        // Handle focus changes in the editor. This isn't a problem in standalone game
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _isGameFocused = false;
            return;
        }

        if (!_isGameFocused)
        {
            // If we registered a click, the game is focused again
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                _isGameFocused = true;
            }

            // Always eat the first click, or early return (because we don't have focus)
            return;
        }
#endif

        #region Key Callbacks

        if (!PauseMenu.IsPaused())
        {
            if (Input.GetKeyDown("1"))
                if(_testStarted)
                    NextAudioPosition();
                else
                {
                    SetAudioFramework(_audioFramework);
                    int index = UnityEngine.Random.Range(0, _positionOrder[_idxCurrTest].Length);
                    NewAudioPosition(index);
                }
            if (_enableGuessing && Input.GetMouseButtonDown(0)) // left/primary click. Only spawn new object if guessing is enabled
                SpawnGuesserObject();
            if (!_guessFinished && _currAudioObj != null && _currGuesserObj != null && Input.GetKeyDown(KeyCode.Return)) // Enter key. Can only make guess if we have source and guess positions
                MakeGuess();
        }
        
        #endregion

        //update stopwatch text while player looks for source
        if (_enableGuessing)
            _timeTextTmp.text = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
    }
    
    /// <summary>
    /// Instanciates _audioPrefab at position "index" in _positionOrder and destroys old/current object
    /// </summary>
    private void NewAudioPosition(int index)
    {
        if (index < 0 || index >= _positionOrder[_idxCurrTest].Length)
        {
            Debug.Log("Invalid source position index: " + index);
            return;
        }

        //Destroy and disable old/Current objects
        if (_currAudioObj != null)
            Destroy(_currAudioObj);

        if (_currLineObj != null)
            Destroy(_currLineObj);

        if (_currGuesserObj != null)
            Destroy(_currGuesserObj);

        //temporarily disable character controlls and start countdown and stopwatch via coroutine
        _playerCamera.gameObject.GetComponentInParent<CharacterController>().enabled = false;
        StartCoroutine(CountdownToStart());

        //Set new position
        _idxCurrPos = index;
        
        //set info text
        _sourcePositionText.text = "Source Position: " + (_idxCurrPos + 1);

        //instantiate new audio source gameObject
        _currAudioObj = Instantiate(_audioFramework == AudioFramework.ProjectAcoustics 
            ? _audioAcousticsPrefab : _audioPrefab, _objPositions[_positionOrder[_idxCurrTest][_idxCurrPos]], Quaternion.identity);
        _currAudioObj.GetComponent<MeshRenderer>().enabled = _audioSourceVisible;

        //set audioClip on the new gameObject and activate object
        SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);

        //enable guessing
        _enableGuessing = true;
        _guessFinished = false;
    }

   
    private void NextAudioPosition()
    {
        if (!_guessFinished)
            return;

        if (_idxCurrPos + 1 >= _positionOrder[_idxCurrTest].Length || _idxCurrPos < 0)
        {
            _scoreTextTmp.text = "Test finished";
            _testStarted = false;
            return;
        }

        //Move player to previous audio source as start position
        var startPos = _objPositions[_positionOrder[_idxCurrTest][_idxCurrPos]];
        startPos.y += 0.5f;
        _playerCamera.transform.parent.position = startPos;

        //first 5 positions have Cantina Band and next 5 footsteps
        _idxCurrClip = (_idxCurrPos + 1 < 5) ? 0 : 1;
        _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
        
        //set audio framework to use for this position (this also calls setAudioClipOnObj())
        SetAudioFramework(_audioFramework);
        
        NewAudioPosition(_idxCurrPos + 1);
    }

    private void NextAudioClip()
    {
        if (_audioClips.Length == 0)
            return;

        _idxCurrClip = (_idxCurrClip + 1 >= _audioClips.Length) ? 0 : _idxCurrClip + 1;

        //Change clip in current audio gameObject
        if (_currAudioObj != null)
            SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);

        //UI text
        _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
    }

    private void ToggleVisibilityAudioObj()
    {
        _audioSourceVisible = !_audioSourceVisible;

        if (_audioSourceVisible)
        {
            if (_currAudioObj != null)
                _currAudioObj.GetComponent<MeshRenderer>().enabled = true;
        }
        else
        {
            if (_currAudioObj != null)
                _currAudioObj.GetComponent<MeshRenderer>().enabled = false;
        }
    }


    private void SpawnGuesserObject()
    {
        //Destroy old/Current object
        if (_currGuesserObj != null)
            Destroy(_currGuesserObj);

        //instantiate new guesser gameObject in front of player
        Vector3 spawnPos = _playerCamera.transform.position + _playerCamera.transform.forward * 4.0f; // spawn Distance 5
        _currGuesserObj = Instantiate(_guesserPrefab, spawnPos, _playerCamera.transform.rotation);
    }

    private void MakeGuess()
    {
        _enableGuessing = false;
        _guessFinished = true;

        //stop stopwatch and refresh text
        _stopwatch.Stop();
        var elapsedTime = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
        _timeTextTmp.text = elapsedTime;

        //calculate distance between audio source obj and guess
        float distance = Vector3.Distance(_currAudioObj.transform.position, _currGuesserObj.transform.position);
        
        //log results
        if (_logResults && _testStarted)
            using (StreamWriter sw = File.AppendText(GetLogPath()))
            {
                sw.WriteLine(DateTime.Now.ToShortTimeString() + " -- " + _audioFramework + " POSITION: " + _idxCurrPos +
                             " -- DISTANZ: " + distance.ToString("0.0") + " -- ZEIT: " + elapsedTime);
            }

        //set score and make it show with pulse animation
        _scoreTextTmp.text = "Distanz" + Environment.NewLine + distance.ToString("0.0");
        _scoreTextTmp.gameObject.SetActive(true);
        StartCoroutine(PulseTMP(2));

        //show audio source obj and draw line to guess obj
        _currAudioObj.GetComponent<MeshRenderer>().enabled = true;
        _currLineObj = Instantiate(_linePrefab);
        LineRenderer line = _currLineObj.GetComponent<LineRenderer>();
        line.SetPosition(0, _currAudioObj.transform.position);
        line.SetPosition(1, _currGuesserObj.transform.position);
    }

    private void SetAudioClipOnObj(ref GameObject obj, AudioClip clip)
    {
        if (obj == null)
            return;
        
        if (_audioFramework == AudioFramework.FMOD || _audioFramework == AudioFramework.GraphAudio)
        {
            if (obj.CompareTag("AcousticsBase"))
            {
                Destroy(obj);
                obj = Instantiate(_audioPrefab, _objPositions[_positionOrder[_idxCurrTest][_idxCurrPos]], Quaternion.identity);
                obj.GetComponent<MeshRenderer>().enabled = _audioSourceVisible;
            }
                
            var eventEmitter = obj.GetComponentInChildren<FMODUnity.StudioEventEmitter>();
            eventEmitter.ChangeEvent(GetFmodEventByName(clip.name));
            eventEmitter.Play();
        }
        else if (_audioFramework == AudioFramework.SteamAudio)
        {
            //We need to create a new gameObject instance otherwise Steam Audio will be buggy
            if (obj != null)
                Destroy(obj);
            obj = Instantiate(_audioPrefab, _objPositions[_positionOrder[_idxCurrTest][_idxCurrPos]], Quaternion.identity);
            obj.GetComponent<MeshRenderer>().enabled = _audioSourceVisible;

            //Set audioclip/Event
            var eventEmitter = obj.GetComponentInChildren<FMODUnity.StudioEventEmitter>();
            eventEmitter.ChangeEvent(GetFmodEventByName(clip.name));

            //TODO: Test if still needed after update to steamAudio 4.0
            Destroy(obj.GetComponentInChildren<SteamAudio.SteamAudioSource>()); //Steam Audio doesnt work otherwise
            var steamAudioSource = obj.transform.GetChild(0).gameObject.AddComponent<SteamAudio.SteamAudioSource>();
            steamAudioSource.occlusion = true;
            steamAudioSource.occlusionType = OcclusionType.Volumetric;
            steamAudioSource.occlusionSamples = 32;
            steamAudioSource.transmission = true;
            steamAudioSource.reflections = true;

            eventEmitter.Play();
        }
        else if (_audioFramework == AudioFramework.ProjectAcoustics)
        {
            if (obj.CompareTag("FmodBase"))
            {
                Destroy(obj);
                obj = Instantiate(_audioAcousticsPrefab, _objPositions[_positionOrder[_idxCurrTest][_idxCurrPos]], Quaternion.identity);
                obj.GetComponent<MeshRenderer>().enabled = _audioSourceVisible;
            }
            var source = obj.GetComponentInChildren<AudioSource>();
            source.Stop();
            source.clip = clip;
            source.Play();
        }

        string GetFmodEventByName(string clipName)
        {
            string affix = "";
            if (_audioFramework == AudioFramework.SteamAudio)
                affix = "SteamAudio";
            else if (_audioFramework == AudioFramework.GraphAudio)
                affix = "GraphAudio";

            if (clipName.Contains("Cantina Band"))
                return "event:/CantinaBand" + affix;
            else if (clipName.Contains("We Are"))
                return "event:/WeAre" + affix;
            else if (clipName.Contains("footsteps"))
                return "event:/footsteps" + affix;
            else
                return "event:/CantinaBand" + affix;
        }
    }

    private void SetAudioFramework(AudioFramework audioFramework)
    {
        _audioFramework = audioFramework;

        switch (_audioFramework)
        {
            case AudioFramework.FMOD:
                _acousticsAudioManager.SetActive(false);
                _graphAudioManager.SetActive(false);
                SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);
                _audioFrameworkText.text = "Audio Framework: A";
                break;
            case AudioFramework.SteamAudio:
                _acousticsAudioManager.SetActive(false);
                _graphAudioManager.SetActive(false);
                SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);
                _audioFrameworkText.text = "Audio Framework: B";
                break;
            case AudioFramework.GraphAudio:
                _acousticsAudioManager.SetActive(false);
                _graphAudioManager.SetActive(true);
                SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);
                _audioFrameworkText.text = "Audio Framework: C";
                break;
            case AudioFramework.ProjectAcoustics:
                _acousticsAudioManager.SetActive(true);
                _graphAudioManager.SetActive(false);
                SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);
                _audioFrameworkText.text = "Audio Framework: D";
                break;
            default:
                _acousticsAudioManager.SetActive(false);
                _graphAudioManager.SetActive(false);
                SetAudioClipOnObj(ref _currAudioObj, _audioClips[_idxCurrClip]);
                _audioFrameworkText.text = "Audio Framework: A";
                break;
        }
    }
    
    
    public void StartTest(GameObject button)
    {
        //get dropdown values for framework and test variant
        var dropdownFramework = button.transform.Find("DropdownFramework").GetComponent<TMP_Dropdown>().value;
        var dropdownVariation = button.transform.Find("DropdownVariation").GetComponent<TMP_Dropdown>().value;
        _idxCurrTest = dropdownVariation;
        _idxCurrPos = 0;
        
        //set audio clip to cantina band
        _idxCurrClip = 0;
        _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
        
        //get framework we want to use from dropdown
        var framework = (AudioFramework) Enum.GetValues(typeof(AudioFramework)).GetValue(dropdownFramework);

        //Move player to start position
        _playerCamera.transform.parent.position = new Vector3(0f,1.06f,-6.426f);
        
        //log
        using (StreamWriter sw = File.AppendText(GameLogic.GetLogPath()))
        {
            sw.WriteLine(DateTime.Now.ToShortTimeString() + " -- STARTED TEST VARIATION " + _idxCurrTest + " WITH " + framework + " --");
        }

        //set audio framework to use for this position
        SetAudioFramework(framework);
        NewAudioPosition(0);

        _testStarted = true;
        
        //exit pause menu
        gameObject.GetComponent<PauseMenu>().Resume();
    }

    /// <summary>
    /// Lets the score textMeshPro _scoreTextTmp pulsate via scaling
    /// </summary>
    /// <returns></returns>
    private IEnumerator PulseTMP(int pulses)
    {
        for (int j = 0; j < pulses; j++) //amount of pulses
        {
            for (float i = 1f; i <= 1.2f; i += 0.05f)
            {
                _scoreTextTmp.rectTransform.localScale = new Vector3(i, i, i);
                yield return new WaitForSecondsRealtime(0.05f);
            }

            _scoreTextTmp.rectTransform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

            for (float i = 1.2f; i >= 1f; i -= 0.05f)
            {
                _scoreTextTmp.rectTransform.localScale = new Vector3(i, i, i);
                yield return new WaitForSecondsRealtime(0.05f);
            }

            _scoreTextTmp.rectTransform.localScale = new Vector3(1f, 1f, 1f);
        }
    }

    private IEnumerator CountdownToStart()
    {
        _scoreTextTmp.gameObject.SetActive(true);
        int time = 3;
        while (time > 0)
        {
            _scoreTextTmp.text = "<size=+24>" + time.ToString() + "</size>";
            StartCoroutine(PulseTMP(1));
            yield return new WaitForSeconds(1f);
            time--;
        }

        _scoreTextTmp.text = "<size=+24>Los!</size>";

        //enable character controlls and start stopwatch
        _playerCamera.gameObject.GetComponentInParent<CharacterController>().enabled = true;
        _stopwatch.Restart();

        yield return new WaitForSeconds(1f);
        _scoreTextTmp.gameObject.SetActive(false);
    }

    private Vector3[] GetSceneSourcePositions(string sceneName)
    {
        if(sceneName.Equals("Dust2"))
            return new[]
            {
                new Vector3(41.43f, 26.21f, 47.99f), new Vector3(-36.59f, 19.36f, -0.05f), new Vector3(-34.34f, 19.36f, -30.94f), //T-Spawn, Front long doors, In long doors
                new Vector3(-42.29f, 19.76f, -68.97f), new Vector3(-85.73f, 9.39f, -12.88f), new Vector3(-51.84f, 19.88f, -54.09f), new Vector3(-85.12f, 19.88f, -95.69f), //Long behind blue, Long deep pit, Long out doors, Middle long
                new Vector3(-104.23f, 21.95f, -118.93f), new Vector3(-82.58f, 22.95f, -152.46f), new Vector3(-71.62f, 24.54f, -144.24f), new Vector3(-64.23f, 26.53f, -177.6f), //A Car, A Ramp, A Default, A Goose
                new Vector3(-25.76f, 24.14f, -157.71f), new Vector3(-22.32f, 24.14f, -113.97f), new Vector3(-25.63f, 17.64f, -105.63f), new Vector3(-5.90f, 18.15f, -89.26f), //A Gandalf, A short peek, A short corner, A short
                new Vector3(10.77f, 18.15f, -77.66f), new Vector3(12.43f, 18.15f, -26.95f), new Vector3(41.09f, 18.15f, -23.62f), new Vector3(26.5f, 18.15f, 18.06f), //Catwalk, Top-Mid palme, Top-Mid corner, suicide
                new Vector3(25.79f, 10.65f, -83.87f), new Vector3(64.37f, 12.05f, -81.55f), new Vector3(65.26f, 15.01f, -67.11f), new Vector3(95.51f, 20.18f, -67.11f), //lower mid, lower tunnels, tunnel stairs, upper tunnels
                new Vector3(126.55f, 20.18f, -73.44f), new Vector3(102.68f, 18.87f, -15f), new Vector3(109.80f, 24.36f, 30.73f), new Vector3(93.45f, 26.85f, 14.68f), //upper tunnels deep, front upper tunnel, T ramp, T spawn tunnel peek
                new Vector3(60.13f, 26.85f, 24.73f), new Vector3(126.15f, 27.85f, 53.99f), new Vector3(119.11f, 18.63f, -113.05f), new Vector3(100.70f, 18.63f, -101.15f), //T spawn save, T spawn car, B out tunnel, B car corner
                new Vector3(92.59f, 18.63f, -111.75f), new Vector3(93.31f, 18.63f, -161.11f), new Vector3(122.05f, 19.94f, -177.47f), new Vector3(79.88f, 25.95f, -159.19f), //B car, B site, B platou, B window
                new Vector3(78.87f, 18.21f, -130.16f), new Vector3(53.5f, 13.06f, -139.33f), new Vector3(24.37f, 10.68f, -118.66f), new Vector3(22.43f, 10.68f, -98.65f), //B doors, Mid-to-B ramp, Mid-to-B, Double door
                new Vector3(-15.75f, 10.68f, -132.11f), new Vector3(-25.92f, 10.23f, -147.96f), new Vector3(-36.81f, 18.42f, -141.55f), new Vector3(-59.9f, 17.4f, -130.86f) //CT Spawn, CT Spawn deep, CT short boost, CT Ramp
            };
        if (sceneName.Equals("ProjectAcousticsDemo"))
            return new[]
                { new Vector3(6.1f,1.58f,-20.62f), new Vector3(10.18f,1.58f,-37.17f), new Vector3(16.79f,1.58f,-20.09f), new Vector3(2.79f,1.58f,0.2f), 
                    new Vector3(-12.6f,1.58f,-12.25f), new Vector3(-14.07f,0.74f,-37.03f), new Vector3(-25.527f,2.748f,-65f), new Vector3(-30.52f,-2.14f,-66.42f),
                    new Vector3(-43.63f,1.79f,-94.6f), new Vector3(10.73f,-0.252f,-62.763f), new Vector3(-4.852f,-11.627f,-58.998f), new Vector3(10.261f,-16.608f,-66.23f),
                    new Vector3(3.522f,-7.632f,-70.471f), new Vector3(35.179f,-0.285f,-54.427f), new Vector3(50.48f,-1.214f,-70.51f), new Vector3(64.731f,2.509f,-84.773f),
                    new Vector3(49.387f,1.082f,-15.947f), new Vector3(58.234f,5.401f,-15.947f), new Vector3(64.34f,10.59f,-19.08f), new Vector3(32.791f,-0.326f,8.006f),
                    new Vector3(26.845f,8.9f,3.661f), new Vector3(21.745f,11.305f,18.161f), new Vector3(1.807f,3.396f,29.594f), new Vector3(1.25f,6.152f,24.7f),
                    new Vector3(-0.803f,7.34f,41.145f), new Vector3(-26.863f,1.35f,8.555f), new Vector3(-33.451f,7.42f,17.973f), new Vector3(-35.67f,0.845f,31.522f),
                    new Vector3(-36.96f,7.438f,40.47f), new Vector3(-41.255f,-1.592f,-9.253f), new Vector3(-54.763f,-6.21f,-17.361f), new Vector3(-69.362f,-6.698f,8.647f),
                    new Vector3(-62.308f,-3.421f,-4.185f), new Vector3(-41.81f,1.036f,-38.435f), new Vector3(-53.048f,-2.789f,-45.164f), new Vector3(-51.76f,9.398f,-48.6f),
                    new Vector3(-67.213f,1.91f,-32.59f)};
        return null;
    }

    public static string GetLogPath()
    {
        string path;
        path = Application.isEditor ? Directory.GetParent(Application.dataPath).FullName : Application.dataPath;
        path = Path.Combine(path, "Logs", "EvaluationData");

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);

        path = Path.Combine(path, "results_" + DateTime.Today.ToString("dd_MM") + ".txt");
        return path;
    }
}
