using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GraphAudio
{
    public static class ExampleGraphUsage
    {
        [MenuItem("Window/Graph Audio/Create example Graph")]
        public static void CreateGraph()
        {
            // Create graph
            Graph graph = Graph.Create("NewGraphAudio");

            // Create nodes
            Node nodeA = Node.Create("NodeA");
            Node nodeB = Node.Create("NodeB");
            Node nodeC = Node.Create("NodeC");

            // Add nodes to graph.
            graph.AddNode(nodeA);
            graph.AddNode(nodeB);
            graph.AddNode(nodeC);

            // Add Edges after node Assets are saved. since we have undirected graph we add Edges on both nodes
            nodeA.AddEdge(nodeB); //Edge from Node A to Node B
            nodeB.AddEdge(nodeA); //Edge from Node B to Node A
            nodeA.AddEdge(nodeC); //Edge from Node A to Node C
            nodeC.AddEdge(nodeA); //Edge from Node C to Node A

        }

        [MenuItem("Window/Graph Audio/SaveFMOD")]
        public static void SaveFMOD()
        {
            AssetDatabase.ForceReserializeAssets(new List<string>() { "Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset" });
            AssetDatabase.SaveAssets();
        }
    }
}
