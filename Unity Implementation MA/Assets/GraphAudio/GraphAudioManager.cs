using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Linq;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using SteamAudio;
using UnityEditor;
using Debug = UnityEngine.Debug;
using Vector3 = UnityEngine.Vector3;

namespace GraphAudio
{
    public class GraphAudioManager : MonoBehaviour
    {
        public Graph _graph;
        public FMODUnity.StudioListener _fmodAudioListener;

        public static GraphAudioManager Instance { get; private set; } //Singleton

        private JobHandle _pathHandle, _clearHandle;
        private NativeArray<NodeDOTS> _nodesDOTS;
        private NativeArray<EdgeDOTS> _edgesDOTS;
        private NativeMultiHashMap<int, int> _neighboursIndices;
        private readonly List<FMODUnity.StudioEventEmitter> _soundSources = new List<StudioEventEmitter>();

        private UniformGrid _uniformGrid;
        private Vector3 _oldListenerPos = new Vector3(0.0f, 0.0f, 0.0f);
        private Dictionary<int, Vector3> _oldSourcePositions = new Dictionary<int, Vector3>();
        private Dictionary<int, float3> _newSourcePositions = new Dictionary<int, float3>();
        private int _interpolationFramesCount = 30; // Number of frames to completely interpolate between 2 positions
        private int _elapsedFrames = 0;
        private const int _layerMask = 1 << 6;// Bit shift the index of the map layer (6) to get a bit mask

        

        private void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            if (_graph == null)
                throw new System.ArgumentNullException("Graph is null");

            //Create native arrays with persistent memory allocation for our pathfinding job
            ConvertGraphToPathfindingDOTS(_graph, out _nodesDOTS, out _edgesDOTS, out _neighboursIndices);

            if (_fmodAudioListener == null)
                _fmodAudioListener = GameObject.FindObjectOfType<FMODUnity.StudioListener>();

            //Create uniform grid with our graph nodes
            GameObject child = transform.GetChild(0).gameObject;
            _uniformGrid = new UniformGrid(in _nodesDOTS, new Bounds(child.transform.position, child.transform.localScale), child.GetComponent<UniformGridGizmoRenderer>()._gridSize);
            
            //StartCoroutine(CalcEdgesOcclusion());
        }

        // FixedUpdate is called once per fixed time interval
        private void FixedUpdate()
        {
             //only re-calculate if listener position has changed
             Vector3 listenerPos = _fmodAudioListener.transform.parent.position; //we use parent transform, because fmodListener is on camera and not player model
             if (Vector3.Distance(listenerPos, _oldListenerPos) <= 0.1f)
                 return;

             //we dont use graph if we have direct path between listener and sound source
             List<int> activeSourceIndices = new List<int>(); //contains indices of _soundSources that use the graph (no direct path to listener)
             List<List<Vector3>> shortestPathsPositions = new List<List<Vector3>>(); //for shortest path visualization
             
             for (int i = 0; i < _soundSources.Count; i++)
                 if (Physics.Linecast(_fmodAudioListener.transform.position, _soundSources[i].transform.parent.position, _layerMask))
                     activeSourceIndices.Add(i);
                 else
                 {
                     //reset virtual sound source position and occlusion value
                     _soundSources[i].transform.position = 
                         Vector3.Lerp(_oldSourcePositions[i], _soundSources[i].transform.parent.position, Mathf.Clamp01((float)_elapsedFrames / _interpolationFramesCount));
                     _elapsedFrames++;
                     _soundSources[i].SetParameter("Occlusion", 1.0f);
                     //add for GraphNodeRenderer.Instance.renderShortestPaths
                     shortestPathsPositions.Add(new List<Vector3>(2) {_soundSources[i].transform.parent.position}); //listener pos will be added in graphNodeRenderer
                 }

             if (activeSourceIndices.Count == 0)
             {
                 if (GraphNodeRenderer.Instance.renderShortestPaths)
                     GraphNodeRenderer.Instance.VisualizeShortestPath(shortestPathsPositions, listenerPos);
                 //cache new listener position
                 _oldListenerPos = listenerPos;
                 return;
             }
             
             #region Listener find closest Node
             NativeArray<float3> listenerArray = new NativeArray<float3>(1, Allocator.TempJob);
             listenerArray[0] = listenerPos;
             //native array containing the nodes close to the listener position
             NativeArray<NodeDOTS> closeNodesListener = new NativeArray<NodeDOTS>(_uniformGrid.GetNodesAroundPosition(listenerPos), Allocator.TempJob);
             NativeArray<int> resultsFCNIJob = new NativeArray<int>(activeSourceIndices.Count, Allocator.TempJob); //for result of FindClosestNodeIdxJob

             //get players/listeners nearest node as resultsFCNIJob[0] via Job
             JobHandle closestListenerNodeHandle = new FindClosestNodeIdxJob
             {
                 positions = listenerArray,
                 nodes = closeNodesListener,
                 closestNodeIdx = resultsFCNIJob
             }.Schedule();

             #endregion

             #region Sound sources find closest Node & graph pathfinding

             //get all nodes close to all audio source positions
             HashSet<NodeDOTS> closeNodes = new HashSet<NodeDOTS>();
             NativeArray<float3> sourcePositions = new NativeArray<float3>(activeSourceIndices.Count, Allocator.TempJob,
                 NativeArrayOptions.UninitializedMemory);
             for (int i = 0; i < activeSourceIndices.Count; i++)
             {
                 //actual source position is saved in parent, because we move this gameObjects position later for direction rendering
                 sourcePositions[i] = _soundSources[activeSourceIndices[i]].transform.parent.transform.position;
                 closeNodes.UnionWith(
                     _uniformGrid.GetNodesAroundPosition(_soundSources[activeSourceIndices[i]].transform.parent.transform.position));
             }

             NativeArray<NodeDOTS> closeNodesSources =
                 new NativeArray<NodeDOTS>(closeNodes.ToArray(), Allocator.TempJob);

             //make sure arrays have been reseted from last frames clearJob
             _clearHandle.Complete();
             closestListenerNodeHandle.Complete();

             //Visualize node closest to listener position
             if (GraphNodeRenderer.Instance.renderClosestNodes)
                 GraphNodeRenderer.Instance.SetListenerPos(_nodesDOTS[resultsFCNIJob[0]].position);

             //create our pathfinding job and execute it
             GraphPathfindingDOTS graphDOTS = new GraphPathfindingDOTS
             {
                 nodes = _nodesDOTS,
                 edges = _edgesDOTS,
                 neighboursIndices = _neighboursIndices,
                 startNodeIdx = resultsFCNIJob[0],
                 listenerPos = listenerPos
             };
             _pathHandle = graphDOTS.Schedule();

             //create job to find closest nodes for each soundSource
             JobHandle closestSourcesNodeHandle = new FindClosestNodeIdxJob
             {
                 positions = sourcePositions,
                 nodes = closeNodesSources,
                 closestNodeIdx = resultsFCNIJob
             }.Schedule();


             //Dispose native arrays from listeners FindClosestNodeIdxJob
             closeNodesListener.Dispose();
             listenerArray.Dispose();

             //cache new listener position
             _oldListenerPos = listenerPos;

             _pathHandle.Complete();
             closestSourcesNodeHandle.Complete();

             #endregion

             #region FMOD play sounds

             //Set custom fmod parameters for each source
             for (int i = 0; i < activeSourceIndices.Count; i++)
             {
                 //occlusion and transmission via steam audio parameter
                 var directDistance = Vector3.Distance(listenerPos, _soundSources[activeSourceIndices[i]].transform.parent.position);
                 var occlusion =
                     Mathf.Clamp01(Mathf.Pow(directDistance / graphDOTS.nodes[resultsFCNIJob[i]].totalAttenuation,
                         2.0f)); //equation 6.7 (cowan p.185)
                 _soundSources[activeSourceIndices[i]].SetParameter("Occlusion", occlusion*0.5f);

                 //distance attenuation and direction via virtual sound source position
                 var closestNode = GetShortestPathClosestNode(graphDOTS, resultsFCNIJob[i]);
                 if (!closestNode.position.Equals(_newSourcePositions[activeSourceIndices[i]]) )
                 {
                     _oldSourcePositions[activeSourceIndices[i]] = _soundSources[activeSourceIndices[i]].transform.position;
                     _newSourcePositions[activeSourceIndices[i]] = closestNode.position;
                     _elapsedFrames = 0;
                 }
                 var virtualSourcePos = listenerPos + (Vector3)(closestNode.direction * graphDOTS.nodes[resultsFCNIJob[i]].totalAttenuation);
                 _soundSources[activeSourceIndices[i]].transform.position = 
                     Vector3.Lerp(_oldSourcePositions[activeSourceIndices[i]], virtualSourcePos, Mathf.Clamp01((float)_elapsedFrames / _interpolationFramesCount));
                 _elapsedFrames++;

                 if (GraphNodeRenderer.Instance.renderShortestPaths) //get the shortest path for this source-listener pair
                     shortestPathsPositions.Add(GetShortestPath(graphDOTS, resultsFCNIJob[i]));
             }

             #endregion

             //Visualize nodes closest to source positions
             if (GraphNodeRenderer.Instance.renderClosestNodes)
             {
                 List<Vector3> positions = new List<Vector3>();
                 for (int i = 0; i < activeSourceIndices.Count; i++)
                     positions.Add(_nodesDOTS[resultsFCNIJob[i]].position);
                 GraphNodeRenderer.Instance.SetSourcePositions(positions);
             }

             //Visualize shortest path found by dijkstra
             if (GraphNodeRenderer.Instance.renderShortestPaths)
                 GraphNodeRenderer.Instance.VisualizeShortestPath(shortestPathsPositions, listenerPos);

             //Dispose native arrays from sound sources FindClosestNodeIdxJob
             closeNodesSources.Dispose();
             sourcePositions.Dispose();
             resultsFCNIJob.Dispose();

             //reset _nodes array for next frame
             _clearHandle = new ResetNodesArray {nodes = _nodesDOTS}.Schedule(_nodesDOTS.Length, 1);
        }

        private void OnDisable()
        {
            //wait for jobs to finish
            _pathHandle.Complete();
            _clearHandle.Complete();

            //dispose all native objects
            _nodesDOTS.Dispose();
            _edgesDOTS.Dispose();
            _neighboursIndices.Dispose();
        }

        private bool GetSteamAudioDSP(out DSP steamSpatializer, int sourceIndex)
        {
            steamSpatializer = default;
            ChannelGroup group;
            _soundSources[sourceIndex].EventInstance.getChannelGroup(out @group);
            int numdsps;
            @group.getNumDSPs(out numdsps);
            for (int i = 0; i < numdsps; i++)
            {
                DSP dsp;
                @group.getDSP(i, out dsp);
                string name;
                uint version;
                int a, b, c;
                dsp.getInfo(out name, out version, out a, out b, out c);
                if (name.Equals("Steam Audio Spatializer"))
                    steamSpatializer = dsp;
            }

            return !steamSpatializer.Equals(default(DSP));
        }


        /// <summary>
        /// Iterates from the node at soundSourceNodeIndex towards the node closest to the listener
        /// on the graphs shortest path (has to be calculated beforehands) using the node.predecessorIdx
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="soundSourceNodeIndex"></param>
        /// <returns>List of positions for the nodes on shortest path from source to listener</returns>
        private List<Vector3> GetShortestPath(GraphPathfindingDOTS graph, int soundSourceNodeIndex)
        {
            List<Vector3> positions = new List<Vector3>();
            int predecessorIdx = graph.nodes[soundSourceNodeIndex].predecessorIdx;

            //Add start node (sound source)
            positions.Add(graph.nodes[soundSourceNodeIndex].position);

            //iterate through graph on shortest path
            while (predecessorIdx != -1)
            {
                positions.Add(graph.nodes[predecessorIdx].position);
                predecessorIdx = graph.nodes[predecessorIdx].predecessorIdx;
            }

            return positions;
        }

        /// <summary>
        /// Iterates from the node at soundSourceNodeIndex towards the node closest to the listener
        /// on the graphs shortest path (has to be calculated beforehands) using the node.predecessorIdx
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="soundSourceNodeIndex">index of node closest to sound source</param>
        /// <returns>Node from the shortest path source-listener closest to listener.</returns>
        private NodeDOTS GetShortestPathClosestNode(GraphPathfindingDOTS graph, int soundSourceNodeIndex)
        {
            int currNodeIdx = soundSourceNodeIndex;
            int predecessorIdx = graph.nodes[soundSourceNodeIndex].predecessorIdx;

            //iterate through graph on shortest path
            while (predecessorIdx != -1)
            {
                currNodeIdx = predecessorIdx;
                predecessorIdx = graph.nodes[predecessorIdx].predecessorIdx;
            }

            return graph.nodes[currNodeIdx];
        }

        /// <summary>
        /// Adds or removes a fmod sound source from the graph audio manager.
        /// Also gets the Steam Audio Spatializer DSP from that source
        /// </summary>
        /// <param name="source">fmod sound source to add/remove</param>
        public void AddSoundSource(FMODUnity.StudioEventEmitter source)
        {
            _soundSources.Add(source);
            _oldSourcePositions.Add(_soundSources.Count-1, source.transform.position);
            _newSourcePositions.Add(_soundSources.Count-1, Vector3.zero);
        }

        /// <summary>
        /// Removes a fmod sound source from the graph audio manager.
        /// Also removes the Steam Audio Spatializer DSP from that source
        /// </summary>
        /// <param name="source">fmod sound source to add/remove</param>
        public void RemoveSoundSource(FMODUnity.StudioEventEmitter source)
        {
            int index = _soundSources.FindIndex(x => x == source);
            if (index != -1)
            {
                _soundSources.RemoveAt(index);
                _oldSourcePositions.Remove(index);
                _newSourcePositions.Remove(index);
            }
        }

        /// <summary>
        /// Allocates new Native-Structs with Allocator.Persistent for the use with GraphPathfindingDOTS.
        /// Remember to later call Dispose() on those structs
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="nodesDOTS"></param>
        /// <param name="edgesDOTS"></param>
        /// <param name="neighboursIndices"></param>
        public static void ConvertGraphToPathfindingDOTS(Graph graph, out NativeArray<NodeDOTS> nodesDOTS, out NativeArray<EdgeDOTS> edgesDOTS, out NativeMultiHashMap<int, int> neighboursIndices)
        {
            //Create Native arrays for our graphs DOTS job. We want to keep the arrays for the lifetime so we dont have to allocate new ones every frame
            nodesDOTS = new NativeArray<NodeDOTS>(graph.Nodes.Count, Allocator.Persistent);
            neighboursIndices = new NativeMultiHashMap<int, int>(graph.Nodes.Count, Allocator.Persistent);
            List<EdgeDOTS> edges = new List<EdgeDOTS>(); //Since we dont know how many edges we have, we need a list first to later allocate memory for our NativeArray edgesDOTS
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                //add this node with dijkstra start values
                nodesDOTS[i] = new NodeDOTS()
                {
                    position = graph.Nodes[i]._location,
                    totalAttenuation = float.MaxValue, //dijkstra path length
                    index = i,
                    predecessorIdx = -1
                };

                //add each edge from this node to our list. Also save the index from this edge for our node neighbour list
                foreach (Edge edg in graph.Nodes[i].Neighbors)
                {
                    var newEdge = new EdgeDOTS()
                    {
                        length = edg._length * (1 + (Mathf.Pow(edg._occlusion, 1.5f) / 4)), //Equation 6.2 (Cowan page 173)
                        FromNodeIndex = graph.Nodes.FindIndex(x => x == edg._origin),
                        ToNodeIndex = graph.Nodes.FindIndex(x => x == edg._target)
                    };
                    var index = edges.FindIndex(x => x.FromNodeIndex == newEdge.FromNodeIndex && x.ToNodeIndex == newEdge.ToNodeIndex);
                    if(index == -1)
                    {
                        edges.Add(newEdge);   
                        neighboursIndices.Add(i, edges.Count - 1); //NativeMultiHashMap can have multiple values for a key. We use this to save all edges indices from a single node                  
                    }
                    else
                        neighboursIndices.Add(i, index); //NativeMultiHashMap can have multiple values for a key. We use this to save all edges indices from a single node                  
                }
            }

            //convert our edges list to native array
            edgesDOTS = new NativeArray<EdgeDOTS>(edges.ToArray(), Allocator.Persistent);
        }

        private IEnumerator CalcEdgesOcclusion()
        {
            yield return new WaitForSeconds(5);
            foreach (var edge in _graph.AllEdges)
            {
                _fmodAudioListener.transform.position = edge._target._location;
                _soundSources[0].transform.position = edge._origin._location;
                yield return new WaitForEndOfFrame();
                var output = _soundSources[0].gameObject.GetComponent<SteamAudioSource>().GetOutputs(SimulationFlags.Direct).direct;
                float occlusionFactor = 1.0f - output.occlusion;
                edge._occlusionFloat = (edge._occlusionFloat + occlusionFactor) / 2.0f;
                edge._occlusion = Convert.ToByte(Math.Max(0, Math.Min(255, (int)Math.Floor(edge._occlusionFloat * 256.0))));
            }
            Debug.Log("GraphAudio: Occlusion calculation finished.");
            AssetDatabase.SaveAssets();
        }
        
        /// <summary>
        /// Unity Job to find the node closest to position. Each position will have its closest node with the same Index in closestNodeIdx-array
        /// </summary>
        [BurstCompile]
        struct FindClosestNodeIdxJob : IJob
        {
            [ReadOnly] public NativeArray<float3> positions;
            [ReadOnly] public NativeArray<NodeDOTS> nodes;
            [WriteOnly] public NativeArray<int> closestNodeIdx; //OUTPUT. index of the node closest to position[index]

            public void Execute()
            {
                NativeArray<float> closestDistance = new NativeArray<float>(positions.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory); //use array option since we write to the whole array immendiatly without reading
                //initialize closestDistance
                for (int i = 0; i < closestDistance.Length; i++)
                {
                    closestDistance[i] = float.MaxValue;
                }

                //iterate every node in graph
                for (int i = 0; i < nodes.Length; i++)
                {
                    for (int j = 0; j < closestDistance.Length; j++)
                    {
                        //calc distance from node to position
                        float distance = math.distance(nodes[i].position, positions[j]);
                        if (distance < closestDistance[j])
                        {
                            closestNodeIdx[j] = nodes[i].index;
                            closestDistance[j] = distance;
                        }
                    }
                }

                closestDistance.Dispose();
            }
        }
    }
}