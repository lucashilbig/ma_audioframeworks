using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GraphAudio {

    [RequireComponent(typeof(FMODUnity.StudioEventEmitter))]
    public class GraphAudioSoundSource : MonoBehaviour
    {
        void OnEnable()
        {
            GraphAudioManager.Instance.AddSoundSource(gameObject.GetComponent<FMODUnity.StudioEventEmitter>(), true);
        }

        void OnDisable()
        {
            GraphAudioManager.Instance.AddSoundSource(gameObject.GetComponent<FMODUnity.StudioEventEmitter>(), false);
        }
    }
}
