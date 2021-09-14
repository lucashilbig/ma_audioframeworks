using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace GraphAudio
{
    public static class GraphMenuItems
    {
        //[MenuItem("Window/Graph Audio/Create Graph from Navmesh")]
        public static void CreateGraphFromNavmesh()
        {
            // Create graph
            Graph graph = Graph.Create("Graph" + SceneManager.GetActiveScene().name);

            // Get node locations from navmesh
            AddAsNodesToGraph(graph, CreateNodeLocationsFromNavmesh());

            AssetDatabase.SaveAssets();
        }

        //[MenuItem("Window/Graph Audio/AddY0point5")]
        public static void AddY0point5()
        {
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));

            foreach (var node in graph.Nodes)
            {
                node._location.y += 0.5f;
            }

            if (!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/GraphAudio/GraphProjectAcousticsDemo.asset"});
            AssetDatabase.SaveAssets();
        }

        //[MenuItem("Window/Graph Audio/AddNodeLayerOnTop")]
        public static void AddNodeLayerOnTop()
        {
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));
            var newNodes = new List<Node>();

            foreach (var node in graph.Nodes)
            {
                Node newNode = Node.Create(node.name + "A");
                var newLoc = node._location;
                newLoc.y += 1.5f;
                newNode._location = newLoc;
                newNodes.Add(newNode);
            }

            foreach (var t in newNodes) graph.AddNode(t);

            if (!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/GraphAudio/GraphProjectAcousticsDemo.asset"});
            AssetDatabase.SaveAssets();
        }

        //[MenuItem("Window/Graph Audio/CleanAsset")]
        public static void CleanAsset()
        {
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));
            var namesToRemove = new List<string>();
            for (int i = 0; i <= 3861; i++)
            {
                if (!graph.Nodes.Exists(x => x.name.Equals("Node" + i)))
                    namesToRemove.Add("Node" + i);
            }

            var file = File.ReadAllLines(@"C:\Users\LucasHilbig\Documents\Uni\1_Masterarbeit\Implementation_MA\Unity Implementation MA\Assets\GraphAudio\GraphProjectAcousticsDemo.asset");
            for(int i = 0; i < file.Length; i++)
            {
                if (file[i].Contains("m_Name"))
                {
                    var name = file[i].Split(':')[1].Trim();
                    if (namesToRemove.Contains(name))
                    {
                        file[i - 10] = " ";
                        file[i - 9] = " ";
                        file[i - 8] = " ";
                        file[i - 7] = " ";
                        file[i - 6] = " ";
                        file[i - 5] = " ";
                        file[i - 4] = " ";
                        file[i - 3] = " ";
                        file[i - 2] = " ";
                        file[i - 1] = " ";
                        file[i] = " ";
                        file[i + 1] = " ";
                        file[i + 2] = " ";
                        file[i + 3] = " ";
                    }
                }
            }

            var newLines = new List<string>();
            foreach (var line in file)
            {
                if(!string.IsNullOrWhiteSpace(line))
                    newLines.Add(line);
            }
            
            File.WriteAllLines(@"C:\Users\LucasHilbig\Documents\Uni\1_Masterarbeit\Implementation_MA\Unity Implementation MA\Assets\GraphAudio\GraphProjectAcousticsDemo.asset",
                newLines);
        }


        //[MenuItem("Window/Graph Audio/CleanNullEntries")]
        public static void CleanNullEntries()
        {
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));

            Debug.Log("Removed null nodes: " + graph.Nodes.RemoveAll(x => x == null));

            if (!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/GraphAudio/GraphProjectAcousticsDemo.asset"});
            AssetDatabase.SaveAssets();
        }

        //[MenuItem("Window/Graph Audio/CreateAllEdges")]
        public static void CreateAllEdges()
        {
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));

            graph.CreateAllEdges(3.0f);

            if (!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/GraphAudio/GraphProjectAcousticsDemo.asset"});
            AssetDatabase.SaveAssets();
        }
        
        [MenuItem("Window/Graph Audio/Calc Occlusion")]
        public static void CalcOccl()
        {
            if (!Application.isPlaying)
                return;
            
            var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));

            graph.CalcOcclusionAllEdges("Assets/GraphAudio/GraphProjectAcousticsDemo.asset");

        }

        /// <summary>
        /// Usage: Create GameObject "BoundsHighNode" with UniformGridGizmoRenderer-Component and move the BB around the Nodes that should be
        /// removed. Then adjust the height in .RemoveAll() call.
        /// </summary>
        [MenuItem("Window/Graph Audio/Cleanup High Nodes")]
        public static void CleanHighNodes()
        {
            // var graph = (Graph) AssetDatabase.LoadAssetAtPath("Assets/GraphAudio/GraphProjectAcousticsDemo.asset", typeof(Graph));
            // var boundsGameObject = GameObject.Find("BoundsHighNode");
            // var gridBounds = new Bounds(boundsGameObject.transform.position, boundsGameObject.transform.localScale);
            //
            // var count = graph.Nodes.RemoveAll(x => gridBounds.Contains(x._location) && x._location.y > 5.0f);
            // Debug.Log("Removed Nodes: " + count);

            if (!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/GraphAudio/GraphProjectAcousticsDemo.asset"});
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Window/Graph Audio/SaveFMOD")]
        public static void SaveFMOD()
        {
            AssetDatabase.ForceReserializeAssets(new List<string>() {"Assets/Plugins/FMOD/Resources/FMODStudioSettings.asset"});
            AssetDatabase.SaveAssets();
        }

        private static void AddAsNodesToGraph(Graph graph, List<Vector3> probeLocations)
        {
            //Create Node for every probe location
            for (int i = 0; i < probeLocations.Count; i++)
            {
                Node node = Node.Create("Node" + i);
                node._location = probeLocations[i];
                graph.AddNode(node);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Returns node locations in a grid pattern on top of the active scenes navmesh.
        /// </summary>
        /// <returns></returns>
        private static List<Vector3> CreateNodeLocationsFromNavmesh()
        {
            var locations = new List<Vector3>();

            var gridObject = GameObject.Find("UniformGridBounds");
            Bounds gridBounds = new Bounds(gridObject.transform.position, gridObject.transform.localScale);

            //iterate in a grid over scenes BB
            for (float x = gridBounds.min.x; x <= gridBounds.max.x; x += 2.0f)
            for (float y = gridBounds.min.y; y <= gridBounds.max.y; y += 2.0f)
            for (float z = gridBounds.min.z; z <= gridBounds.max.z; z += 2.0f)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(new Vector3(x, y, z), out hit, 1.0f, NavMesh.AllAreas))
                {
                    if (!locations.Exists(x => Vector3.Distance(x, hit.position) < 1.0f))
                        locations.Add(hit.position);
                }
            }

            return locations;
        }
    }
}