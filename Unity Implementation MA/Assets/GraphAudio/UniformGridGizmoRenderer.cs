using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class UniformGridGizmoRenderer : MonoBehaviour
{
    public bool _renderGridCells = false;
    public bool _renderCellAdress = false;
    public Vector3 _gridSize = new Vector3(64f, 6f, 64f);
    private Vector3 _cellSize; // In X-,Y-,Z-Dimension
    private Bounds _gridBounds;

    private void OnDrawGizmos()
    {
        if(_renderGridCells)
        {
            Gizmos.color = Color.cyan;
            
            for(float x = _gridBounds.min.x; x <= _gridBounds.max.x; x += _cellSize.x)
            {
                //draw horizontal lines
                for(float y = _gridBounds.min.y; y <= _gridBounds.max.y; y += _cellSize.y)
                {
                    Vector3 from = new Vector3(x, y, _gridBounds.min.z);
                    Gizmos.DrawLine(from, new Vector3(from.x, from.y, from.z + _gridBounds.size.z));
                }
                //draw vertical lines
                for(float z = _gridBounds.min.z; z <= _gridBounds.max.z; z += _cellSize.z)
                {
                    Vector3 from = new Vector3(x, _gridBounds.min.y, z);
                    Gizmos.DrawLine(from, new Vector3(from.x, from.y + _gridBounds.size.y, from.z));
                }
            }
            for(float z = _gridBounds.min.z; z <= _gridBounds.max.z; z += _cellSize.z)
            {
                //draw horizontal lines
                for(float x = _gridBounds.min.x; x <= _gridBounds.max.x; x += _cellSize.x)
                {
                    Vector3 from = new Vector3(x, _gridBounds.min.y, z);
                    Gizmos.DrawLine(from, new Vector3(from.x, from.y + _gridBounds.size.y, from.z));
                }
                //draw vertical lines
                for(float y = _gridBounds.min.y; y <= _gridBounds.max.y; y += _cellSize.y)
                {
                    Vector3 from = new Vector3(_gridBounds.min.x, y, z);
                    Gizmos.DrawLine(from, new Vector3(from.x + _gridBounds.size.x, from.y, from.z));
                }
            }
        }

        if(_renderCellAdress)
        {
            for(float x = _gridBounds.min.x + 0.5f * _cellSize.x; x < _gridBounds.max.x; x += _cellSize.x)
                for(float y = _gridBounds.min.y + 0.5f * _cellSize.y; y <= _gridBounds.max.y; y += _cellSize.y)
                    for(float z = _gridBounds.min.z + 0.5f * _cellSize.z; z <= _gridBounds.max.z; z += _cellSize.z)
                    {
                        var pos = new Vector3(x, y, z);
                        var gridPos = CalcGridPos(pos);
                        var gridHash = CalcGridAddress(gridPos);
                            Handles.Label(pos, gridHash.ToString());

                    }
        }
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, transform.localScale);

    }

    private void OnValidate()
    {
        Bounds gridBounds = new Bounds(transform.position, transform.localScale);
        _gridBounds = gridBounds;
        _cellSize.x = gridBounds.size.x / _gridSize.x; // worldSize / gridSize
        _cellSize.y = gridBounds.size.y / _gridSize.y;
        _cellSize.z = gridBounds.size.z / _gridSize.z;
    }

    private Vector3 CalcGridPos(Vector3 p)
    {
        Vector3 gridPos;
        gridPos.x = Mathf.Floor((p.x - _gridBounds.min.x) / _cellSize.x);
        gridPos.y = Mathf.Floor((p.y - _gridBounds.min.y) / _cellSize.y);
        gridPos.z = Mathf.Floor((p.z - _gridBounds.min.z) / _cellSize.z);
        return gridPos;
    }

    // calculate address in grid from position (clamping to edges)
    private uint CalcGridAddress(Vector3 gridPos)
    {
        gridPos.x = Mathf.Max(0, Mathf.Min(gridPos.x, _gridSize.x - 1));
        gridPos.y = Mathf.Max(0, Mathf.Min(gridPos.y, _gridSize.y - 1));
        gridPos.z = Mathf.Max(0, Mathf.Min(gridPos.z, _gridSize.z - 1));
        return (uint)(((gridPos.z * _gridSize.y) * _gridSize.x) + (gridPos.y * _gridSize.x) + gridPos.x);
    }
}
