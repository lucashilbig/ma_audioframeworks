using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GraphAudio {

    [RequireComponent(typeof(FMODUnity.StudioEventEmitter))]
    public class GraphAudioSoundSource : MonoBehaviour
    {
        void OnEnable()
        {
            GraphAudioManager.Instance.AddSoundSource(gameObject.GetComponent<FMODUnity.StudioEventEmitter>());
            GraphNodeRenderer.Instance.AddSourceCube();
        }

        void OnDisable()
        {
            GraphAudioManager.Instance.RemoveSoundSource(gameObject.GetComponent<FMODUnity.StudioEventEmitter>());
            GraphNodeRenderer.Instance.RemoveSourceCube();
        }
    }
}
