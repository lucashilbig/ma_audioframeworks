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
        private List<byte> _occlusionFactors;//occlusion factor for each entrance in _edgeLocations

        void Start()
        {
            if(_nodeLocations == null || _nodeLocations.Count == 0)
            {
                _nodeLocations = new List<Tuple<Vector3, string>>();
                _edgeLocations = new List<Tuple<Vector3, Vector3>>();
                _occlusionFactors = new List<byte>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                {
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                    //Cache edges
                    foreach(Edge edge in node.Neighbors)
                    {
                        Vector3 a = node._location;
                        Vector3 b = edge._target._location;
                        //offset edge a bit because we have 2 edges from each node connection
                        if(Math.Abs(a.z - b.z) < 0.1f)
                        {
                            a.z += 0.2f;
                            b.z += 0.2f;
                        }
                        else
                        {
                            a.x += 0.2f;
                            b.x += 0.2f;
                        }
                        var edgeLocReverse = new Tuple<Vector3, Vector3>(b, a);
                        if(_edgeLocations.Contains(edgeLocReverse))
                        {
                            if(Math.Abs(a.z - b.z) < 0.1f)
                            {
                                a.z -= 0.4f;
                                b.z -= 0.4f;
                            }
                            else
                            {
                                a.x -= 0.4f;
                                b.x -= 0.4f;
                            }
                        }
                        _edgeLocations.Add(new Tuple<Vector3, Vector3>(a, b));
                        _occlusionFactors.Add(edge._occlusion);
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
                _occlusionFactors = new List<byte>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                {
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                    //Cache edges
                    foreach(Edge edge in node.Neighbors)
                    {
                        //TODO: fix kanten verschiebung
                        Vector3 a = node._location;
                        Vector3 b = edge._target._location;
                        //offset edge a bit because we have 2 edges from each node connection
                        if(Math.Abs(a.z - b.z) < 0.1f)
                        {
                            a.z += 0.2f;
                            b.z += 0.2f;
                        }
                        else
                        {
                            a.x += 0.2f;
                            b.x += 0.2f;
                        }
                        var edgeLocReverse = new Tuple<Vector3, Vector3>(b, a);
                        if(_edgeLocations.Contains(edgeLocReverse))
                        {
                            if(Math.Abs(a.z - b.z) < 0.1f)
                            {
                                a.z -= 0.4f;
                                b.z -= 0.4f;
                            }
                            else
                            {
                                a.x -= 0.4f;
                                b.x -= 0.4f;
                            }
                        }
                        _edgeLocations.Add(new Tuple<Vector3, Vector3>(a, b));
                        _occlusionFactors.Add(edge._occlusion);
                    }
                }
            }

            // draw all the nodes
            Gizmos.color = Color.blue;
            Vector3 cubeSize = new Vector3(0.7f, 0.7f, 0.7f);
            for(int i = 0; i < _nodeLocations.Count; i++)
            {
                Vector3 labelPos = _nodeLocations[i].Item1;
                labelPos.y += 0.4f;
                Handles.Label(labelPos, _nodeLocations[i].Item2);
                Gizmos.DrawCube(_nodeLocations[i].Item1, cubeSize);
            }
            // draw all the edges. Map occlusion factor to edge color. 255 (1.0f, not occluded) is green and 0 is red            
            for(int i = 0; i < _edgeLocations.Count; i++)
            {
                float occlusion = _occlusionFactors[i] / 255.0f;
                Color color = new Color(1.0f - occlusion, occlusion, 0.0f);
                Gizmos.color = color;
                Gizmos.DrawLine(_edgeLocations[i].Item1, _edgeLocations[i].Item2);
            }
#endif
        }

        void OnValidate()
        {
            //Re-cache graph nodes
            if(refresh)
            {
                _nodeLocations = new List<Tuple<Vector3, string>>();
                _edgeLocations = new List<Tuple<Vector3, Vector3>>();
                _occlusionFactors = new List<byte>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                {
                    _nodeLocations.Add(new Tuple<Vector3, string>(node._location, node.name));
                    //Cache edges
                    foreach(Edge edge in node.Neighbors)
                    {
                        Vector3 a = node._location;
                        Vector3 b = edge._target._location;
                        //offset edge a bit because we have 2 edges from each node connection
                        if(Math.Abs(a.z - b.z) < 0.1f)
                        {
                            a.z += 0.2f;
                            b.z += 0.2f;
                        }
                        else
                        {
                            a.x += 0.2f;
                            b.x += 0.2f;
                        }
                        var edgeLocReverse = new Tuple<Vector3, Vector3>(b, a);
                        if(_edgeLocations.Contains(edgeLocReverse))
                        {
                            if(Math.Abs(a.z - b.z) < 0.1f)
                            {
                                a.z -= 0.4f;
                                b.z -= 0.4f;
                            }
                            else
                            {
                                a.x -= 0.4f;
                                b.x -= 0.4f;
                            }
                        }
                        _edgeLocations.Add(new Tuple<Vector3, Vector3>(a, b));
                        _occlusionFactors.Add(edge._occlusion);
                    }

                }
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