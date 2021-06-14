// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

#if UNITY_EDITOR && !NET_4_6
#error Microsoft Project Acoustics plugin requires "Scripting Runtime Version" and "API Compatibility Level" under Edit/Project Settings/Player set to ".NET 4.x". Please change the settings and restart the editor.
#endif

namespace Microsoft.Acoustics
{
    [DisallowMultipleComponent]
    public class AcousticsGeometry : MonoBehaviour
    {
        // This component has no content. Its presence on a GameObject indicates
        // that the object should be used in cloud acoustics calculations.

        [PostProcessScene]
        public static void DestroyOnBuild()
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                foreach (var geo in FindObjectsOfType<AcousticsGeometry>())
                {
                    DestroyImmediate(geo);
                }
            }
        }
    }
}

#endif  // UNITY_EDITOR