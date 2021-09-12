using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GraphAudio
{
    public class Edge : ScriptableObject
    {
        [Header("DSP-Parameters")]
        [Range(0, 255)]
        public byte _occlusion;//0 means path is not occluded at all
        [Range(0.0f, 1.0f)]
        public float _occlusionFloat;//same as _occlusion only as float 0 to 1
        public float _length;

        [Header("Graph related")]
        public Node _target;
        public Node _origin;

        public static Edge Create(Node target, Node origin)
        {
            Edge edge = CreateInstance<Edge>();
            edge._target = target;
            edge._origin = origin;
            edge.name = "EdgeFrom" + origin.name + "To" + target.name;
            return edge;
        }
    }

    public struct EdgeDOTS
    {
        public float length;//represents the amount of attenuation and is the exaggerated distance between two nodes due to occlusion (Cowan)

        public int FromNodeIndex;
        public int ToNodeIndex;
    }
}
