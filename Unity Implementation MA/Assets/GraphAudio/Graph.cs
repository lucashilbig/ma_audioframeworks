using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Threading;
using SteamAudio;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Vector3 = UnityEngine.Vector3;

namespace GraphAudio
{
    public class Graph : ScriptableObject
    {
        [SerializeField]
        private List<Node> nodes;
        [SerializeField]
        private List<Edge> allEdges;
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
        public List<Edge> AllEdges
        {
            get
            {
                if(allEdges == null)
                {
                    allEdges = new List<Edge>();
                }

                return allEdges;
            }
            private set => allEdges = value;
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
            var allEdgesList = new List<Edge>();
            foreach(var (item1, item2) in neighbours)
                foreach(var nd in item2)
                {
                    var edge = allEdgesList.Find(x => x._target == item1 && x._origin == nd);
                    if(edge == null)
                        allEdgesList.Add(item1.AddEdge(nd));
                    else
                        item1.Neighbors.Add(edge);
                }

            AllEdges = allEdgesList;
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
        public void CalcOcclusionAllEdges(string assetPath)
        {
            //create new steamAudio simulator and load scene into it
            Context context = new Context();
            var settings = SteamAudioManager.GetSimulationSettings(false);
            settings.flags = SimulationFlags.Direct;
            var simulator = new Simulator(context, settings);
            var scene = new Scene(context, SteamAudio.SceneType.Default, null, null, 
                SteamAudioManager.ClosestHit, SteamAudioManager.AnyHit);
            scene.Commit();
            simulator.SetScene(scene);
            simulator.Commit();
            
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

                    float occlusionFactor = 1.0f - CalcOcclusionSteamAudio(simulator, node._location, edge._target._location);//since in steamAudio 1.0f occlusion factor means no occlusion at all
                    edge._occlusionFloat = occlusionFactor;
                    edge._occlusion = Convert.ToByte(Math.Max(0, Math.Min(255, (int)Math.Floor(occlusionFactor * 256.0))));
                });
            });

            if(!Application.isPlaying)
                AssetDatabase.ForceReserializeAssets(new List<string>() { assetPath });
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Uses SteamAudio to calculate a occlusion factor between the listener and sound source
        /// </summary>
        /// <param name="simulator">Simulator to use for steamAudio calculations</param>
        /// <param name="listenerPos"></param>
        /// <param name="sourcePos"></param>
        /// <returns>Occlusion factor between 0.0f and 1.0f</returns>
        private float CalcOcclusionSteamAudio(Simulator simulator, Vector3 listenerPos, Vector3 sourcePos)
        {
            //set listener pos and settings for simulation
            var sharedInputs = new SimulationSharedInputs { };
            sharedInputs.listener.origin = Common.ConvertVector(listenerPos);
            var listenerForward = (sourcePos - listenerPos).normalized;
            sharedInputs.listener.ahead = Common.ConvertVector(listenerForward);
            sharedInputs.listener.up = Common.ConvertVector(Vector3.up);
            sharedInputs.listener.right = Common.ConvertVector(Vector3.Cross(Vector3.up, listenerForward).normalized);
            sharedInputs.numRays = SteamAudioSettings.Singleton.realTimeRays;
            sharedInputs.numBounces = SteamAudioSettings.Singleton.realTimeBounces;
            sharedInputs.duration = SteamAudioSettings.Singleton.realTimeDuration;
            sharedInputs.order = SteamAudioSettings.Singleton.realTimeAmbisonicOrder;
            sharedInputs.irradianceMinDistance = SteamAudioSettings.Singleton.realTimeIrradianceMinDistance;
            simulator.SetSharedInputs(SimulationFlags.Direct, sharedInputs);

            //set source pos and settings
            var settings = SteamAudioManager.GetSimulationSettings(false);
            settings.flags = SimulationFlags.Direct;
            var source = new Source(simulator, settings);
            source.AddToSimulator(simulator);
            
            var inputs = new SimulationInputs { };
            inputs.source.origin = Common.ConvertVector(sourcePos);
            Vector3 sourceForward = (listenerPos - sourcePos).normalized;//source is facing the listener
            inputs.source.ahead = Common.ConvertVector(sourceForward);
            inputs.source.up = Common.ConvertVector(Vector3.up);
            inputs.source.right = Common.ConvertVector(Vector3.Cross(Vector3.up, sourceForward).normalized);
            inputs.distanceAttenuationModel.type = DistanceAttenuationModelType.Default;
            inputs.airAbsorptionModel.type = AirAbsorptionModelType.Default;
            inputs.directivity.dipoleWeight = 0.0f;
            inputs.directivity.dipolePower = 0.0f;
            inputs.occlusionType = OcclusionType.Raycast;
            inputs.occlusionRadius = 1.0f;
            inputs.numOcclusionSamples = 16;
            inputs.reverbScaleLow = 1.0f;
            inputs.reverbScaleMid = 1.0f;
            inputs.reverbScaleHigh = 1.0f;
            inputs.hybridReverbTransitionTime = SteamAudioSettings.Singleton.hybridReverbTransitionTime;
            inputs.hybridReverbOverlapPercent = SteamAudioSettings.Singleton.hybridReverbOverlapPercent / 100.0f;
            inputs.baked = Bool.False;
            inputs.pathingProbes = IntPtr.Zero;
            inputs.visRadius = SteamAudioSettings.Singleton.bakingVisibilityRadius;
            inputs.visThreshold = SteamAudioSettings.Singleton.bakingVisibilityThreshold;
            inputs.visRange = SteamAudioSettings.Singleton.bakingVisibilityRange;
            inputs.pathingOrder = SteamAudioSettings.Singleton.bakingAmbisonicOrder;
            inputs.enableValidation = Bool.False;
            inputs.findAlternatePaths = Bool.False;
            inputs.flags = SimulationFlags.Direct;
            inputs.directFlags = 0;
            inputs.directFlags = inputs.directFlags | DirectSimulationFlags.Occlusion;
            source.SetInputs(SimulationFlags.Direct, inputs);
            
            //run occlusion calculations
            simulator.RunDirect();
            
            //get outputs from simulation
            var outputs = source.GetOutputs(SimulationFlags.Direct);
            var occlusionValue = outputs.direct.occlusion;
            //remove from simulation after we finished
            source.RemoveFromSimulator(simulator);

            return occlusionValue;
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
            startNode.direction = Vector3.Normalize(startNode.position - listenerPos);//cowan equation 6.6
            nodes[startNode.index] = startNode;

            foreach(var edgeIdx in neighboursIndices.GetValuesForKey(startNode.index))
            {
                var targetIdx = (edges[edgeIdx].ToNodeIndex != startNode.index) ? edges[edgeIdx].ToNodeIndex : edges[edgeIdx].FromNodeIndex;
                NodeDOTS target = nodes[targetIdx];
                target.totalAttenuation = math.distance(target.position, listenerPos);
                target.direction = Vector3.Normalize(target.position - listenerPos);//cowan equation 6.6
                nodes[targetIdx] = target;//since we have native array we need to re-assign
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
                    var targetIdx = (edges[edgeIdx].ToNodeIndex != current.index) ? edges[edgeIdx].ToNodeIndex : edges[edgeIdx].FromNodeIndex;
                    NodeDOTS target = nodes[targetIdx];
                    float newPathLength = current.totalAttenuation + edges[edgeIdx].length;
                    if(newPathLength < target.totalAttenuation)
                    {
                        //update target node
                        target.totalAttenuation = newPathLength;
                        target.predecessorIdx = current.index;
                        nodes[targetIdx] = target;//since we have native array we need to re-assign

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
