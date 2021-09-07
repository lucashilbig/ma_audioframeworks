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
        public bool renderShortestPaths;

        public static GraphNodeRenderer Instance { get; private set; }//Singleton

        //gizmo stuff
        private List<Tuple<Vector3, string>> _nodeLocations;
        private List<Tuple<Vector3, Vector3>> _edgeLocations;
        private List<byte> _occlusionFactors;//occlusion factor for each entrance in _edgeLocations

        //runtime stuff
        private readonly List<GameObject> _pathLines = new List<GameObject>();
        private readonly List<GameObject> _cubesSource = new List<GameObject>();
        private GameObject _cubeListener;

        private void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            //runtime stuff. Add cube for listener. Sound sources will be added with a call in GraphAudioSoundSource.cs
            _cubeListener = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeListener.transform.SetParent(transform);
            _cubeListener.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            _cubeListener.GetComponent<BoxCollider>().enabled = false;
            _cubeListener.GetComponent<MeshRenderer>().material.color = Color.blue;
            _cubeListener.SetActive(renderClosestNodes);
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
                _cubesSource.ForEach(x => x.SetActive(renderClosestNodes));
                _pathLines.ForEach(x => x.SetActive(renderShortestPaths));
                _cubeListener?.SetActive(renderClosestNodes);
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

        public void VisualizeShortestPath(List<List<Vector3>> pathPositions, Vector3 listenerPos)
        {
            if(pathPositions.Count != _pathLines.Count)
                return;

            List<Mesh> meshes = new List<Mesh>();

            for(int i = 0; i < pathPositions.Count; i++)
            {
                //add listener position as last vertex since we go from source to listener
                pathPositions[i].Add(listenerPos);
                
                //create index list
                List<int> indices = new List<int>();
                for (int j = 0; j < pathPositions[i].Count; j++)
                    indices.Add(j);
                
                Mesh mesh = new Mesh
                {
                    name = "LineMesh" + i,
                    vertices = pathPositions[i].ToArray()
                };
                mesh.SetIndices(indices.ToArray(), MeshTopology.LineStrip, 0);
                meshes.Add(mesh);
            }


            //set lineRenderer GameObjects
            for(int i = 0; i < _pathLines.Count; i++)
                _pathLines[i].GetComponent<MeshFilter>().mesh = meshes[i];
        }

        public void SetListenerPos(Vector3 pos) => _cubeListener.transform.position = pos;
        public void SetSourcePositions(List<Vector3> positions)
        {
            for(int i = 0; i < _cubesSource.Count; i++)
                _cubesSource[i].transform.position = positions[i];
        }

        public void AddSourceCube()
        {
            var newCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            newCube.transform.SetParent(transform);
            newCube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            newCube.GetComponent<BoxCollider>().enabled = false;
            newCube.GetComponent<MeshRenderer>().material.color = Color.red;
            newCube.SetActive(renderClosestNodes);
            _cubesSource.Add(newCube);

            //also add a lineRenderer for this source
            GameObject lineObj = new GameObject("Line" + _pathLines.Count);
            lineObj.transform.SetParent(transform);
            lineObj.AddComponent<MeshFilter>();
            MeshRenderer renderer = lineObj.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            lineObj.SetActive(renderShortestPaths);
            _pathLines.Add(lineObj);
        }

        public void RemoveSourceCube()
        {
            if(_cubesSource.Count > 0)
            {
                GameObject cube = _cubesSource[0];
                _cubesSource.RemoveAt(0);
                Destroy(cube);

                //also remove a lineRendererObj
                GameObject renderer = _pathLines[0];
                _pathLines.RemoveAt(0);
                Destroy(renderer);
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