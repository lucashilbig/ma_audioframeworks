using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GraphAudio
{
    public class GraphNodeRenderer : MonoBehaviour
    {
        public Graph graph;

        private List<Vector3> _nodeLocations;

        void Start()
        {
            if(_nodeLocations == null || _nodeLocations.Count == 0)
            {
                _nodeLocations = new List<Vector3>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                    _nodeLocations.Add(node._location);
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
                _nodeLocations = new List<Vector3>();
                //Cache locations from graph nodes
                foreach(Node node in graph.Nodes)
                    _nodeLocations.Add(node._location);
            }

            // Now draw all the probes
            Gizmos.color = Color.blue;
            Vector3 cubeSize = new Vector3(0.4f, 0.4f, 0.4f);
            for(int i = 0; i < _nodeLocations.Count; i++)
            {
                Gizmos.DrawCube(_nodeLocations[i], cubeSize);
            }
#endif
        }

        [MenuItem("Window/Graph Audio Example/Increse Nodes Y by 1")]
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