using UnityEngine;

/// <summary>
/// Script with help of MGKPlayz answer: https://answers.unity.com/questions/221562/make-an-object-pulse-to-a-beat.html
/// </summary>
[RequireComponent(typeof (AudioSource))]
public class PulseToAudio : MonoBehaviour
{
    [Range(0, 7)]
    public int _band;
    public float _scaleMultiplier, _scalingRate;
    public bool _useParentTransform;

    AudioSource _audioSource;
    static float[] _samples = new float[512];
    static float[] _freqBand = new float[8];
    static Vector3 _baseScale;

    // Start is called before the first frame update
    void Start()
    {
        _audioSource = GetComponent<AudioSource>();
        _baseScale = _useParentTransform ? transform.parent.localScale : transform.localScale;
    }

    // Update is called once per frame
    void Update()
    {
        //Get frequencies
        _audioSource.GetSpectrumData(_samples, 0, FFTWindow.Blackman);
        MakeFrequencyBands();

        //Change Object scale
        Vector3 scale = new Vector3((_freqBand[_band] * _scaleMultiplier) + _baseScale.x, 
            (_freqBand[_band] * _scaleMultiplier) + _baseScale.y, (_freqBand[_band] * _scaleMultiplier) + _baseScale.z);
        if(_useParentTransform)
            transform.parent.localScale = Vector3.Lerp(transform.parent.localScale, scale, _scalingRate * Time.deltaTime);
        else
            transform.localScale = Vector3.Lerp(transform.localScale, scale, _scalingRate * Time.deltaTime);
    }

    void MakeFrequencyBands()
    {
        int count = 0;

        for(int i = 0; i < 8; i++)
        {
            float average = 0;
            int sampleCount = (int)Mathf.Pow(2, i) * 2;

            if(i == 7)
            {
                sampleCount += 2;
            }
            for(int j = 0; j < sampleCount; j++)
            {
                average += _samples[count] * (count + 1);
                count++;
            }

            average /= count;

            _freqBand[i] = average * 10;
        }
    }
}
