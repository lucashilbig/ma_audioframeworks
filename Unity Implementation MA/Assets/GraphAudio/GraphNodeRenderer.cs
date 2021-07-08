using System.Collections;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GraphAudio
{
    public class GraphNodeRenderer : MonoBehaviour
    {
        public bool refresh;
        public Graph graph;

        private List<Tuple<Vector3, string>> _nodeLocations;
        private List<Tuple<Vector3, Vector3>> _edgeLocations;

        void Start()
        {
            if(_nodeLocations == null || _nodeLocations.Count == 0)
            {
                _nodeLocations = new List<Tuple<Vector3, string>>();
                _edgeLocations = new List<Tuple<Vector3, Vector3>>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                {
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                    //Cache edges
                    foreach(Edge edge in node.Neighbors)
                    {
                        var edgeLoc = new Tuple<Vector3, Vector3>(node._location, edge._target._location);
                        var edgeLocReverse = new Tuple<Vector3, Vector3>(edge._target._location, node._location);
                        if(!_edgeLocations.Contains(edgeLoc) && !_edgeLocations.Contains(edgeLocReverse))
                            _edgeLocations.Add(edgeLoc);
                    }
                }
            }
        }

        void Update()
        {

        }

        void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            //cache locations from graph nodes once, since Start() doesnt run while not playing
            if(_nodeLocations == null || _nodeLocations.Count == 0)
            {
                _nodeLocations = new List<Tuple<Vector3, string>>();
                _edgeLocations = new List<Tuple<Vector3, Vector3>>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                {
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                    //Cache edges
                    foreach(Edge edge in node.Neighbors)
                    {
                        var edgeLoc = new Tuple<Vector3, Vector3>(node._location, edge._target._location);
                        var edgeLocReverse = new Tuple<Vector3, Vector3>(edge._target._location, node._location);
                        if(!_edgeLocations.Contains(edgeLoc) && !_edgeLocations.Contains(edgeLocReverse))
                            _edgeLocations.Add(edgeLoc);
                    }
                }
            }

            // draw all the nodes
            Gizmos.color = Color.blue;
            Vector3 cubeSize = new Vector3(0.4f, 0.4f, 0.4f);
            for(int i = 0; i < _nodeLocations.Count; i++)
            {
                Vector3 labelPos = _nodeLocations[i].Item1;
                labelPos.y += 0.4f;
                Handles.Label(labelPos, _nodeLocations[i].Item2);
                Gizmos.DrawCube(_nodeLocations[i].Item1, cubeSize);
            }
            // draw all the edges
            Gizmos.color = Color.green;
            for(int i = 0; i < _edgeLocations.Count; i++)
                Gizmos.DrawLine(_edgeLocations[i].Item1, _edgeLocations[i].Item2);            
#endif
        }

        void OnValidate()
        {
            //Re-cache graph nodes
            if(refresh)
            {
                _nodeLocations = new List<Tuple<Vector3, string>>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                refresh = false;
            }
        }


        [MenuItem("Window/Graph Audio/Increse Nodes Y by 1")]
        public static void IncreaseY()
        {
            Graph graph = (Graph)AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphDust2Acoustics.asset", typeof(Graph));

            foreach(Node node in graph.Nodes)
                node._location.y += 1.0f;

            AssetDatabase.SaveAssets();
        }
    }

    public static class AcousticsProbesToGraph
    {

        public static void ConvertToGraph(string graphName, List<Vector3> probeLocations)
        {
            // Create graph
            Graph graph = Graph.Create(graphName);

            //Create Node for every Acoustics probe location
            for(int i = 0; i < probeLocations.Count; i++)
            {
                Node node = Node.Create("Node" + i);
                node._location = probeLocations[i];
                graph.AddNode(node);
            }
        }
    }
}