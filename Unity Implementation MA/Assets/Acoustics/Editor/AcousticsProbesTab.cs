using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Microsoft.Cloud.Acoustics;
using UnityEngine.AI;
using NUnit.Framework.Internal.Execution;
using System.Runtime.InteropServices;

namespace Microsoft.Acoustics.Editor
{
    public class AcousticsProbesTab : ScriptableObject
    {
        private struct CalcProbeParams
        {
            public IntPtr AcousticMesh;
            public TritonSimulationParameters SimParams;
            public TritonOperationalParameters OpParams;
            public IntPtr Matlib;
        };

        /// Properties related to the probe preview
        // PreviewResults contains the voxelization and probe data from Triton
        IntPtr m_previewResults;
        public bool HasPreviewResults { get { return m_previewResults != IntPtr.Zero; } }
        public void SetPreviewResults(IntPtr newPreviewResults)
        {
            m_previewResults = newPreviewResults;
        }
        public void ClearPreviewResults()
        {
            SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_Destroy(m_previewResults);
            m_previewResults = IntPtr.Zero;
        }

        public int NumProbes 
        { 
            get 
            { 
                if (m_previewResults == IntPtr.Zero)
                {
                    return 0;
                }
                int count = 0;
                if (SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_GetProbeCount(m_previewResults, out count))
                {
                    return count;
                }
                return 0;
            } 
        }
        bool m_previewCancelRequested = false;

        // PreviewRootObject handles the Unity voxels and probes
        GameObject m_previewRootObject;
        public bool PreviewShowing { get { return m_previewRootObject != null; } }

        /// Bake properties
        private TimeSpan m_estimatedTotalComputeTime = TimeSpan.Zero;
        public TimeSpan EstimatedTotalComputeTime { get { return m_estimatedTotalComputeTime; } }
        string m_computeTimeCostSheet = null;

        /// Status properties
        string m_progressMessage;
        int m_progressValue = 0;
        public int ProgressValue { get { return m_progressValue; } }

        /// Threading properties
        System.Threading.Thread m_workerThread;
        // Used to let our worker thread run an action on the main thread.
        List<Action> m_queuedActions = new List<Action>();

        AcousticsMaterialsTab m_MaterialsTab;

        /// <summary>
        /// Initialize Probes tab, where the pre-bake is managed
        /// </summary>
        /// <param name="materialsTab">We need the material coefficients for the pre-bake </param>
        public void Initialize(AcousticsMaterialsTab materialsTab)
        {
            this.m_MaterialsTab = materialsTab;
        }

        /// <summary>
        /// Based on the probe calculation data and information about the nodes, get the estimated compute time for this scene.
        /// </summary>
        public void UpdateComputeTimeEstimate()
        {
            if (m_previewResults == IntPtr.Zero)
            {
                Debug.Log("No results are available.");
                CleanupPreviewData(false);
                return;
            }

            if (String.IsNullOrEmpty(m_computeTimeCostSheet))
            {
                MonoScript thisScript = MonoScript.FromScriptableObject(this);
                string pathToThisScript = Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript));
                string unityRootPath = Path.GetDirectoryName(Application.dataPath);

                string simFreqName = (AcousticsEditor.s_AcousticsParameters.SimulationMaxFrequency == AcousticsParameters.CoarseSimulationFrequency) ? AcousticsParameters.CoarseSimulationName : AcousticsParameters.FineSimulationName;
                string costSheetFileName = $"{simFreqName}BakeCostSheet.xml";

                string costSheetFile = Path.Combine(unityRootPath, pathToThisScript, costSheetFileName);
                m_computeTimeCostSheet = Path.GetFullPath(costSheetFile); // Normalize the path
            }

            float simulationVolume = (AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.x - AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.x) * // Length
                                     (AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.y - AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.y) * // Width
                                     (AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper.z - AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower.z);  // Height

            SimulationConfiguration simConfig = new SimulationConfiguration()
            {
                Frequency = (int)AcousticsEditor.s_AcousticsParameters.SimulationMaxFrequency,
                ProbeCount = NumProbes,
                ProbeSpacing = AcousticsEditor.s_AcousticsParameters.ProbeHorizontalSpacingMax,
                ReceiverSpacing = AcousticsEditor.s_AcousticsParameters.ReceiverSampleSpacing,
                SimulationVolume = simulationVolume,
            };

            m_estimatedTotalComputeTime = CloudProcessor.EstimateProcessingTime(m_computeTimeCostSheet, simConfig, AcousticsEditor.s_AcousticsParameters.GetPoolConfiguration());
        }

        /// <summary>
        /// Remove the calculated preview data
        /// </summary>
        /// <param name="deleteFiles">If true, delete the data files containing the preview as well.</param>
        public void CleanupPreviewData(bool deleteFiles)
        {
            if (m_previewRootObject != null)
            {
                AcousticsProbesRenderer probeRenderer = ((AcousticsProbesRenderer)m_previewRootObject.GetComponent<AcousticsProbes>()?.ProbesRenderer);
                AcousticsVoxelsRenderer voxelRenderer = ((AcousticsVoxelsRenderer)m_previewRootObject.GetComponent<AcousticsVoxels>()?.VoxelRenderer);

                if (voxelRenderer != null)
                {
                    voxelRenderer.SetPreviewData(IntPtr.Zero);
                    ScriptableObject.DestroyImmediate(voxelRenderer);
                }

                if (probeRenderer != null)
                {
                    probeRenderer?.SetPreviewData(IntPtr.Zero);
                    ScriptableObject.DestroyImmediate(probeRenderer);
                }

                DestroyImmediate(m_previewRootObject);

                m_previewRootObject = null;
            }

            if (deleteFiles)
            {
                // This returns both the vox file and config file. We may get other matches as well.
                string[] assetGuids = AssetDatabase.FindAssets(AcousticsEditor.s_AcousticsParameters.DataFileBaseName);

                foreach (string guid in assetGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    // Make sure we're deleting only the files we want
                    if (Path.GetFileName(assetPath).Equals(AcousticsEditor.s_AcousticsParameters.VoxFilename) ||
                        Path.GetFileName(assetPath).Equals(AcousticsEditor.s_AcousticsParameters.ConfigFilename))
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }
                }
            }
        }

        public static IntPtr CreateMaterialLibrary(Dictionary<string, float> nameAbsorptionDictionary)
        {
            IntPtr instance;

            var materialsUnmanagedSize = Marshal.SizeOf(typeof(TritonAcousticMaterial));
            var materialsUnmanagedPtr = Marshal.AllocHGlobal(materialsUnmanagedSize * nameAbsorptionDictionary.Count);

            try
            {
                int i = 0;
                var acousticMaterial = new TritonAcousticMaterial();
                foreach (KeyValuePair<string, float> entry in nameAbsorptionDictionary)
                {
                    acousticMaterial.MaterialName = entry.Key;
                    acousticMaterial.Absorptivity = entry.Value;

                    // copy into unmanaged memory block
                    var materialPtr = materialsUnmanagedPtr + (i * materialsUnmanagedSize);
                    Marshal.StructureToPtr(acousticMaterial, materialPtr, false);
                    i++;
                }

                if (!AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_CreateFromMaterials(
                    materialsUnmanagedPtr,
                    nameAbsorptionDictionary.Count,
                    out instance))
                {
                    throw new InvalidOperationException("Failed to create new Material Library");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(materialsUnmanagedPtr);
            }

            return instance;
        }

        /// <summary>
        /// Called when the user clicks the Calculate button on the Probes tab. Passes the mesh data to Triton for
        /// voxelization and calculation of probe locations.
        /// </summary>
        /// <param name="showDialog">Whether or not to show dialogs when there is an error</param>
        /// <returns>Whether it successfully kicked off the pre-bake</return>
        public bool CalculateProbePoints(bool showDialog = true)
        {
            // Init our cross-thread communication data
            ClearPreviewResults();
            m_progressValue = 0;
            m_progressMessage = "Converting mesh object vertices...";
            m_previewCancelRequested = false;

            CleanupPreviewData(false);

            // Check if we have a nav mesh and warn if not.
            NavMeshTriangulation triangulatedNavMesh = NavMesh.CalculateTriangulation();
            AcousticsNavigation[] customNavMeshes = GameObject.FindObjectsOfType<AcousticsNavigation>();

            if (triangulatedNavMesh.vertices.Length < 1 && customNavMeshes.Length == 0)
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("NavMesh Required", "You have not created or specified a navigation mesh! Navigation meshes determine how probe " +
                        "points are placed. \n\nYou can create a nav mesh by using Unity's navigation system or by marking objects as Acoustics Navigation in the " +
                        "Objects tab.", "OK");
                }
                return false;
            }

            // AcousticMesh is the object that contains all mesh data (including navigation mesh) for the calculation.
            IntPtr acousticMesh = IntPtr.Zero;
            if (!AcousticMeshNativeMethods.TritonPreprocessor_AcousticMesh_Create(out acousticMesh))
            {
                return false;
            }

            // Populate AcousticMesh with all tagged mesh objects in the scene.
            AcousticsGeometry[] agList = GameObject.FindObjectsOfType<AcousticsGeometry>();

            if (agList.Length < 1)
            {
                Debug.LogError("No game objects have been marked as Acoustics Geometry!");
                return false;
            }

            // Put up the progress bar.
            UpdateProgressBarDuringPreview();

            // We have to create a new material library that will be used for the materials in this scene.
            // Store the mapping between the Unity material names and absorption coefficients that were selected by the user.
            Dictionary<string, float> materialMap = new Dictionary<string, float>();

            foreach (TreeViewItem item in m_MaterialsTab.MaterialsListView.GetRows())
            {
                TritonMaterialsListElement element = (TritonMaterialsListElement)item;

                if (element.MaterialCode == AcousticMaterialNativeMethods.MaterialCodes.Reserved0)
                {
                    // User selected "Custom" for the material
                    materialMap.Add(element.Name, element.Absorptivity);
                }
                else
                {
                    // Get the absorptivity from the list of known materials dropdown's library.
                    TritonAcousticMaterial mat = new TritonAcousticMaterial();
                    AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_GetMaterialInfo(m_MaterialsTab.MaterialsListView.MaterialLibrary,
                        element.MaterialCode,
                        ref mat);
                    materialMap.Add(element.Name, mat.Absorptivity);
                }
            }

            // This is the material library that will be used both for probe layout and for the actual bake.
            IntPtr materialLib = CreateMaterialLibrary(materialMap);

            // Convert all mesh objects that have been tagged.
            foreach (AcousticsGeometry ag in agList)
            {
                MeshFilter mf = ag.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    long materialCode = AcousticMaterialNativeMethods.MaterialCodes.DefaultWallCode;

                    Mesh m = mf.sharedMesh;
                    Renderer r = ag.GetComponent<Renderer>();

                    if (r != null && r.sharedMaterial != null && materialMap.ContainsKey(r.sharedMaterial.name))
                    {
                        AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_GetMaterialCode(materialLib, r.sharedMaterial.name, out materialCode);
                    }

                    if (m != null)
                    {
                        AddToTritonAcousticMesh(mf.transform, acousticMesh, m.vertices, m.triangles, materialCode, false, true);
                    }
                }
            }

            // Convert the nav mesh(es)
            if (triangulatedNavMesh.vertices.Length > 0)
            {
                AddToTritonAcousticMesh(null, acousticMesh, triangulatedNavMesh.vertices, triangulatedNavMesh.indices, AcousticMaterialNativeMethods.MaterialCodes.TritonNavigableArea, true, false);
            }

            foreach (AcousticsNavigation nav in customNavMeshes)
            {
                MeshFilter mf = nav.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    Mesh m = mf.sharedMesh;
                    if (m != null)
                    {
                        AddToTritonAcousticMesh(mf.transform, acousticMesh, m.vertices, m.triangles, AcousticMaterialNativeMethods.MaterialCodes.TritonNavigableArea, true, true);
                    }
                }
            }

            // Create the configuration/specification objects for Triton. For the most part we use defaults.
            TritonProbeSamplingSpecification pss = new TritonProbeSamplingSpecification();
            TritonSimulationRegionSpecification src = new TritonSimulationRegionSpecification();

            src.Large =
                new TritonBoundingBox
                {
                    MinCorner = AcousticsEditor.TritonVec3fFromVector3(AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Lower),
                    MaxCorner = AcousticsEditor.TritonVec3fFromVector3(AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Large_Upper)
                };

            src.Small =
                new TritonBoundingBox
                {
                    MinCorner = AcousticsEditor.TritonVec3fFromVector3(AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Small_Lower),
                    MaxCorner = AcousticsEditor.TritonVec3fFromVector3(AcousticsEditor.s_AcousticsParameters.PerProbeSimulationRegion_Small_Upper)
                };
            
            pss.MaxHorizontalSpacing = AcousticsEditor.s_AcousticsParameters.ProbeHorizontalSpacingMax;
            pss.MinHorizontalSpacing = AcousticsEditor.s_AcousticsParameters.ProbeHorizontalSpacingMin;
            pss.MinHeightAboveGround = AcousticsEditor.s_AcousticsParameters.ProbeMinHeightAboveGround;
            pss.VerticalSpacing = AcousticsEditor.s_AcousticsParameters.ProbeVerticalSpacing;

            // Now, hand the task off to Triton for calculation.
            TritonSimulationParameters simParams = new TritonSimulationParameters
            {
                MeshUnitAdjustment = AcousticsEditor.s_AcousticsParameters.MeshUnitAdjustment,
                SceneScale = AcousticsEditor.s_AcousticsParameters.SceneScale,
                SpeedOfSound = AcousticsEditor.s_AcousticsParameters.SpeedOfSound,
                SimulationFrequency = AcousticsEditor.s_AcousticsParameters.SimulationMaxFrequency,
                ReceiverSampleSpacing = AcousticsEditor.s_AcousticsParameters.ReceiverSampleSpacing,
                ProbeSpacing = pss,
                PerProbeSimulationRegion = src,
                VoxelMapResolution = -1
            };

            string fileOutPrefix = AcousticsEditor.s_AcousticsParameters.DataFileBaseName;

            TritonOperationalParameters opParams = new TritonOperationalParameters
            {
                Prefix = fileOutPrefix,
                DisablePml = AcousticsEditor.s_AcousticsParameters.DisablePml,
                OptimizeVoxelMap = true,
                WorkingDir = AcousticsEditor.s_AcousticsParameters.AcousticsDataFolderEditorOnly
                // All other values are ignored
            };

            CalcProbeParams cpp = new CalcProbeParams
            {
                AcousticMesh = acousticMesh,
                SimParams = simParams,
                OpParams = opParams,
                Matlib = materialLib
            };

            m_workerThread = new System.Threading.Thread(DoCalculateProbes);
            m_workerThread.Start(cpp);

            return true;
        }

        /// <summary>
        /// Calculate the voxels and probe points in a separate thread to prevent blocking the UI.
        /// </summary>
        /// <param name="parms">Must contain a filled in CalcProbeParams object.</param>
        void DoCalculateProbes(object parms)
        {
            CalcProbeParams cpp = (CalcProbeParams)parms;
            IntPtr SimConfig = IntPtr.Zero;

            if (!SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_Create(cpp.AcousticMesh, ref cpp.SimParams, ref cpp.OpParams, cpp.Matlib, false, StatusCallback, out SimConfig))
            {
                m_progressMessage = "Failed to create SimulationConfiguration";
                QueueUIThreadAction(LogMessage);
            }

            // When the calculation is finished run DisplayPreviewResults to clean up and add the output to the scene.
            m_previewResults = SimConfig;
            QueueUIThreadAction(DisplayPreviewResults);

            // Now that we're done, free the pointers in cpp
            AcousticMeshNativeMethods.TritonPreprocessor_AcousticMesh_Destroy(cpp.AcousticMesh);
            AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_Destroy(cpp.Matlib);
        }

        void LogMessage()
        {
            Debug.Log(m_progressMessage);
        }

        /// <summary>
        /// Queue a single action for execution on the UI thread
        /// </summary>
        /// <param name="a">Action that should be queued</param>
        void QueueUIThreadAction(Action a)
        {
            lock (m_queuedActions)
            {
                m_queuedActions.Add(a);
            }
        }

        public void Update()
        {
            List<Action> actionsToPerform = new List<Action>();

            lock (m_queuedActions)
            {
                actionsToPerform.AddRange(m_queuedActions);
                m_queuedActions.Clear();
            }

            foreach (Action actionToPerform in actionsToPerform)
            {
                actionToPerform?.Invoke();
            }
        }

        /// <summary>
        /// Called once the calculations are complete or have been aborted.
        /// </summary>
        public void DisplayPreviewResults()
        {
            m_workerThread = null;

            EditorUtility.ClearProgressBar();

            if (m_previewResults == IntPtr.Zero)
            {
                Debug.Log("No results are available.");
                CleanupPreviewData(false);
                return;
            }

            UpdateComputeTimeEstimate();

            Debug.Log(String.Format("Number of probe points: {0}", NumProbes));

            if (m_previewRootObject == null)
            {
                m_previewRootObject = new GameObject();
                // The mere existence of this component will cause its OnDrawGizmos function to be called.
                m_previewRootObject.AddComponent<AcousticsProbes>().ProbesRenderer = ScriptableObject.CreateInstance<AcousticsProbesRenderer>();
                m_previewRootObject.AddComponent<AcousticsVoxels>().VoxelRenderer = ScriptableObject.CreateInstance<AcousticsVoxelsRenderer>();
                m_previewRootObject.name = "Acoustics Previewer";
                m_previewRootObject.hideFlags = HideFlags.HideInInspector | HideFlags.NotEditable | HideFlags.DontSaveInEditor;
                m_previewRootObject.tag = "EditorOnly";
            }
            // Root object already exists in the scene. No need to recreate it.

            ((AcousticsProbesRenderer)m_previewRootObject.GetComponent<AcousticsProbes>().ProbesRenderer)?.SetPreviewData(m_previewResults);
            ((AcousticsVoxelsRenderer)m_previewRootObject.GetComponent<AcousticsVoxels>().VoxelRenderer)?.SetPreviewData(m_previewResults);
        }

        public void RenderUI()
        {
            float buttonWidth = 115;

            if (PreviewShowing)
            {
                GUILayout.Label("Clear the preview to make changes and recompute.", EditorStyles.boldLabel);
                GUILayout.Label("Use the 'Clear' button below to clear it.", EditorStyles.boldLabel);
                GUILayout.Space(10);
                GUILayout.Label("Use Gizmos menu to show/hide probe points and voxels.", EditorStyles.boldLabel);
            }

            using (new EditorGUI.DisabledScope(PreviewShowing))
            {
                GUILayout.Label("Step Three", EditorStyles.boldLabel);
                GUILayout.Label("Previewing the probe points helps you ensure that your probe locations map to the areas " +
                    "in the scene where the user will travel, as well as evaulating the number of probe points, which affects " +
                    "bake time and cost.\n\nIn addition, you can preview the voxels to see how portals (doors, windows, etc.) " +
                    "might be affected by the simulation resolution.\n\nThe probe points calculated here will be used when " +
                    "you submit your bake.", EditorStyles.wordWrappedLabel);

                GUILayout.Space(20);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Simulation Resolution");

                int selectedOld = (AcousticsEditor.s_AcousticsParameters.SimulationMaxFrequency == AcousticsParameters.CoarseSimulationFrequency) ? 0 : 1;
                string[] optionList = new string[] { AcousticsParameters.CoarseSimulationName + "  ", AcousticsParameters.FineSimulationName + "  " }; // Extra spaces are for better UI layout.

                int selectedNew = GUILayout.SelectionGrid(selectedOld, optionList, 2, EditorStyles.radioButton);
                GUILayout.FlexibleSpace();
                AcousticsEditor.s_AcousticsParameters.SimulationMaxFrequency = (selectedNew == 0) ? AcousticsParameters.CoarseSimulationFrequency : AcousticsParameters.FineSimulationFrequency;
                EditorGUILayout.EndHorizontal();

                if (selectedNew != selectedOld)
                {
                    // Need a new cost sheet
                    m_computeTimeCostSheet = null;
                    UpdateComputeTimeEstimate();
                }

                GUILayout.Space(20);

                string oldDataFolder = AcousticsEditor.s_AcousticsParameters.AcousticsDataFolder;

                EditorGUILayout.BeginHorizontal();
                AcousticsEditor.s_AcousticsParameters.AcousticsDataFolder = EditorGUILayout.TextField(new GUIContent("Acoustics Data Folder", "Enter the path where you want acoustics data stored."), AcousticsEditor.s_AcousticsParameters.AcousticsDataFolder);
                if (GUILayout.Button(new GUIContent("...", "Use file chooser dialog to specify the folder for acoustics files."), GUILayout.Width(25)))
                {
                    string result = EditorUtility.OpenFolderPanel("Acoustics Data Folder", "", "");
                    if (!String.IsNullOrEmpty(result))
                    {
                        AcousticsEditor.s_AcousticsParameters.AcousticsDataFolder = result;
                    }
                    GUI.FocusControl("...");    // Manually move the focus so that the text field will update
                }
                EditorGUILayout.EndHorizontal();

                if (!oldDataFolder.Equals(AcousticsEditor.s_AcousticsParameters.AcousticsDataFolder) && !Directory.Exists(AcousticsEditor.s_AcousticsParameters.AcousticsDataFolderEditorOnly))
                {
                    Directory.CreateDirectory(AcousticsEditor.s_AcousticsParameters.AcousticsDataFolderEditorOnly);
                }

                AcousticsEditor.s_AcousticsParameters.DataFileBaseName = EditorGUILayout.TextField(new GUIContent("Acoustics Files Prefix", "Enter the base filename for acoustics files."), AcousticsEditor.s_AcousticsParameters.DataFileBaseName);

                // Update the serialized copy of this data
                MarkParametersDirty();
            }

            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!PreviewShowing))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(buttonWidth)))
                {
                    CleanupPreviewData(true);
                }
            }

            using (new EditorGUI.DisabledScope(PreviewShowing))
            {
                if (GUILayout.Button("Calculate...", GUILayout.Width(buttonWidth)))
                {
                    CalculateProbePoints();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        void MarkParametersDirty()
        {
            EditorUtility.SetDirty(AcousticsEditor.s_AcousticsParameters);
        }

        /// <summary>
        /// Given the vertices and triangles of a unity mesh, add it to the AcousticMesh object.
        /// </summary>
        /// <param name="transform">Transform object used to convert object-local coordinates to world coordinates.</param>
        /// <param name="acousticMesh">Pointer to AcousticMesh object to add mesh to.</param>
        /// <param name="verticesIn">List of vertices for the mesh.</param>
        /// <param name="trianglesIn">List of triangle indices for the mesh.</param>
        /// <param name="materialCode">The material code (from Triton.MaterialLibrary) that should be assigned to the mesh.</param>
        /// <param name="isNavMesh">If True, the given mesh will be added as a navigation mesh.</param>
        /// <param name="needsTransform">If True, the given mesh needs a local coordinate transformation to Unity world coordinates.</param>
        void AddToTritonAcousticMesh(Transform transform, IntPtr acousticMesh, Vector3[] verticesIn, int[] trianglesIn, long materialCode, bool isNavMesh, bool needsTransform)
        {
            TritonAcousticMeshTriangleInformation[] triangles = new TritonAcousticMeshTriangleInformation[trianglesIn.Length / 3];
            AcousticsPALPublic.TritonVec3f[] vertices;

            if (!needsTransform)
            {
                vertices = Array.ConvertAll<Vector3, AcousticsPALPublic.TritonVec3f>(verticesIn, vIn =>
                {
                    // NavMesh vertices are already in Unity World coordinates
                    Vector4 vInTransformed = AcousticsEditor.WorldToTriton(vIn);

                    return new AcousticsPALPublic.TritonVec3f(vInTransformed.x, vInTransformed.y, vInTransformed.z);
                });
            }
            else
            {
                vertices = Array.ConvertAll<Vector3, AcousticsPALPublic.TritonVec3f>(verticesIn, vIn =>
                {
                    // Other mesh vertices are in local (relative) coordinates. Need to convert to Unity world coordinates.
                    Vector4 vInTransformed = AcousticsEditor.WorldToTriton(transform.TransformPoint(vIn));

                    return new AcousticsPALPublic.TritonVec3f(vInTransformed.x, vInTransformed.y, vInTransformed.z);
                });
            }

            for (int i = 0; i < triangles.Length; i++)
            {
                int startIndex = i * 3;

                TritonAcousticMeshTriangleInformation t = new TritonAcousticMeshTriangleInformation();

                t.Indices.x = trianglesIn[startIndex];
                t.Indices.y = trianglesIn[startIndex + 1];
                t.Indices.z = trianglesIn[startIndex + 2];

                if (!isNavMesh)
                {
                    t.MaterialCode = materialCode;
                }

                triangles[i] = t;
            }

            AcousticMeshNativeMethods.TritonPreprocessor_AcousticMesh_Add(acousticMesh,
                vertices,
                vertices.Length,
                triangles,
                triangles.Length,
                isNavMesh ? MeshType.MeshTypeNavigation : MeshType.MeshTypeGeometry);
        }

        /// <summary>
        /// Called by the Triton code to display progress messages.
        /// </summary>
        /// <param name="message">Progress message to display</param>
        /// <param name="percent">Percent complete (0 - 100)</param>
        /// <returns>True if the calculation should be canceled.</returns>
        bool StatusCallback(string message, int percent)
        {
            bool returnValue = false;

            m_progressMessage = message;
            m_progressValue = percent;

            returnValue = m_previewCancelRequested;

            // Have to do updates on the UI thread.
            QueueUIThreadAction(UpdateProgressBarDuringPreview);

            return returnValue;
        }

        /// <summary>
        /// Method that actually updates the progress bar. Must be called on the UI thread.
        /// </summary>
        void UpdateProgressBarDuringPreview()
        {
            if (m_progressValue < 100)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Calculating Probe Locations", m_progressMessage, (float)m_progressValue / 100.0f))
                {
                    m_previewCancelRequested = true;
                }
            }
            else
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
