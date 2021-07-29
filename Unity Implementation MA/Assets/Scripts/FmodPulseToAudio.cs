using UnityEngine;
using System.Runtime.InteropServices;
using System;

/// <summary>
/// Script with help of MGKPlayz answer: https://answers.unity.com/questions/221562/make-an-object-pulse-to-a-beat.html
/// </summary>
public class FmodPulseToAudio : MonoBehaviour
{
    [Range(0, 7)]
    public int _band = 4;
    public float _scaleMultiplier = 2.0f, _scalingRate = 10.0f;

    FMODUnity.StudioEventEmitter _audioSource;
    FMOD.DSP _fft;
    static float[] _spectrumAllChannels;
    static float[] mFFTSpectrum;
    static float[] _freqBand = new float[8];
    static Vector3 _baseScale;
    const int WindowSize = 1024;

    // Start is called before the first frame update
    void Start()
    {
        _audioSource = GetComponent<FMODUnity.StudioEventEmitter>();
        _baseScale = transform.localScale;

        //Set up fast fourier transform dsp to get spectrum data later
        if(FMODUnity.RuntimeManager.CoreSystem.createDSPByType(FMOD.DSP_TYPE.FFT, out _fft) == FMOD.RESULT.OK)
        {
            _fft.setParameterInt((int)FMOD.DSP_FFT.WINDOWTYPE, (int)FMOD.DSP_FFT_WINDOW.HANNING);
            _fft.setParameterInt((int)FMOD.DSP_FFT.WINDOWSIZE, WindowSize * 2);
            FMODUnity.RuntimeManager.StudioSystem.flushCommands();

            // Get the master bus (or any other bus for that matter)
            FMOD.Studio.Bus selectedBus = FMODUnity.RuntimeManager.GetBus("bus:/");
            if(selectedBus.hasHandle())
            {
                // Get the channel group
                FMOD.ChannelGroup channelGroup;
                if(selectedBus.getChannelGroup(out channelGroup) == FMOD.RESULT.OK)
                {
                    // Add fft to the channel group
                    if(channelGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, _fft) != FMOD.RESULT.OK)
                    {
                        Debug.LogWarningFormat("FMOD: Unable to add _FFT to the master channel group");
                    }
                }
                else
                {
                    Debug.LogWarningFormat("FMOD: Unable to get Channel Group from the selected bus");
                }
            }
            else
            {
                Debug.LogWarningFormat("FMOD: Unable to get the selected bus");
            }
        }
        else
        {
            Debug.LogWarningFormat("FMOD: Unable to create FMOD.DSP_TYPE.FFT");
        }

    }

    // Update is called once per frame
    void Update()
    {
        if(_audioSource == null)
            _audioSource = GetComponent<FMODUnity.StudioEventEmitter>();
        else
        {
            if(_audioSource.IsPlaying())
            {
                if(_fft.hasHandle())
                {
                    IntPtr unmanagedData;
                    uint length;
                    if(_fft.getParameterData((int)FMOD.DSP_FFT.SPECTRUMDATA, out unmanagedData, out length) == FMOD.RESULT.OK)
                    {
                        FMOD.DSP_PARAMETER_FFT fftData = (FMOD.DSP_PARAMETER_FFT)Marshal.PtrToStructure(unmanagedData, typeof(FMOD.DSP_PARAMETER_FFT));
                        if(fftData.numchannels > 0)
                        {
                            if(mFFTSpectrum == null)
                            {
                                // Allocate the fft spectrum buffer once
                                for(int i = 0; i < fftData.numchannels; ++i)
                                {
                                    mFFTSpectrum = new float[fftData.length];
                                    _spectrumAllChannels = new float[fftData.length];
                                }
                            }
                            Array.Clear(_spectrumAllChannels, 0, _spectrumAllChannels.Length);
                            for(int i = 0; i < fftData.numchannels; i++)
                            {
                                fftData.getSpectrum(i, ref mFFTSpectrum);
                                for(int j = 0; j < mFFTSpectrum.Length; j++)
                                    _spectrumAllChannels[j] += (mFFTSpectrum[j]);
                            }
                        }
                    }
                    if(_spectrumAllChannels != null)
                    {
                        MakeFrequencyBands();

                        //Change Object scale
                        Vector3 scale = new Vector3((_freqBand[_band] * _scaleMultiplier) + _baseScale.x,
                            (_freqBand[_band] * _scaleMultiplier) + _baseScale.y, (_freqBand[_band] * _scaleMultiplier) + _baseScale.z);
                        transform.localScale = Vector3.Lerp(transform.localScale, scale, _scalingRate * Time.deltaTime);
                    }
                }
            }
        }
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
                average += _spectrumAllChannels[count] * (count + 1);
                count++;
            }

            average /= count;

            _freqBand[i] = average * 10;
        }
    }

    void OnDestroy()
    {
        FMOD.Studio.Bus selectedBus = FMODUnity.RuntimeManager.GetBus("bus:/");
        if(selectedBus.hasHandle())
        {
            FMOD.ChannelGroup channelGroup;
            if(selectedBus.getChannelGroup(out channelGroup) == FMOD.RESULT.OK)
            {
                if(_fft.hasHandle())
                {
                    channelGroup.removeDSP(_fft);
                }
            }
        }
    }
}
