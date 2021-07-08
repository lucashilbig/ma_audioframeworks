using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GraphAudio
{
    public class Edge : ScriptableObject
    {
        [Header("DSP-Parameters")]
        [Range(0, 255)]
        public byte _occlusion;
        public float _length;

        [Header("Graph related")]
        public Node _target;

        public static Edge Create(Node target, string sourceNodeName)
        {
            Edge edge = CreateInstance<Edge>();
            edge._target = target;
            edge.name = "EdgeFrom" + sourceNodeName + "To" + target.name;
            return edge;
        }
    }
}
