using System.Collections;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GraphAudio
{
    public class GraphNodeRenderer : MonoBehaviour
    {
        [Header("Gizmo Render Options")]
        public bool renderNodesGizmo = true;
        public bool renderNodeNamesGizmo;
        public bool renderEdgesGizmo;
        public bool refresh;
        public Graph graph;

        [Header("Runtime Render Options")]
        public bool renderClosestNodes;//closest node to listener & sound source positions will be rendered
        public bool renderShortestPathes;

        public static GraphNodeRenderer Instance { get; private set; }//Singleton

        //gizmo stuff
        private List<Tuple<Vector3, string>> _nodeLocations;
        private List<Tuple<Vector3, Vector3>> _edgeLocations;
        private List<byte> _occlusionFactors;//occlusion factor for each entrance in _edgeLocations

        //runtime stuff
        private List<GameObject> Cubes_Source = new List<GameObject>();
        private GameObject Cube_Listener;

        private void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            //runtime stuff. Add cube for listener. Sound sources will be added with a call in GraphAudioSoundSource.cs
            Cube_Listener = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Cube_Listener.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Cube_Listener.GetComponent<BoxCollider>().enabled = false;
            Cube_Listener.GetComponent<MeshRenderer>().material.color = Color.blue;
            Cube_Listener.SetActive(renderClosestNodes);
        }

        void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            if(Selection.activeGameObject != transform.gameObject)
            {
                return;
            }
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
            if(renderNodesGizmo)
            {
                Gizmos.color = Color.blue;
                Vector3 cubeSize = new Vector3(0.7f, 0.7f, 0.7f);
                for(int i = 0; i < _nodeLocations.Count; i++)
                {
                    if(renderNodeNamesGizmo)
                    {
                        Vector3 labelPos = _nodeLocations[i].Item1;
                        labelPos.y += 0.4f;
                        Handles.Label(labelPos, _nodeLocations[i].Item2);
                    }
                    Gizmos.DrawCube(_nodeLocations[i].Item1, cubeSize);
                }
            }
            // draw all the edges. Map occlusion factor to edge color. 255 (1.0f, not occluded) is green and 0 is red
            if(renderEdgesGizmo)
            {
                for(int i = 0; i < _edgeLocations.Count; i++)
                {
                    float occlusion = _occlusionFactors[i] / 255.0f;
                    Color color = new Color(occlusion, 1.0f - occlusion, 0.0f);
                    Gizmos.color = color;
                    Gizmos.DrawLine(_edgeLocations[i].Item1, _edgeLocations[i].Item2);
                }
            }
#endif
        }

        void OnValidate()
        {
            if(Application.isPlaying)
            {
                Cubes_Source.ForEach(x => x.SetActive(renderClosestNodes));
                Cube_Listener?.SetActive(renderClosestNodes);

                graph.CalcOcclusionAllEdges();
            }

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

        public void VisualizeShortestPath(GraphPathfindingDOTS graph)
        {

        }

        public void SetListenerPos(Vector3 pos) => Cube_Listener.transform.position = pos;
        public void SetSourcePositions(List<Vector3> positions)
        {
            for(int i = 0; i < Cubes_Source.Count; i++)
                Cubes_Source[i].transform.position = positions[i];
        }

        public void AddSourceCube()
        {
            var new_cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            new_cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            new_cube.GetComponent<BoxCollider>().enabled = false;
            new_cube.GetComponent<MeshRenderer>().material.color = Color.red;
            new_cube.SetActive(renderClosestNodes);
            Cubes_Source.Add(new_cube);
        }

        public void RemoveSourceCube()
        {
            if(Cubes_Source.Count > 0)
            {
                GameObject cube = Cubes_Source[0];
                Cubes_Source.RemoveAt(0);
                Destroy(cube);
            }
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