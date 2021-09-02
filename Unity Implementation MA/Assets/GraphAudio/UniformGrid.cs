using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace GraphAudio
{
    public class UniformGrid
    {
        public Dictionary<uint, NodeDOTS[]> Grid;

        private Bounds _bounds;//AA-Bounding-Box of the grid

        private readonly Vector3 _gridSize; // In X-,Y-,Z-Dimension.
        private readonly Vector3 _cellSize; // In X-,Y-,Z-Dimension
        private readonly Vector3 _gridOrigin;
        private readonly int _numGridCells; // total number of cells in the grid

        public UniformGrid(in NativeArray<NodeDOTS> nodes, Bounds gridBounds, Vector3 gridSize)
        {
            _bounds = gridBounds;
            _gridSize = gridSize;
            _gridOrigin = gridBounds.min;
            _cellSize.x = gridBounds.size.x / _gridSize.x; // worldSize / gridSize
            _cellSize.y = gridBounds.size.y / _gridSize.y;
            _cellSize.z = gridBounds.size.z / _gridSize.z;
            _numGridCells = (int)Mathf.Floor(_gridSize.x * _gridSize.y * _gridSize.z);
            Grid = PutNodesInGrid(in nodes);
        }

        /// <summary>
        /// Returns all Nodes in the cell of pos and its neighbour cells
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public NodeDOTS[] GetNodesAroundPosition(Vector3 pos)
        {
            List<NodeDOTS> nodes = new List<NodeDOTS>();
            uint cell = CalcGridAddress(CalcGridPos(pos));
            nodes.AddRange(Grid[cell]);//this cell
            foreach(uint address in CalcGridNeighbourAddress(cell))//neighbour cells
                nodes.AddRange(Grid[address]);

            return nodes.ToArray();
        }

        /// <summary>
        /// Calculates the cell adress for every node in the graph and returns dictionary with the nodes
        /// inside of each cell.
        /// </summary>
        /// <param name="graph"></param>
        /// <returns></returns>
        private Dictionary<uint, NodeDOTS[]> PutNodesInGrid(in NativeArray<NodeDOTS> nodes)
        {
            Dictionary<uint, List<NodeDOTS>> tempDic = new Dictionary<uint, List<NodeDOTS>>(_numGridCells);

            foreach(var node in nodes)
            {
                //get the address for the cell in the grid where our current node is located
                uint cellAddress = CalcGridAddress(CalcGridPos(node.position));

                //Add node to the uniform grid
                if(tempDic.ContainsKey(cellAddress))
                    tempDic[cellAddress].Add(node);
                else
                {
                    var newList = new List<NodeDOTS>() { node };
                    tempDic.Add(cellAddress, newList);
                }
            }


            //convert Lists in dictionary to arrays for better runtime performance
            Dictionary<uint, NodeDOTS[]> dic = new Dictionary<uint, NodeDOTS[]>(_numGridCells);

            //create dic entrance for every cell, so we dont get a non existing key error at runtime
            for(uint i = 0; i < _numGridCells; i++)
            {
                NodeDOTS[] arr = (tempDic.ContainsKey(i)) ? tempDic[i].ToArray() : Array.Empty<NodeDOTS>();//so we later can use list.addRange() without checking for null obj
                dic.Add(i, arr);
            }

            return dic;
        }

        /// <summary>
        /// calculate position of p (world space) in uniform grid space 
        /// </summary>
        private Vector3 CalcGridPos(Vector3 p)
        {
            Vector3 gridPos;
            gridPos.x = Mathf.Floor((p.x - _gridOrigin.x) / _cellSize.x);
            gridPos.y = Mathf.Floor((p.y - _gridOrigin.y) / _cellSize.y);
            gridPos.z = Mathf.Floor((p.z - _gridOrigin.z) / _cellSize.z);
            return gridPos;
        }

        /// <summary>
        /// calculate address in grid from position (clamping to edges). Adresses begin at 0 
        /// </summary>
        private uint CalcGridAddress(Vector3 gridPos)
        {
            gridPos.x = Mathf.Max(0, Mathf.Min(gridPos.x, _gridSize.x - 1));
            gridPos.y = Mathf.Max(0, Mathf.Min(gridPos.y, _gridSize.y - 1));
            gridPos.z = Mathf.Max(0, Mathf.Min(gridPos.z, _gridSize.z - 1));
            return (uint)(((gridPos.z * _gridSize.y) * _gridSize.x) + (gridPos.y * _gridSize.x) + gridPos.x);
        }

        /// <summary>
        /// Calculates the grid addresses of the cells surrounding "cellAddress"
        /// NOTE: Edge-Case detection is not needed, because we test the nodes inside the cells for distance separatly
        /// </summary>
        /// <param name="cellAddress">Address of cell in grid</param>
        /// <returns></returns>
        private uint[] CalcGridNeighbourAddress(uint cellAddress)
        {
            List<uint> neighbours = new List<uint>();
            neighbours.Add(cellAddress + 1);//front
            neighbours.Add(cellAddress - 1);//behind
            uint offsetRL = System.Convert.ToUInt32(_gridSize.x * _gridSize.y);
            neighbours.Add(cellAddress + offsetRL);//right
            neighbours.Add(cellAddress + offsetRL + 1);//right front
            neighbours.Add(cellAddress + offsetRL - 1);//right behind
            neighbours.Add(cellAddress - offsetRL);//left
            neighbours.Add(cellAddress - offsetRL + 1);//left front
            neighbours.Add(cellAddress - offsetRL - 1);//left behind
            uint offsetTB = System.Convert.ToUInt32(_gridSize.x);
            neighbours.Add(cellAddress + offsetTB);//top
            neighbours.Add(cellAddress + offsetTB + 1);//top front
            neighbours.Add(cellAddress + offsetTB - 1);//top behind
            neighbours.Add(cellAddress + offsetTB + offsetRL);//top right
            neighbours.Add(cellAddress + offsetTB + offsetRL + 1);//top right front
            neighbours.Add(cellAddress + offsetTB + offsetRL - 1);//top right behind
            neighbours.Add(cellAddress + offsetTB - offsetRL);//top left
            neighbours.Add(cellAddress + offsetTB - offsetRL + 1);//top left front
            neighbours.Add(cellAddress + offsetTB - offsetRL - 1);//top left behind
            neighbours.Add(cellAddress - offsetTB);//bottom
            neighbours.Add(cellAddress - offsetTB + 1);//bottom front
            neighbours.Add(cellAddress - offsetTB - 1);//bottom behind
            neighbours.Add(cellAddress - offsetTB + offsetRL);//bottom right
            neighbours.Add(cellAddress - offsetTB + offsetRL + 1);//bottom right front
            neighbours.Add(cellAddress - offsetTB + offsetRL - 1);//bottom right behind
            neighbours.Add(cellAddress - offsetTB - offsetRL);//bottom left
            neighbours.Add(cellAddress - offsetTB - offsetRL + 1);//bottom left front
            neighbours.Add(cellAddress - offsetTB - offsetRL - 1);//bottom left behind

            //remove out of bounds values
            neighbours.RemoveAll(x => x < 0 || x >= _numGridCells);
            return neighbours.ToArray();
        }
    }
}