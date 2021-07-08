using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;

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

        /// <summary>
        /// Creates all Edges between all neighbouring nodes. Edges have default values of 1 for occlusion (full occluded).
        /// For a node all nodes within a cube of maxNeighbourDistance are considered as neighbours.
        /// Use 5.0f maxNeighbourDistance for project Acoustics converted graph.
        /// </summary>
        public void CreateAllEdges(float maxNeighbourDistance)
        {
            ConcurrentBag<Tuple<Node, List<Node>>> neighbours = new ConcurrentBag<Tuple<Node, List<Node>>>();
            //find neighbours of every node
            Parallel.ForEach(Nodes, node =>
            {
                new ParallelOptions//Use 75% of available cpu resources
                {
                    MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))
                };
                neighbours.Add(new Tuple<Node, List<Node>>(node, FindNeighbours(node, maxNeighbourDistance)));
            });

            //create Edges towards Neighbours for each node
            foreach(var tuple in neighbours)
                foreach(var nd in tuple.Item2)
                    tuple.Item1.AddEdge(nd);
            
        }

        /// <summary>
        /// Finds all Nodes within the cube of maxDistance radius (towards each axis) around this node.
        /// Use 5.0f maxDistance for project Acoustics converted graph.
        /// Works in parallel and uses 75% of available cpu resources.
        /// </summary>
        /// <returns>List of Neighbours</returns>
        private List<Node> FindNeighbours(Node node, float maxDistance)
        {
            ConcurrentBag<Node> neighbours = new ConcurrentBag<Node>();
            Parallel.ForEach(Nodes, nd =>
            {
                new ParallelOptions//Use 75% of available cpu resources
                {
                    MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))
                };

                if(node != nd && Math.Abs(node._location.x - nd._location.x) < maxDistance && Math.Abs(node._location.y - nd._location.y) < maxDistance
                && Math.Abs(node._location.z - nd._location.z) < maxDistance)
                    neighbours.Add(nd);
            });

            return neighbours.ToList();
        }
    }
}
