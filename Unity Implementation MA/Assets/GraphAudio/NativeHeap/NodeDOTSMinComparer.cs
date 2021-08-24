using System.Collections.Generic;
using GraphAudio;

namespace Unity.Collections
{

    public struct NodeDOTSMinComparer : IComparer<NodeDOTS>
    {
        public int Compare(NodeDOTS a, NodeDOTS b)
        {
            return a.totalAttenuation.CompareTo(b.totalAttenuation);
        }
    }
}
