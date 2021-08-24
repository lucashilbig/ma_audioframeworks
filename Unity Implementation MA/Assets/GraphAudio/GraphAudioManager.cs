using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace GraphAudio
{
    public class GraphAudioManager : MonoBehaviour
    {
        public Graph _graph;
        public FMODUnity.StudioListener _fmodAudioListener;

        public static GraphAudioManager Instance { get; private set; }//Singleton

        private JobHandle _jobHandle, _clearHandle;
        private NativeArray<NodeDOTS> _nodesDOTS;
        private NativeArray<EdgeDOTS> _edgesDOTS;
        private NativeMultiHashMap<int, int> _neighboursIndices;
        private List<FMODUnity.StudioEventEmitter> _soundSources = new List<FMODUnity.StudioEventEmitter>();

        private UniformGrid _uniformGrid;

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
            Vector3 listenerPos = _fmodAudioListener.gameObject.transform.position;
            //native array containing the nodes close to the listener position
            NativeArray<NodeDOTS> closeNodesListener = new NativeArray<NodeDOTS>(_uniformGrid.GetNodesAroundPosition(listenerPos), Allocator.TempJob);
            NativeArray<int> startIndex = new NativeArray<int>(1, Allocator.TempJob);//for result of FindClosestNodeIdxJob
            //get players nearest node as startIndex[0] via Job
            JobHandle closestNodeHandle = new FindClosestNodeIdxJob
            {
                position = listenerPos,
                nodes = closeNodesListener,
                closestNodeIdx = startIndex,
                closestDistance = float.MaxValue
            }.Schedule();


            //make sure arrays have been reseted from last frames clearJob
            _clearHandle.Complete();
            closestNodeHandle.Complete();

            //create our pathfinding job and execute it
            GraphPathfindingDOTS graphDOTS = new GraphPathfindingDOTS
            {
                nodes = _nodesDOTS,
                edges = _edgesDOTS,
                neighboursIndices = _neighboursIndices,
                startNodeIdx = startIndex[0]
            };
            _jobHandle = graphDOTS.Schedule();

            //TODO: Find closest node to audio source position


            //Dispose native arrays from FindClosestNodeIdxJob
            startIndex.Dispose();
            closeNodesListener.Dispose();
            //play sounds with fmod
            _jobHandle.Complete();

            //reset _nodes array for next frame
            _clearHandle = new ResetNodesArray { nodes = _nodesDOTS }.Schedule(_nodesDOTS.Length, 1);
        }

        private void OnDisable()
        {
            //wait for jobs to finish
            _jobHandle.Complete();
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

        [BurstCompile]
        struct FindClosestNodeIdxJob : IJob
        {
            [ReadOnly]
            public float3 position;
            [ReadOnly]
            public NativeArray<NodeDOTS> nodes;
            [WriteOnly]
            public NativeArray<int> closestNodeIdx;//index of the node closest to position
            public float closestDistance;

            public void Execute()
            {
                for(int i = 0; i < nodes.Length; i++)
                {
                    //calc distance from node to position
                    float distance = Mathf.Sqrt(Mathf.Pow(position.x - nodes[i].position.x, 2) + Mathf.Pow(position.y - nodes[i].position.y, 2)
                        + Mathf.Pow(position.z - nodes[i].position.z, 2));
                    if(distance < closestDistance)
                    {
                        closestNodeIdx[0] = nodes[i].index;
                        closestDistance = distance;
                    }
                }
            }
        }
    }
}
