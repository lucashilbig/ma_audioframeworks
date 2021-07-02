using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GraphAudio
{
    public class Graph : ScriptableObject
    {
        [SerializeField]
        private List<Node> nodes;
        public List<Node> Nodes
        {
            get
            {
                if(nodes == null)
                {
                    nodes = new List<Node>();
                }

                return nodes;
            }
        }

        public static Graph Create(string name)
        {
            Graph graph = CreateInstance<Graph>();

            string path = string.Format("Assets/GraphAudio/{0}.asset", name);
            AssetDatabase.CreateAsset(graph, path);

            return graph;
        }

        public void AddNode(Node node)
        {
            Nodes.Add(node);
            AssetDatabase.AddObjectToAsset(node, this);
            AssetDatabase.SaveAssets();
        }
    }
}
