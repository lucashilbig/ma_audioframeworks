using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GraphAudio
{
    public class Node : ScriptableObject
    {
        [Header("DSP-Parameters")]
        public float _transmission;
        public float _wetness;
        public float _decayTime;
        public float _outdoorness;
        public float _totalAttenuation; //total attenuation of current path to listener. Occlusion value and path length of last connection are factored in here

        public float _soundEnergy; // amount of sound energy able to reach this location from the listener
        public Vector3 _direction; // direction from which the sound energy comes (direction towards listener)

        [Header("Graph related")]
        public Vector3 _location;
        [SerializeField]
        private List<Edge> _neighbors;
        public List<Edge> Neighbors // Edges to neighbor nodes. Edge contains target node
        {
            get
            {
                if(_neighbors == null)
                {
                    _neighbors = new List<Edge>();
                }

                return _neighbors;
            }
        }

        public static Node Create(string name)
        {
            Node node = CreateInstance<Node>();
            node.name = name;
            return node;
        }

        public void AddEdge(Node target)
        {
            //TODO: Dont save every edge asset separat. Takes ages
            Edge edge = Edge.Create(target, this.name);
            edge._occlusion = byte.MaxValue;//full occluded: 255
            edge._length = Vector3.Distance(_location, target._location);
            Neighbors.Add(edge);
            AssetDatabase.AddObjectToAsset(edge, this);
            AssetDatabase.SaveAssets();
        }
    }
}
