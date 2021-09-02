using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

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

        /// <summary>
        /// calculates the occlusion value for each edge in the graph.
        /// Calculation will be done with steam audio
        /// </summary>
        public void CalcOcclusionAllEdges()
        {
            //get steamAudio data from the scene
            var steamAudioManager = SteamAudio.SteamAudioManager.GetSingleton();
            if(steamAudioManager == null)
            {
                Debug.LogError("Phonon Manager Settings object not found in the scene! Click Window > Phonon");
                return;
            }

            steamAudioManager.Initialize(SteamAudio.GameEngineStateInitReason.Playing);
            SteamAudio.ManagerData managerData = steamAudioManager.ManagerData();

            var sceneExported = (managerData.gameEngineState.Scene().GetScene() != IntPtr.Zero);
            if(!sceneExported)
            {
                Debug.LogError("Scene not found. Make sure to pre-export the scene.");
                return;
            }

            var environment = managerData.gameEngineState.Environment().GetEnvironment();

            //Iterate over all Nodes and edges
            Parallel.ForEach(Nodes, node =>
            {
                new ParallelOptions//Use 75% of available cpu resources
                {
                    MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))
                };

                Parallel.ForEach(node.Neighbors, edge =>
                {
                    new ParallelOptions//Use 75% of available cpu resources
                    {
                        MaxDegreeOfParallelism = Convert.ToInt32(Math.Ceiling((Environment.ProcessorCount * 0.75) * 2.0))
                    };

                    float occlusionFactor = 1.0f - CalcOcclusionSteamAudio(environment, node._location, edge._target._location);//since in steamAudio 1.0f occlusion factor means no occlusion at all
                    edge._occlusionFloat = occlusionFactor;
                    edge._occlusion = Convert.ToByte(Math.Max(0, Math.Min(255, (int)Math.Floor(occlusionFactor * 256.0))));
                });
            });

            if(!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() { "Assets/GraphAudio/GraphDust2Acoustics.asset" });
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Uses SteamAudio to calculate a occlusion factor between the listener and sound source
        /// </summary>
        /// <param name="environment">Pointer to the game scene environment steam audio object. SteamAudio.ManagerData.gameEngineState.Environment().GetEnvironment();</param>
        /// <param name="listenerPos"></param>
        /// <param name="sourcePos"></param>
        /// <returns>Occlusion factor between 0.0f and 1.0f</returns>
        private float CalcOcclusionSteamAudio(IntPtr environment, Vector3 listenerPos, Vector3 sourcePos)
        {
            var listenerPosition = SteamAudio.Common.ConvertVector(listenerPos);
            var listenerAhead = SteamAudio.Common.ConvertVector((sourcePos - listenerPos).normalized);//listener is facing the sound source
            var listenerUp = SteamAudio.Common.ConvertVector(Vector3.up);

            var source = new SteamAudio.Source();
            source.position = SteamAudio.Common.ConvertVector(sourcePos);
            Vector3 sourceForward = (listenerPos - sourcePos).normalized;//source is facing the listener
            source.ahead = SteamAudio.Common.ConvertVector(sourceForward);
            source.up = SteamAudio.Common.ConvertVector(Vector3.up);
            source.right = SteamAudio.Common.ConvertVector(Vector3.Cross(Vector3.up, sourceForward).normalized);
            source.directivity = new SteamAudio.Directivity();
            source.directivity.dipoleWeight = 0.0f;//default from SteamAudioSource.cs
            source.directivity.dipolePower = 0.0f;//default from SteamAudioSource.cs
            source.directivity.callback = IntPtr.Zero;
            source.distanceAttenuationModel = new SteamAudio.DistanceAttenuationModel();
            source.airAbsorptionModel = new SteamAudio.AirAbsorptionModel();

            SteamAudio.DirectSoundPath directPath = SteamAudio.PhononCore.iplGetDirectSoundPath(environment, listenerPosition,
                listenerAhead, listenerUp, source, 1.0f, 16, SteamAudio.OcclusionMode.OcclusionWithFrequencyIndependentTransmission, SteamAudio.OcclusionMethod.Partial);

            return directPath.occlusionFactor;
        }

    }

    [BurstCompile]
    public struct GraphPathfindingDOTS : IJob
    {

        public NativeArray<NodeDOTS> nodes;
        [ReadOnly]
        public NativeArray<EdgeDOTS> edges;
        [ReadOnly]
        public NativeMultiHashMap<int, int> neighboursIndices;// key is index of node, values are indices of edges from key-node

        public int startNodeIdx;
        public float3 listenerPos;

        //use dijkstra-algorithm with greedy approach to determin the shortest pathes from startNode to all other nodes
        public void Execute()
        {
            DijkstraPathFinding();
        }

        private void DijkstraPathFinding()
        {
            //start node and all its neighbours are connected to the listener and totalAttenuation will be calculated after equation 6.5 of cowan (p. 177)
            NodeDOTS startNode = nodes[startNodeIdx];
            startNode.totalAttenuation = math.distance(startNode.position, listenerPos);
            nodes[startNode.index] = startNode;

            foreach(var edgeIdx in neighboursIndices.GetValuesForKey(startNode.index))
            {
                NodeDOTS target = nodes[edges[edgeIdx].ToNodeIndex];
                target.totalAttenuation = math.distance(target.position, listenerPos);
                nodes[edges[edgeIdx].ToNodeIndex] = target;//since we have native array we need to re-assign
            }

            //create HeapMap as priority queue. We use nodes.totalAttenuation for sorting
            NativeHeap<NodeDOTS, NodeDOTSMinComparer> priorityQueue = new NativeHeap<NodeDOTS, NodeDOTSMinComparer>(Allocator.Temp, initialCapacity: nodes.Length);
            NativeArray<NativeHeapIndex> heapIndices = new NativeArray<NativeHeapIndex>(nodes.Length, Allocator.Temp);
            //insert all of our nodes
            for(int i = 0; i < nodes.Length; i++)
                heapIndices[i] = priorityQueue.Insert(nodes[i]);

            while(priorityQueue.Count > 0)
            {
                NodeDOTS current = priorityQueue.Pop();

                //iterate this nodes edges and update neighbour nodes totalAttenuation/path length
                foreach(var edgeIdx in neighboursIndices.GetValuesForKey(current.index))
                {
                    NodeDOTS target = nodes[edges[edgeIdx].ToNodeIndex];
                    float newPathLength = current.totalAttenuation + edges[edgeIdx].length;
                    if(newPathLength < target.totalAttenuation)
                    {
                        //update target node
                        target.totalAttenuation = newPathLength;
                        target.predecessorIdx = current.index;
                        nodes[edges[edgeIdx].ToNodeIndex] = target;//since we have native array we need to re-assign

                        //update priority queue
                        if(priorityQueue.IsValidIndex(heapIndices[target.index]))
                            priorityQueue.Remove(heapIndices[target.index]);
                        heapIndices[target.index] = priorityQueue.Insert(target);
                    }
                }
            }

            //Dispose all native structures
            heapIndices.Dispose();
            priorityQueue.Dispose();
        }

    }

    /// <summary>
    /// Resets the nodes array for Dijkstra pathfinding with default values predecessorIdx = -1 and totalAttenuation = float.MaxValue
    /// </summary>
    [BurstCompile]
    public struct ResetNodesArray : IJobParallelFor
    {
        public NativeArray<NodeDOTS> nodes;

        public void Execute(int index)
        {
            NodeDOTS node = nodes[index];
            node.predecessorIdx = -1;
            node.totalAttenuation = float.MaxValue;
            nodes[index] = node;//native array, so we have to re-assign
        }
    }
}
