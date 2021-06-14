// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Microsoft.Acoustics.Editor
{
    public class AcousticsProbesRenderer : AcousticsActualRenderer
    {
        // This is the SimConfig pointer
        private IntPtr m_previewData;
        private List<Vector3> m_probePoints = new List<Vector3>();

        // Class that draws the gizmos showing the acoustic probe locations.
        public override void Render()
        {
            if (m_previewData == IntPtr.Zero)
            {
                return;
            }

            // Cache the probes once, then use the cached data to draw them every frame
            if (m_probePoints.Count == 0)
            {
                int numProbes = 0;
                if (!SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_GetProbeCount(m_previewData, out numProbes))
                {
                    return;
                }

                AcousticsPALPublic.TritonVec3f curLocation = new AcousticsPALPublic.TritonVec3f();
                for (int curProbe = 0; curProbe < numProbes; curProbe++)
                {
                    if (!SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_GetProbePoint(m_previewData, curProbe, ref curLocation))
                    {
                        continue;
                    }

                    m_probePoints.Add(AcousticsEditor.TritonToWorld(new Vector3(curLocation.x, curLocation.y, curLocation.z)));
                }
            }

            // Now draw all the probes
            Gizmos.color = Color.cyan;
            Vector3 cubeSize = new Vector3(0.2f, 0.2f, 0.2f);
            for (int i = 0; i < m_probePoints.Count; i++)
            {
                Gizmos.DrawCube(m_probePoints[i], cubeSize);
            }
        }

        public void SetPreviewData(IntPtr results)
        {
            m_previewData = results;
            m_probePoints.Clear();
        }
    }
}
