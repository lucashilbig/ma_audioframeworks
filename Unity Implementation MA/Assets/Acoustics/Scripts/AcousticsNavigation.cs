// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Microsoft.Acoustics
{
    [DisallowMultipleComponent]
    public class AcousticsNavigation : MonoBehaviour
    {
        // This component has no content. Its presence on a GameObject indicates
        // that the object should be used as a navigation mesh when determining probe point layout.

        [PostProcessScene]
        public static void DestroyOnBuild()
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                foreach (var nav in FindObjectsOfType<AcousticsNavigation>())
                {
                    DestroyImmediate(nav);
                }
            }
        }
    }
}

#endif  // UNITY_EDITOR