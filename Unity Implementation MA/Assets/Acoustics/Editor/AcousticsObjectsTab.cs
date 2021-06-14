using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Microsoft.Acoustics.Editor
{
    public class AcousticsObjectsTab : ScriptableObject
    {
        private enum SelectedSceneFilter
        {
            All = 0,
            MeshRenderers = 1,
            Terrains = 2,
            Geometry = 3,
            Navigation = 4
        };

        private enum HierarchyFilterMode
        {
            All = 0,
            Name = 1,
            Type = 2,
        };

        private SelectedSceneFilter m_currentSceneFilter = SelectedSceneFilter.All;

        private int m_CountMeshes = 0;
        public int CountMeshes { get { return m_CountMeshes; } }
        private int m_CountTerrains = 0;
        public int CountTerrains { get {return m_CountTerrains;} }
        private int m_CountGeometry = 0;
        public int CountGeometry {  get { return m_CountGeometry; } }
        private int m_CountNav = 0;
        public int CountNav {  get { return m_CountNav; } }
        private int m_CountGeometryUnmarked = 0;
        public int CountGeometryUnmarked {  get { return m_CountGeometryUnmarked; } }
        private int m_CountNavigationUnmarked = 0;
        public int CountNavigationUnmarked {  get { return m_CountNavigationUnmarked; } }

        private void ResetObjectCounts()
        {
            m_CountMeshes = 0;
            m_CountTerrains = 0;
            m_CountGeometry = 0;
            m_CountNav = 0;
            m_CountGeometryUnmarked = 0;
            m_CountNavigationUnmarked = 0;
        }

        public void RenderUI()
        {
            GUILayout.Label("Step One", EditorStyles.boldLabel);
            GUILayout.Label("Add the AcousticsGeometry or AcousticsNavigation components to objects using the options below.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(10);

            GUILayout.Label("Scene Filter:");
            string[] optionList = new string[] { "All Objects  ", "Mesh Renderers  ", "Terrains  ", "Acoustics Geometry  ", "Acoustics Navigation  " }; // Extra spaces are for better UI layout

            SelectedSceneFilter oldFilter = m_currentSceneFilter;
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            m_currentSceneFilter = (SelectedSceneFilter)GUILayout.SelectionGrid((int)m_currentSceneFilter, optionList, 2, EditorStyles.radioButton);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            string[] filterTextOptions = { "All {0}", "{0} MeshRenderer", "{0} Terrain", "{0} Acoustics Geometry", "{0} Acoustics Navigation" };
            string filterText = filterTextOptions[(int)m_currentSceneFilter];

            if (m_currentSceneFilter != oldFilter)
            {
                switch (m_currentSceneFilter)
                {
                    case SelectedSceneFilter.All:
                        SetSearchFilter("", HierarchyFilterMode.All);
                        break;

                    case SelectedSceneFilter.MeshRenderers:
                        SetSearchFilter("MeshRenderer", HierarchyFilterMode.Type);
                        break;

                    case SelectedSceneFilter.Terrains:
                        SetSearchFilter("Terrain", HierarchyFilterMode.Type);
                        break;

                    case SelectedSceneFilter.Geometry:
                        SetSearchFilter("AcousticsGeometry", HierarchyFilterMode.Type);
                        break;

                    case SelectedSceneFilter.Navigation:
                        SetSearchFilter("AcousticsNavigation", HierarchyFilterMode.Type);
                        break;
                }
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(10);

            // Get only the MeshRenderer and Terrain components from the selection
            Transform[] currentSelection;
            bool selectionFilter = false;

            if (Selection.objects.Length > 0)
            {
                currentSelection = Selection.GetTransforms(SelectionMode.Deep | SelectionMode.Editable);
                selectionFilter = true;
            }
            else
            {
                currentSelection = GameObject.FindObjectsOfType<Transform>();
                selectionFilter = false;
            }

            int countWithIgnored = currentSelection.Length;
            currentSelection = currentSelection.Where(t => t.gameObject.activeInHierarchy == true && 
                (t.GetComponent<MeshRenderer>() != null || t.GetComponent<Terrain>() != null)).ToArray();
            int countAllItems = currentSelection.Length;

            // Count up all the different things we're interested in about the current selection
            ResetObjectCounts();

            foreach (Transform t in currentSelection) // All items here are either a mesh or terrain
            {
                bool isMesh = (t.GetComponent<MeshRenderer>() != null);
                bool isTerrain = (t.GetComponent<Terrain>() != null);
                bool isGeometry = (t.GetComponent<AcousticsGeometry>() != null);
                bool isNav = (t.GetComponent<AcousticsNavigation>() != null);

                if (isMesh)
                {
                    m_CountMeshes++;
                }

                if (isTerrain)
                {
                    m_CountTerrains++;
                }

                if (isGeometry)
                {
                    m_CountGeometry++;
                }
                else
                {
                    m_CountGeometryUnmarked++;
                }

                if (isNav)
                {
                    m_CountNav++;
                }
                else
                {
                    m_CountNavigationUnmarked++;
                }
            }

            string ignoredMessage = "";
            if (countWithIgnored > countAllItems)
            {
                ignoredMessage = $" ({countWithIgnored - countAllItems} ignored)";
            }

            if (selectionFilter)
            {
                string objectText = String.Format(filterText, "Selected");
                EditorGUILayout.LabelField($"{objectText} Objects{ignoredMessage}:", EditorStyles.boldLabel);
            }
            else
            {
                string objectText = String.Format(filterText, "Scene");
                EditorGUILayout.LabelField($"{objectText} Objects{ignoredMessage}:", EditorStyles.boldLabel);
            }
            EditorGUI.indentLevel += 1;
            EditorGUILayout.LabelField($"Total: {countWithIgnored}, Mesh: {m_CountMeshes}, Terrain: {m_CountTerrains}, Geometry: {m_CountGeometry}, Navigation: {m_CountNav}");
            EditorGUI.indentLevel -= 1;

            GUILayout.Space(10);
            EditorGUILayout.LabelField($"{m_CountMeshes} MeshRenderers and {m_CountTerrains} Terrains", EditorStyles.boldLabel);

            EditorGUI.indentLevel += 1;

            if (countAllItems > 0)
            {
                // Geometry select/unselect
                bool mixedSelection = (m_CountGeometry != 0 && m_CountGeometryUnmarked != 0);

                if (mixedSelection)
                {
                    EditorGUI.showMixedValue = true;
                }

                bool currentValue = (m_CountGeometry > 0);
                bool newValue = EditorGUILayout.Toggle(new GUIContent("Acoustics Geometry", "Mark the selected objects as geometry for the acoustics simulation."), currentValue);

                // If mixed mode is on the toggle always returns false until the checkbox is clicked.
                if ((!mixedSelection && (newValue != currentValue)) || (mixedSelection && newValue))
                {
                    ChangeObjectMarkStatus(currentSelection, newValue, false);
                }

                if (mixedSelection)
                {
                    EditorGUI.showMixedValue = false;
                }

                // Navigation select/unselect
                mixedSelection = (m_CountNav != 0 && m_CountNavigationUnmarked != 0);

                if (mixedSelection)
                {
                    EditorGUI.showMixedValue = true;
                }

                currentValue = (m_CountNav > 0);
                newValue = EditorGUILayout.Toggle(new GUIContent("Acoustics Navigation", "Mark the selected objects as navigable to be used during probe point layout."), currentValue);

                // If mixed mode is on the toggle always returns false until the checkbox is clicked.
                if ((!mixedSelection && (newValue != currentValue)) || (mixedSelection && newValue))
                {
                    ChangeObjectMarkStatus(currentSelection, newValue, true);
                }

                if (mixedSelection)
                {
                    EditorGUI.showMixedValue = false;
                }
                EditorGUI.indentLevel -= 1;
            }

            GUILayout.Space(20);

            if (!selectionFilter || countAllItems == 0)
            {
                if (selectionFilter && countWithIgnored > 0)
                {
                    EditorGUILayout.HelpBox("Only inactive or non-mesh/terrain objects are selected. Please select one or more active mesh or terrain objects using the hierarchy window.", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Please select one or more active mesh or terrain objects using the hierarchy window. Use the scene filters above to filter for relevant objects.", MessageType.None);
                }
                return;
            }
        }


        /// <summary>
        /// Set the filter in the hierarchy window. Uses reflection to find a non-public method.
        /// </summary>
        /// <param name="filter">Text to use as filter</param>
        /// <param name="filterMode">What kind of filter should be applied</param>
        private void SetSearchFilter(string filter, HierarchyFilterMode filterMode)
        {
            SearchableEditorWindow hierarchyWindow = null;
            SearchableEditorWindow[] windows = Resources.FindObjectsOfTypeAll<SearchableEditorWindow>();

            foreach (SearchableEditorWindow window in windows)
            {
                if (window.GetType().ToString() == "UnityEditor.SceneHierarchyWindow")
                {
                    hierarchyWindow = window;
                    break;
                }
            }

            if (hierarchyWindow != null)
            {
                MethodInfo setSearchType = typeof(SearchableEditorWindow).GetMethod("SetSearchFilter", BindingFlags.NonPublic | BindingFlags.Instance);
                ParameterInfo[] param_info = setSearchType.GetParameters();
                object[] parameters;
                if (param_info.Length == 3)
                {
                    // (string searchFilter, SearchMode mode, bool setAll)
                    parameters = new object[] { filter, (int)filterMode, false };
                }
                else if (param_info.Length == 4)
                {
                    // (string searchFilter, SearchMode mode, bool setAll, bool delayed)
                    parameters = new object[] { filter, (int)filterMode, false, false };
                }
                else
                {
                    Debug.LogError("Error calling SetSearchFilter");
                    return;
                }
                setSearchType?.Invoke(hierarchyWindow, parameters);
            }
        }


        /// <summary>
        /// Given a list of objects, will mark or unmark them for inclusion in acoustics or as an acoustics navmesh.
        /// </summary>
        /// <param name="Objects">List of objects to mark or unmark</param>
        /// <param name="SetMark">If true, then mark them, otherwise unmark them.</param>
        /// <param name="ForNavMesh">If true, mark or unmark the navmesh component. Otherwise, mark or unmark the acoustics component.</param>
        public void ChangeObjectMarkStatus(Transform[] Objects, bool SetMark, bool ForNavMesh)
        {
            if (SetMark)
            {
                Array.ForEach<Transform>(Objects, obj => {
                    if ((obj.GetComponent<MeshRenderer>() != null || obj.GetComponent<Terrain>() != null))
                    {
                        if (!ForNavMesh && obj.GetComponent<AcousticsGeometry>() == null)
                        {
                            obj.gameObject.AddComponent<AcousticsGeometry>();
                        }
                        else if (ForNavMesh && obj.GetComponent<AcousticsNavigation>() == null)
                        {
                            obj.gameObject.AddComponent<AcousticsNavigation>();
                        }
                    }
                });
            }
            else if (!ForNavMesh)
            {
                Array.ForEach<Transform>(Objects, obj => {
                    AcousticsGeometry c = obj.GetComponent<AcousticsGeometry>();
                    if (c != null)
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                });
            }
            else
            {
                Array.ForEach<Transform>(Objects, obj => {
                    AcousticsNavigation c = obj.GetComponent<AcousticsNavigation>();
                    if (c != null)
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                });
            }
        }
    }
}