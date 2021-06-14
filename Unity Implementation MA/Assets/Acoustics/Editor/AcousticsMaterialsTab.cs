using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Microsoft.Acoustics.Editor
{
    public class AcousticsMaterialsTab : ScriptableObject
    {
        public enum AcousticMaterialSortOrder
        {
            ByName = 0,
            ByAbsorptivity = 1
        };

        private TritonMaterialsListView m_listView;
        public TritonMaterialsListView MaterialsListView { get { return m_listView; } }
        // State related fields.
        [SerializeField]
        TreeViewState m_listViewState;
        [SerializeField]
        MultiColumnHeaderState m_multiColumnHeaderState;

        private AcousticMaterialSortOrder m_currentMaterialSortOrder = AcousticMaterialSortOrder.ByName;
        public AcousticMaterialSortOrder MaterialSortOrder { set { m_currentMaterialSortOrder = value; } }

        public void Initialize(List<TritonMaterialsListElement> materialsList)
        {
            if (m_listView != null)
            {
                throw new Exception("Already initialized");
            }

            MonoScript thisScript = MonoScript.FromScriptableObject(this);
            string pathToThisScript = Path.GetDirectoryName(AssetDatabase.GetAssetPath(thisScript));
            string unityRootPath = Path.GetDirectoryName(Application.dataPath);

            string materialsPropertiesFile = Path.Combine(unityRootPath, pathToThisScript, @"DefaultMaterialProperties.json");
            materialsPropertiesFile = Path.GetFullPath(materialsPropertiesFile); // Normalize the path

            IntPtr materialLibrary;
            bool created = AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_CreateFromFile(materialsPropertiesFile, out materialLibrary);

            if (!created || materialLibrary == IntPtr.Zero)
            {
                Debug.Log(String.Format("Failed to load default material properties from {0}!", materialsPropertiesFile));
                return;
            }
            else
            {
                int count = 0;
                AcousticMaterialNativeMethods.TritonPreprocessor_MaterialLibrary_GetCount(materialLibrary, out count);
                Debug.Log(String.Format("Default materials loaded. Number of entries in material library: {0}", count));
            }

            if (m_listViewState == null)
            {
                m_listViewState = new TreeViewState();
            }

            MultiColumnHeaderState columnHeaderState = TritonMaterialsListView.CreateDefaultMultiColumnHeaderState(EditorGUIUtility.currentViewWidth);

            bool firstInit = m_multiColumnHeaderState == null;

            if (MultiColumnHeaderState.CanOverwriteSerializedFields(m_multiColumnHeaderState, columnHeaderState))
            {
                MultiColumnHeaderState.OverwriteSerializedFields(m_multiColumnHeaderState, columnHeaderState);
            }

            m_multiColumnHeaderState = columnHeaderState;

            MultiColumnHeader multiColumnHeader = new MultiColumnHeader(columnHeaderState);
            if (firstInit)
            {
                multiColumnHeader.ResizeToFit();
                multiColumnHeader.canSort = false;
                multiColumnHeader.height = MultiColumnHeader.DefaultGUI.minimumHeight;
            }

            m_listView = new TritonMaterialsListView(m_listViewState, multiColumnHeader, materialLibrary, materialsList);
        }

        public void RenderUI()
        {
            if (m_listView == null)
            {
                // Can't do anything if the listview failed to load.
                EditorGUILayout.LabelField("ERROR: Failed to load materials data.");
                return;
            }

            GUILayout.Label("Step Two", EditorStyles.boldLabel);
            GUILayout.Label("Assign acoustic properties to each scene material using the dropdown. " +
                "Different materials can have a dramatic effect on the results of the bake. " +
                "Choose \"Custom\" to set the absorption coefficient directly.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(10);

            GUILayout.Label("Click the scene material name to select the objects which use that material.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(20);

            m_listView.FilterUnmarked = EditorGUILayout.Toggle(new GUIContent("Show Marked Only", "When checked, only materials on objects marked as Acoustics Geometry will be listed"), m_listView.FilterUnmarked);

            string[] optionList = new string[] { "Name  ", "Absorptivity" };
            GUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Sort Acoustics By:", "Use this to choose the sort order for the list of known acoustic materials"));
            m_currentMaterialSortOrder = (AcousticMaterialSortOrder)GUILayout.SelectionGrid((int)m_currentMaterialSortOrder, optionList, 2, EditorStyles.radioButton);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            m_listView.SortKnownMaterialsByAbsorption = (m_currentMaterialSortOrder == AcousticMaterialSortOrder.ByAbsorptivity);

            Rect rect = GUILayoutUtility.GetRect(200f, 200f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            m_listView.OnGUI(rect);
        }
    }
}