using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;

namespace GraphAudio
{
    public class Node : ScriptableObject
    {
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

        #if UNITY_EDITOR
        public Edge AddEdge(Node target)
        {
            Edge edge = Edge.Create(target, this);
            edge._occlusion = byte.MaxValue;//full occluded: 255
            edge._occlusionFloat = 1.0f;
            edge._length = Vector3.Distance(_location, target._location);
            Neighbors.Add(edge);
            AssetDatabase.AddObjectToAsset(edge, this);
            return edge;
        }
        #endif
        
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            
            if(!(obj is Node))
                return false;

            Node strct = (Node) obj;
            return this._location.Equals(strct._location);
        }

        public override int GetHashCode()
        {
            return _location.GetHashCode();
        }
    }

    public struct NodeDOTS
    {
        public float3 position;
        public float3 direction;
        public float totalAttenuation;//dijkstra path length from startNode to this node. Occlusion value and path length of last connection are factored in here
        
        public int index;//Index of this node in GraphPathfindingDOTS.Nodes-Array
        public int predecessorIdx;//index of the predecessor node used by dijkstra-algorithm

        public override bool Equals(object obj)
        {
            if(!(obj is NodeDOTS))
                return false;

            NodeDOTS strct = (NodeDOTS) obj;
            return this.position.Equals(strct.position);
        }

        public override int GetHashCode()
        {
            return position.GetHashCode();
        }
    }

}
