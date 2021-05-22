using UnityEngine;
using UnityEngine.UI;

public class GameLogic : MonoBehaviour
{
    [Header("Settings")]
    public bool _audioSourceVisible;
    public AudioClip[] _audioClips;

    [Header("References")]
    public GameObject _audioPrefab;
    public Text _audioClipText;
    public Text _audioVisibleText;

    GameObject _currAudioObj;//currently active instance of _audioPrefab
    int _idxCurrClip;//index of the currently playing clip from _audioClips. -1 means no clip 
    int _idxCurrPos;//index of current position from _objPositions

    //Audio Source Object Positions for Dust2
    Vector3[] _objPositions = {new Vector3(41.43f, 26.21f, 47.99f), new Vector3(-36.59f, 19.36f, -0.05f), new Vector3(-34.34f, 19.36f, -30.94f),//T-Spawn, Front long doors, In long doors
    new Vector3(-42.29f,19.76f,-68.97f), new Vector3(-85.73f,9.39f,-12.88f), new Vector3(-51.84f,19.88f,-54.09f), new Vector3(-85.12f,19.88f,-95.69f),//Long behind blue, Long deep pit, Long out doors, Middle long
    new Vector3(-104.23f,21.95f,-118.93f), new Vector3(-82.58f,22.95f,-152.46f), new Vector3(-71.62f,24.54f,-144.24f), new Vector3(-64.23f,26.53f,-177.6f),//A Car, A Ramp, A Default, A Goose
    new Vector3(-25.76f,24.14f,-157.71f), new Vector3(-22.32f,24.14f,-113.97f), new Vector3(-25.63f,17.64f,-105.63f), new Vector3(-5.90f,18.15f,-89.26f),//A Gandalf, A short peek, A short corner, A short
    new Vector3(10.77f,18.15f,-77.66f), new Vector3(12.43f,18.15f,-26.95f), new Vector3(41.09f,18.15f,-23.62f), new Vector3(26.5f,18.15f,18.06f),//Catwalk, Top-Mid palme, Top-Mid corner, suicide
    new Vector3(25.79f,10.65f,-83.87f), new Vector3(64.37f,12.05f,-81.55f), new Vector3(65.26f,15.01f,-67.11f), new Vector3(95.51f,20.18f,-67.11f),//lower mid, lower tunnels, tunnel stairs, upper tunnels
    new Vector3(126.55f,20.18f,-73.44f), new Vector3(102.68f,18.87f,-15f), new Vector3(109.80f,24.36f,30.73f), new Vector3(93.45f,26.85f,14.68f),//upper tunnels deep, front upper tunnel, T ramp, T spawn tunnel peek
    new Vector3(60.13f,26.85f,24.73f), new Vector3(126.15f,27.85f,53.99f), new Vector3(119.11f,18.63f,-113.05f), new Vector3(100.70f,18.63f,-101.15f),//T spawn save, T spawn car, B out tunnel, B car corner
    new Vector3(92.59f,18.63f,-111.75f), new Vector3(93.31f,18.63f,-161.11f), new Vector3(122.05f,19.94f,-177.47f), new Vector3(79.88f,25.95f,-159.19f),//B car, B site, B platou, B window
    new Vector3(78.87f,18.21f,-130.16f), new Vector3(53.5f,13.06f,-139.33f), new Vector3(24.37f,10.68f,-118.66f), new Vector3(22.43f,10.68f,-98.65f),//B doors, Mid-to-B ramp, Mid-to-B, Double door
    new Vector3(-15.75f,10.68f,-132.11f), new Vector3(-25.92f,10.23f,-147.96f), new Vector3(-36.81f,18.42f,-141.55f), new Vector3(-59.9f,17.4f,-130.86f)};//CT Spawn, CT Spawn deep, CT short boost, CT Ramp

   
    void Awake()
    {
        _idxCurrClip = (_audioClips.Length == 0) ? -1 : 0;
        _idxCurrPos = -1;

        #region UI
        if(_audioSourceVisible)
            _audioVisibleText.text = "Audio Visible: On";
        else
            _audioVisibleText.text = "Audio Visible: Off";

        if(_idxCurrClip >= 0)
            _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
        else
            _audioClipText.text = "Audio Clip: no clip";
        #endregion
    }

    // Update is called once per frame
    void Update()
    {
        #region Key Callbacks
        if(Input.GetKeyDown("1"))
            NewAudioPositionRnd();
        if(Input.GetKeyDown("2"))
            NextAudioClip();
        if(Input.GetKeyDown("3"))
            ToggleVisibility();
        #endregion
    }

    /// <summary>
    /// Instanciates _audioPrefab at new random position and destroys old/current object
    /// </summary>
    private void NewAudioPositionRnd()
    {
        //Destroy old/Current object
        if(_currAudioObj != null)
            Destroy(_currAudioObj);

        //Select random new position that is different from current
        int newIdx = UnityEngine.Random.Range(0, _objPositions.Length);
        while(newIdx == _idxCurrPos)
            newIdx = UnityEngine.Random.Range(0, _objPositions.Length);
        _idxCurrPos = newIdx;

        //instantiate new audio gameObject
        _currAudioObj = Instantiate(_audioPrefab, _objPositions[_idxCurrPos], Quaternion.identity);

        //set audioClip on the new gameObject
        AudioSource source = _currAudioObj.GetComponent<AudioSource>();
        source.clip = _audioClips[_idxCurrClip];
        source.Play();
    }

    private void NextAudioClip()
    {
        if(_audioClips.Length == 0)
            return;

        _idxCurrClip = (_idxCurrClip + 1 >= _audioClips.Length) ? 0 : _idxCurrClip + 1;

        //Change clip in current audio gameObject
        if(_currAudioObj != null)
        {
            AudioSource source = _currAudioObj.GetComponent<AudioSource>();
            source.clip = _audioClips[_idxCurrClip];
            source.Play();
        }

        //UI text
        _audioClipText.text = "Audio Clip: " + _audioClips[_idxCurrClip].name;
    }

    private void ToggleVisibility()
    {
        _audioSourceVisible = (_audioSourceVisible) ? false : true;

        if(_audioSourceVisible)
        {
            _currAudioObj.GetComponent<MeshRenderer>().enabled = true;
            _audioVisibleText.text = "Audio Visible: On";
        }
        else
        {
            _currAudioObj.GetComponent<MeshRenderer>().enabled = false;
            _audioVisibleText.text = "Audio Visible: Off";
        }
    }
}
