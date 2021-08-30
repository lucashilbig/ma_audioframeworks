using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using System.Linq;

namespace GraphAudio
{
    public class GraphAudioManager : MonoBehaviour
    {
        public Graph _graph;
        public FMODUnity.StudioListener _fmodAudioListener;

        public static GraphAudioManager Instance { get; private set; }//Singleton

        private JobHandle _pathHandle, _clearHandle;
        private NativeArray<NodeDOTS> _nodesDOTS;
        private NativeArray<EdgeDOTS> _edgesDOTS;
        private NativeMultiHashMap<int, int> _neighboursIndices;
        private List<FMODUnity.StudioEventEmitter> _soundSources = new List<FMODUnity.StudioEventEmitter>();

        private UniformGrid _uniformGrid;
        private Vector3 _oldListenerPos = new Vector3(0.0f, 0.0f, 0.0f);

        #region Test Variables
        #endregion

        private void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            if(_graph == null)
                throw new System.ArgumentNullException("Graph is null");

            //Create native arrays with persistent memory allocation for our pathfinding job
            ConvertGraphToPathfindingDOTS(_graph, out _nodesDOTS, out _edgesDOTS, out _neighboursIndices);

            if(_fmodAudioListener == null)
                _fmodAudioListener = GameObject.FindObjectOfType<FMODUnity.StudioListener>();

            //Create uniform grid with our graph nodes
            GameObject child = transform.GetChild(0).gameObject;
            _uniformGrid = new UniformGrid(in _nodesDOTS, new Bounds(child.transform.position, child.transform.localScale), child.GetComponent<UniformGridGizmoRenderer>()._gridSize);
        }

        // FixedUpdate is called once per fixed time interval
        private void FixedUpdate()
        {
            #region Listener find closest Node
            Vector3 listenerPos = _fmodAudioListener.transform.position;
            //only re-calculate graph if listener position has changed
            if(Vector3.Distance(listenerPos, _oldListenerPos) > 0.1f)
            {
                NativeArray<float3> listenerArray = new NativeArray<float3>(1, Allocator.TempJob);
                listenerArray[0] = listenerPos;
                //native array containing the nodes close to the listener position
                NativeArray<NodeDOTS> closeNodesListener = new NativeArray<NodeDOTS>(_uniformGrid.GetNodesAroundPosition(listenerPos), Allocator.TempJob);
                NativeArray<int> resultsFCNIJob = new NativeArray<int>(_soundSources.Count, Allocator.TempJob);//for result of FindClosestNodeIdxJob
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
                NativeArray<float3> sourcePositions = new NativeArray<float3>(_soundSources.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                for(int i = 0; i < _soundSources.Count; i++)
                {
                    sourcePositions[i] = _soundSources[i].transform.position;
                    closeNodes.UnionWith(_uniformGrid.GetNodesAroundPosition(_soundSources[i].transform.position));
                }

                NativeArray<NodeDOTS> closeNodesSources = new NativeArray<NodeDOTS>(closeNodes.ToArray(), Allocator.TempJob);

                //make sure arrays have been reseted from last frames clearJob
                _clearHandle.Complete();
                closestListenerNodeHandle.Complete();

                //Visualize node closest to listener position
                if(GraphNodeRenderer.Instance.renderClosestNodes)
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

                //TEST graph distance vs direct distance of listener-source
                for(int i = 0; i < _soundSources.Count; i++)
                {
                    float newDirect = Vector3.Distance(_soundSources[i].transform.position, listenerPos);
                    UnityEngine.Debug.Log("Direct -> " + newDirect + System.Environment.NewLine
                        + "Graph -> " + _nodesDOTS[resultsFCNIJob[i]].totalAttenuation);
                }

                
                #endregion
                //Visualize nodes closest to source positions
                if(GraphNodeRenderer.Instance.renderClosestNodes)
                {
                    List<Vector3> positions = new List<Vector3>();
                    for(int i = 0; i < _soundSources.Count; i++)
                        positions.Add(_nodesDOTS[resultsFCNIJob[i]].position);
                    GraphNodeRenderer.Instance.SetSourcePositions(positions);
                }

                //Dispose native arrays from sound sources FindClosestNodeIdxJob
                closeNodesSources.Dispose();
                sourcePositions.Dispose();
                resultsFCNIJob.Dispose();

                //reset _nodes array for next frame
                _clearHandle = new ResetNodesArray { nodes = _nodesDOTS }.Schedule(_nodesDOTS.Length, 1);
            }
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

        /// <summary>
        /// Adds or removes a fmod sound source from the graph audio manager.
        /// </summary>
        /// <param name="source">fmod sound source to add/remove</param>
        /// <param name="enabled">true for adding, false for removing the source</param>
        public void AddSoundSource(FMODUnity.StudioEventEmitter source, bool enabled)
        {
            if(enabled)
                _soundSources.Add(source);
            else
                _soundSources.Remove(source);
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
            List<EdgeDOTS> edges = new List<EdgeDOTS>();//Since we dont know how many edges we have, we need a list first to later allocate memory for our NativeArray edgesDOTS
            for(int i = 0; i < graph.Nodes.Count; i++)
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
                foreach(Edge edg in graph.Nodes[i].Neighbors)
                {
                    edges.Add(new EdgeDOTS()
                    {
                        length = edg._length * (1 + (Mathf.Pow(edg._occlusion, 1.5f) / 4)),//Equation 6.2 (Cowan page 173)
                        FromNodeIndex = i,
                        ToNodeIndex = graph.Nodes.FindIndex(x => x == edg._target)
                    });
                    neighboursIndices.Add(i, edges.Count - 1);//NativeMultiHashMap can have multiple values for a key. We use this to save all edges indices from a single node                  
                }
            }
            //convert our edges list to native array
            edgesDOTS = new NativeArray<EdgeDOTS>(edges.ToArray(), Allocator.Persistent);
        }

        /// <summary>
        /// Unity Job to find the node closest to position. Each position will have its closest node with the same Index in closestNodeIdx-array
        /// </summary>
        [BurstCompile]
        struct FindClosestNodeIdxJob : IJob
        {
            [ReadOnly]
            public NativeArray<float3> positions;
            [ReadOnly]
            public NativeArray<NodeDOTS> nodes;
            [WriteOnly]
            public NativeArray<int> closestNodeIdx;//OUTPUT. index of the node closest to position[index]

            public void Execute()
            {
                NativeArray<float> closestDistance = new NativeArray<float>(positions.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);//use array option since we write to the whole array immendiatly without reading
                //initialize closestDistance
                for(int i = 0; i < closestDistance.Length; i++)
                {
                    closestDistance[i] = float.MaxValue;
                }

                //iterate every node in graph
                for(int i = 0; i < nodes.Length; i++)
                {
                    for(int j = 0; j < closestDistance.Length; j++)
                    {
                        //calc distance from node to position
                        float distance = math.distance(nodes[i].position, positions[j]);
                        if(distance < closestDistance[j])
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
