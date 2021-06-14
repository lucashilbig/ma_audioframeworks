// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Threading;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Microsoft.Cloud.Acoustics;
using Microsoft.Azure.Batch.Auth;
using Microsoft.Azure.Batch;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Acoustics.Editor
{
    public class AcousticsEditor : EditorWindow, IHasCustomMenu
    {
        public enum SelectedTab
        {
            ObjectTab,
            MaterialsTab,
            ProbesTab,
            BakeTab
        };

        const string AzurePortalUrl = "https://portal.azure.com";
        const string AzureAccountInfoKeyName = "AcousticsAzureAccounts";

        const string UntitledString = "Untitled";
        const string DefaultAcousticParameterBasePath = "Assets/Editor";
        const string AcousticParametersSuffix = "_AcousticParameters";

        [SerializeField]
        public static AcousticsParameters s_AcousticsParameters;

        private SelectedTab m_currentTab = SelectedTab.ObjectTab;
        public SelectedTab CurrentTab { get { return m_currentTab; } set { m_currentTab = value; } }

        private AcousticsObjectsTab m_ObjectsTab;
        public AcousticsObjectsTab ObjectsTab { get { return m_ObjectsTab; } }
        private AcousticsMaterialsTab m_MaterialsTab;
        public AcousticsMaterialsTab MaterialsTab { get { return m_MaterialsTab; } }
        private AcousticsProbesTab m_ProbesTab;
        public AcousticsProbesTab ProbesTab { get { return m_ProbesTab; } }

        static bool s_staticInitialized = false;
        bool m_initialized = false;

        bool m_bakeCredsFoldoutOpen = true;

        GUIStyle m_leftStyle;
        GUIStyle m_rightStyle;
        GUIStyle m_midStyle;
        GUIStyle m_leftSelected;
        GUIStyle m_rightSelected;
        GUIStyle m_midSelected;

        System.Threading.Thread m_workerThread;

        // Used to let our worker thread run an action on the main thread.
        List<Action> m_queuedActions = new List<Action>();

        string m_progressMessage;

        // Mesh conversion constants.
        static Matrix4x4 s_tritonToWorld;
        static Matrix4x4 s_worldToTriton;

        // Fields related to cloud azure service
        const double AzureBakeCheckInterval = 30; // Interval in seconds to check status on cloud bake
        string m_cloudJobStatus;
        DateTime m_cloudLastUpdateTime;
        double m_timerStartValue;
        bool m_cloudJobDeleting = false;

        /// <summary>
        /// Top level menu item to bring up our UI
        /// </summary>
        [MenuItem("Window/Acoustics")]
        public static EditorWindow ShowWindow()
        {
            EditorWindow win = EditorWindow.GetWindow(typeof(AcousticsEditor));
            win.titleContent = new GUIContent("Acoustics", "Configuration of Triton Environmental Acoustics");
            win.Show();
            return win;
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("About Project Acoustics..."), false, () => {
                AcousticsAbout aboutWindow = ScriptableObject.CreateInstance<AcousticsAbout>();
                aboutWindow.position = new Rect(Screen.width / 2, Screen.height / 2, 1024, 768);
                aboutWindow.titleContent = new GUIContent("About Project Acoustics");
                aboutWindow.ShowUtility();
            });
        }

        /// <summary>
        /// Repaint our window if the hierarchy changes and we're showing the object window, which displays a count of marked objects.
        /// This is primarily to catch the case of someone adding the AcousticsGeometry or AcousticsNavigation components to an object, but would also apply to
        /// adding a prefab that has AcousticsGeometry objects, and so on.
        /// </summary>
        void OnHierarchyChange()
        {
            bool doRepaint = false;

            if (m_currentTab == SelectedTab.ObjectTab)
            {
                doRepaint = true;
            }

            if (m_currentTab == SelectedTab.MaterialsTab && m_MaterialsTab?.MaterialsListView != null)
            {
                m_MaterialsTab.MaterialsListView.Reload();
                doRepaint = true;
            }

            if (doRepaint)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Called whenever the selection changes in the scene.
        /// </summary>
        private void OnSelectionChange()
        {
            if (m_currentTab != SelectedTab.BakeTab)
            {
                Repaint();
            }
        }

        /// <summary>
        /// Called automatically several times a second by Unity
        /// </summary>
        private void Update()
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

            if (m_timerStartValue != 0 && EditorApplication.timeSinceStartup > m_timerStartValue + AzureBakeCheckInterval)
            {
                m_timerStartValue = EditorApplication.timeSinceStartup; // Must be done before call below
                StartAzureStatusCheck();
            }
            if (m_ProbesTab != null)
            {
                // Probes tab has some UI actions to complete also
                m_ProbesTab.Update();
            }
        }

        /// <summary>
        /// Callback that tells us when the active scene has changed. 
        /// We want to reload the acoustic parameters that belong to the active scene.
        /// </summary>
        /// <param name="state"></param>
        private void SceneChanged(Scene current, Scene next)
        {
            // Save current acoustic parameters before we change
            AssetDatabase.SaveAssets();

            // Remove all preview elements when switching scenes
            m_ProbesTab?.CleanupPreviewData(false);

            LoadAcousticParameters();
            AssetDatabase.Refresh();

            // Force a refresh of our data
            m_initialized = false;
            Repaint();
        }

        /// <summary>
        /// Callback that tells us when a scene has been saved (any scene, not necessarily the active one)
        /// We want to reload the acoustic parameters if an untitled scene was renamed
        /// </summary>
        /// <param name="scene"></param>
        private void SceneSaved(Scene scene)
        {
            // We don't care about scenes that aren't the active one
            if (scene != SceneManager.GetActiveScene())
            {
                return;
            }

            // Save current acoustic parameters before we change anything
            AssetDatabase.SaveAssets();

            // We are trying to catch the case of an untitled scene being renamed
            // This also has the side effect of creating new acoustic parameters for any scene that was renamed
            string newSceneAssetName = CreateAcousticParameterAssetString(scene.name);
            if (newSceneAssetName != s_AcousticsParameters.name)
            {
                LoadAcousticParameters();
                AssetDatabase.Refresh();

                // Force a refresh of our data
                m_initialized = false;
                Repaint();
            }
        }

        /// <summary>
        /// Called when the Window is opened / loaded, and when entering/exiting play mode.
        /// </summary>
        private void OnEnable()
        {
            m_cloudLastUpdateTime = DateTime.Now;

            InitializeStatic(); // Normally we would do this in Awake, but there are a few scenarios where we don't get an Awake call.
                                // Can't call Initialize() here because the window is not yet visible, and properties such as its size are not yet available.

            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode += SceneChanged;
            EditorSceneManager.sceneSaved += SceneSaved;
        }

        /// <summary>
        /// Called when the window is closed / unloaded, and when entering/exiting play mode.
        /// </summary>
        private void OnDisable()
        {
            AssetDatabase.SaveAssets();

            // The probe point preview is not preserved when we're disabled, so we need to force a reload when we are reactivated.
            m_ProbesTab?.CleanupPreviewData(false);
            m_ProbesTab?.ClearPreviewResults();
            m_initialized = false;

            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= SceneChanged;
            EditorSceneManager.sceneSaved -= SceneSaved;

            DestroyImmediate(m_ObjectsTab);
            DestroyImmediate(m_MaterialsTab);
            DestroyImmediate(m_ProbesTab);
        }

        /// <summary>
        /// Callback that tells us when we enter and exit Play or Edit mode. We use this to ensure we are properly initialized
        /// when re-entering Edit mode after Play mode.
        /// </summary>
        /// <param name="state"></param>
        void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // We don't get a call to update anything in this case. Force a refresh of our data.
                m_initialized = false;
                Repaint();
            }
            else if (state == PlayModeStateChange.ExitingEditMode)
            {
                m_ProbesTab?.CleanupPreviewData(false);
            }
        } 

        /// <summary>
        /// Called to render our UI
        /// </summary>
        void OnGUI()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // We don't do anything in play mode
                GUILayout.Space(30);
                EditorGUILayout.LabelField("Acoustics editor disabled in Play Mode.", EditorStyles.boldLabel);
                return;
            }

            EditorGUIUtility.wideMode = true;

            Initialize();

            RenderTabButtons();

            switch (m_currentTab)
            {
                case SelectedTab.ObjectTab:
                    RenderObjectsTab();
                    break;

                case SelectedTab.MaterialsTab:
                    RenderMaterialsTab();
                    break;

                case SelectedTab.ProbesTab:
                    RenderProbesTab();
                    break;

                case SelectedTab.BakeTab:
                    RenderBakeTab();
                    break;
            }
        }

        static void InitializeStatic()
        {
            if (!s_staticInitialized)
            {
                // Save off the transforms once. This is converting from unity's default space to Maya Z+
                Matrix4x4 unityToMayaZ = new Matrix4x4();
                unityToMayaZ.SetRow(0, new Vector4(1, 0, 0, 0));
                unityToMayaZ.SetRow(1, new Vector4(0, 0, 1, 0));
                unityToMayaZ.SetRow(2, new Vector4(0, 1, 0, 0));
                unityToMayaZ.SetRow(3, new Vector4(0, 0, 0, 1));

                s_worldToTriton = unityToMayaZ;
                s_tritonToWorld = Matrix4x4.Inverse(s_worldToTriton);

                if (s_AcousticsParameters == null)
                {
                    LoadAcousticParameters();
                }

                s_staticInitialized = true;
            }
        }

        public void Initialize()
        {
            if (!m_initialized)
            {
                m_ObjectsTab = CreateInstance<AcousticsObjectsTab>();
                m_MaterialsTab = CreateInstance<AcousticsMaterialsTab>();
                m_MaterialsTab.Initialize(s_AcousticsParameters.MaterialsListElements);
                m_ProbesTab = CreateInstance<AcousticsProbesTab>();
                m_ProbesTab.Initialize(m_MaterialsTab);
                s_AcousticsParameters.ListView = m_MaterialsTab.MaterialsListView;
                s_AcousticsParameters.ListView.OnDataChanged += MarkParametersDirty;

                if (String.IsNullOrWhiteSpace(s_AcousticsParameters.DataFileBaseName))
                {
                    s_AcousticsParameters.DataFileBaseName = "Acoustics_" + SceneManager.GetActiveScene().name;
                }

                if (String.IsNullOrWhiteSpace(s_AcousticsParameters.AcousticsDataFolder))
                {
                    s_AcousticsParameters.AcousticsDataFolder = Path.Combine("Assets", AcousticsParameters.DefaultDataFolder);
                }

                if (!Directory.Exists(s_AcousticsParameters.AcousticsDataFolder))
                {
                    Directory.CreateDirectory(s_AcousticsParameters.AcousticsDataFolder);
                }

                // We also put data in an Editor subfolder under the AcousticsDataFolder so unnecessary data doesn't get packaged with the game
                if (!Directory.Exists(s_AcousticsParameters.AcousticsDataFolderEditorOnly))
                {
                    Directory.CreateDirectory(s_AcousticsParameters.AcousticsDataFolderEditorOnly);
                }

                // Pre-compute the various styles we will be using.
                m_leftStyle = new GUIStyle(EditorStyles.miniButtonLeft)
                {
                    fontSize = 11
                };
                m_rightStyle = new GUIStyle(EditorStyles.miniButtonRight)
                {
                    fontSize = 11
                };
                m_midStyle = new GUIStyle(EditorStyles.miniButtonMid)
                {
                    fontSize = 11
                };
                // For some reason the different miniButton styles don't have the same values for top margin.
                m_leftStyle.margin.top = 0;
                m_midStyle.margin.top = 0;
                m_rightStyle.margin.top = 0;

                m_leftSelected = new GUIStyle(m_leftStyle)
                {
                    normal = m_leftStyle.active,
                    onNormal = m_leftStyle.active
                };
                m_midSelected = new GUIStyle(m_midStyle)
                {
                    normal = m_midStyle.active,
                    onNormal = m_midStyle.active
                };
                m_rightSelected = new GUIStyle(m_rightStyle)
                {
                    normal = m_rightStyle.active,
                    onNormal = m_rightStyle.active
                };
                m_leftSelected.normal.textColor = Color.white;
                m_midSelected.normal.textColor = Color.white;
                m_rightSelected.normal.textColor = Color.white;

                // Attempt to load Azure account info from the registry
                string azureCreds = EditorPrefs.GetString(AzureAccountInfoKeyName);

                if (!String.IsNullOrEmpty(azureCreds))
                {
                    // decrypt creds
                    byte[] azureCredsBytes = Convert.FromBase64String(azureCreds);
                    byte[] azureCredsBytesUnprotected = System.Security.Cryptography.ProtectedData.Unprotect(azureCredsBytes, null, DataProtectionScope.CurrentUser);

                    string azureCredsUnprotected = System.Text.Encoding.Unicode.GetString(azureCredsBytesUnprotected);
                    EditorJsonUtility.FromJsonOverwrite(azureCredsUnprotected, s_AcousticsParameters.AzureAccounts);

                    // We may have stored empty creds. Only fold if something is there.
                    if (!String.IsNullOrEmpty(s_AcousticsParameters.AzureAccounts.BatchAccountName))
                    {
                        m_bakeCredsFoldoutOpen = false;
                    }
                }

                // If a preview exists, load it.
                if (File.Exists(Path.Combine(s_AcousticsParameters.AcousticsDataFolderEditorOnly, s_AcousticsParameters.DataFileBaseName + "_config.xml")))
                {
                    IntPtr simConfig = IntPtr.Zero;
                    if (SimulationConfigNativeMethods.TritonPreprocessor_SimulationConfiguration_CreateFromFile(s_AcousticsParameters.AcousticsDataFolderEditorOnly, s_AcousticsParameters.DataFileBaseName + @"_config.xml", out simConfig))
                    {
                        m_ProbesTab.SetPreviewResults(simConfig);
                        m_ProbesTab.DisplayPreviewResults();
                    }
                    else
                    {
                        // Ignore.
                        Debug.Log($"Attempt to load preview data failed, so ignoring.");
                    }
                }
                else
                {
                    // Otherwise, remove it
                    m_ProbesTab.ClearPreviewResults();
                }

                if (!String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    m_cloudJobStatus = "Checking status...";
                    StartAzureCheckTimer();
                    StartAzureStatusCheck();
                }
                else
                {
                    m_cloudJobStatus = "";
                }

                m_initialized = true;
            }
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

        /// <summary>
        /// Queue two actions for execution on the UI thread. Use this vs. calling QueueAction(a) twice.
        /// </summary>
        /// <param name="a1">First action to queue</param>
        /// <param name="a2">Second action to queue</param>
        void QueueUIThreadAction(Action a1, Action a2)
        {
            lock (m_queuedActions)
            {
                m_queuedActions.Add(a1);
                m_queuedActions.Add(a2);
            }
        }

        static void LoadAcousticParameters()
        {
            string acousticParametersAssetpath = GetAcousticsParametersAssetPath();

            // See if there already are saved acoustic parameters for the active scene
            s_AcousticsParameters = AssetDatabase.LoadAssetAtPath(acousticParametersAssetpath, typeof(AcousticsParameters)) as AcousticsParameters;

            // Create new one if it doesn't already exist
            if (s_AcousticsParameters == null)
            {
                s_AcousticsParameters = CreateInstance<AcousticsParameters>();

                AssetDatabase.CreateAsset(s_AcousticsParameters, acousticParametersAssetpath);
                AssetDatabase.SaveAssets();
            }
        }

        private static string CreateAcousticParameterAssetString(string sceneName)
        {
            return sceneName + AcousticParametersSuffix;
        }

        /// <summary>
        /// Returns the path for the saved AcousticParameters asset file for the active scene
        /// </summary>
        /// <returns></returns>
        static string GetAcousticsParametersAssetPath()
        {
            string sceneBaseName = UntitledString;
            string basePath = DefaultAcousticParameterBasePath;

            string scenePath = SceneManager.GetActiveScene().path;
            if (!String.IsNullOrEmpty(scenePath))
            {
                sceneBaseName = Path.GetFileNameWithoutExtension(scenePath);
                basePath = Path.Combine(Path.GetDirectoryName(scenePath), "Editor");
            }

            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            return Path.Combine(basePath, CreateAcousticParameterAssetString(sceneBaseName) + ".asset");
        }

        /// <summary>
        /// Renders the tab selection buttons across the top of our UI.
        /// </summary>
        void RenderTabButtons()
        {
            const float buttonWidth = 65;
            const float buttonHeight = 20;
            const float topBottomMargin = 10;
            const float buttonCount = 4;

            float width = EditorGUIUtility.currentViewWidth;
            float offset = buttonWidth * (buttonCount / 2);
            float center = width / 2;

            // BeginArea doesn't actually affect the layout, so allocate the space we want in the layout
            GUILayout.Space(buttonHeight + (topBottomMargin * 3));

            GUILayout.BeginArea(new Rect(center - offset, topBottomMargin, buttonWidth * buttonCount, buttonHeight + topBottomMargin));

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(new GUIContent("Objects", "Step One: Mark Objects"), (m_currentTab == SelectedTab.ObjectTab) ? m_leftSelected : m_leftStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.ObjectTab;
            }

            if (GUILayout.Button(new GUIContent("Materials", "Step Two: Assign Materials"), (m_currentTab == SelectedTab.MaterialsTab) ? m_midSelected : m_midStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.MaterialsTab;
                if (m_MaterialsTab.MaterialsListView != null)
                {
                    m_MaterialsTab.MaterialsListView.Reload();
                }
            }

            if (GUILayout.Button(new GUIContent("Probes", "Step Three: Calculate Probes"), (m_currentTab == SelectedTab.ProbesTab) ? m_midSelected : m_midStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.ProbesTab;
            }

            if (GUILayout.Button(new GUIContent("Bake", "Step Four: Bake in the cloud"), (m_currentTab == SelectedTab.BakeTab) ? m_rightSelected : m_rightStyle, GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
            {
                m_currentTab = SelectedTab.BakeTab;
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        void RenderObjectsTab()
        {
            if (m_ProbesTab.PreviewShowing)
            {
                GUILayout.Label("Clear the preview on the Probes tab to make changes.", EditorStyles.boldLabel);
                GUILayout.Space(20);
            }

            using (new EditorGUI.DisabledScope(m_ProbesTab.PreviewShowing))
            {
                m_ObjectsTab.RenderUI();
            }
        }

        void RenderMaterialsTab()
        {
            if (m_ProbesTab.PreviewShowing)
            {
                GUILayout.Label("Clear the preview on the Probes tab to make changes.", EditorStyles.boldLabel);
                GUILayout.Space(20);
            }

            using (new EditorGUI.DisabledScope(m_ProbesTab.PreviewShowing))
            {
                m_MaterialsTab.RenderUI();
            }
        }

        void RenderProbesTab()
        {
            m_ProbesTab.RenderUI();
        }

        void RenderBakeTab()
        {
            float buttonWidth = 115;
            Rect tmpRect;
            bool allCredsFilledIn = false;
            bool anyCredsFilledIn = false;

            GUILayout.Label("Step Four", EditorStyles.boldLabel);
            GUILayout.Label("Once you have completed the previous steps then submit the job for baking in the cloud here.\n\n" +
                "Make sure you have created your Azure account according to the instructions.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(20);

            // The returned rect is a dummy rect if calculatingLayout is true, but in this case we don't care - the Foldout updates properly during repaint.
            tmpRect = GUILayoutUtility.GetRect(new GUIContent("Advanced"), GUI.skin.label);

            m_bakeCredsFoldoutOpen = EditorGUI.Foldout(tmpRect, m_bakeCredsFoldoutOpen, "Azure Configuration", true);
            if (m_bakeCredsFoldoutOpen)
            {
                string oldCreds = EditorJsonUtility.ToJson(s_AcousticsParameters.AzureAccounts);

                EditorGUI.indentLevel += 1;

                s_AcousticsParameters.AzureAccounts.BatchAccountName = EditorGUILayout.TextField(new GUIContent("Batch Account Name", "Enter the name of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountName);
                s_AcousticsParameters.AzureAccounts.BatchAccountUrl = EditorGUILayout.TextField(new GUIContent("Batch Account URL", "Enter the url of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountUrl);
                s_AcousticsParameters.AzureAccounts.BatchAccountKey = EditorGUILayout.PasswordField(new GUIContent("Batch Account Key", "Enter the key of your Azure Batch account here."), s_AcousticsParameters.AzureAccounts.BatchAccountKey);

                GUILayout.Space(10);

                s_AcousticsParameters.AzureAccounts.StorageAccountName = EditorGUILayout.TextField(new GUIContent("Storage Account Name", "Enter the name of the associated Azure Storage account here."), s_AcousticsParameters.AzureAccounts.StorageAccountName);
                s_AcousticsParameters.AzureAccounts.StorageAccountKey = EditorGUILayout.PasswordField(new GUIContent("Storage Account Key", "Enter the key of the associated Azure Storage account here."), s_AcousticsParameters.AzureAccounts.StorageAccountKey);

                GUILayout.Space(10);

                if (s_AcousticsParameters.TritonImage == "")
                {
                    s_AcousticsParameters.TritonImage = AcousticsParameters.DefaultTritonImage;
                }
                s_AcousticsParameters.TritonImage = EditorGUILayout.TextField(new GUIContent("Toolset Version", "Enter a docker image tag for the bake tools to use. To reset, clear this field"), s_AcousticsParameters.TritonImage);

                // If using a custom triton image, specify ACR information
                if (s_AcousticsParameters.TritonImage != AcousticsParameters.DefaultTritonImage)
                {
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer = EditorGUILayout.TextField(new GUIContent("Azure Container Registry Server", "Enter the server URL of the ACR hosting the docker image."), s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer);
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryAccount = EditorGUILayout.TextField(new GUIContent("Azure Container Registry Account", "Enter the account name of the ACR hosting the docker image for authentication."), s_AcousticsParameters.AzureAccounts.AzureContainerRegistryAccount);
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryKey = EditorGUILayout.PasswordField(new GUIContent("Azure Container Registry Key", "Enter the key of the ACR hosting the docker image for authentication."), s_AcousticsParameters.AzureAccounts.AzureContainerRegistryKey);
                }
                else
                {
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer = null;
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryAccount = null;
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryKey = null;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 20);
                GUIContent buttonContent = new GUIContent("Launch Azure Portal", AzurePortalUrl);
                GUIStyle style = GUI.skin.button;
                Vector2 contentSize = style.CalcSize(buttonContent);
                if (GUILayout.Button(buttonContent, GUILayout.Width(contentSize.x)))
                {
                    Application.OpenURL(AzurePortalUrl);
                }
                GUILayout.EndHorizontal();

                EditorGUI.indentLevel -= 1;

                allCredsFilledIn = AreAllAzureCredentialsFilledIn();

                anyCredsFilledIn = (!String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountName) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountKey) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountUrl) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountName) ||
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountKey));

                // Update stored info if necessary.
                string newCreds = EditorJsonUtility.ToJson(s_AcousticsParameters.AzureAccounts);

                if (!newCreds.Equals(oldCreds))
                {
                    if (anyCredsFilledIn)
                    {
                        // encrypt creds before saving
                        byte[] newCredsBytes = System.Text.Encoding.Unicode.GetBytes(newCreds);
                        byte[] newCredsBytesProtected = System.Security.Cryptography.ProtectedData.Protect(newCredsBytes, null, DataProtectionScope.CurrentUser);

                        string newCredsProtected = Convert.ToBase64String(newCredsBytesProtected);

                        EditorPrefs.SetString(AzureAccountInfoKeyName, newCredsProtected);
                    }
                    else if (!allCredsFilledIn && EditorPrefs.HasKey(AzureAccountInfoKeyName))
                    {
                        // User emptied all the fields. Remove the key from the registry.
                        EditorPrefs.DeleteKey(AzureAccountInfoKeyName);
                    }
                }
            }
            else
            {
                allCredsFilledIn = AreAllAzureCredentialsFilledIn();
            }

            GUILayout.Space(10);

            int oldVM = s_AcousticsParameters.SelectedVMType;
            int oldNodeCount = s_AcousticsParameters.NodeCount;

            s_AcousticsParameters.SelectedVMType = EditorGUILayout.Popup("VM Node Type", s_AcousticsParameters.SelectedVMType, s_AcousticsParameters.SupportedAzureVMTypes);
            int newNodeCount = EditorGUILayout.IntField("Node Count", s_AcousticsParameters.NodeCount);
            int previewNodes = m_ProbesTab.HasPreviewResults ? m_ProbesTab.NumProbes : int.MaxValue;

            // Node count needs be between 1 and total number of probes
            s_AcousticsParameters.NodeCount = Math.Min(Math.Max(newNodeCount, 1), previewNodes);
            s_AcousticsParameters.UseLowPriorityNodes = EditorGUILayout.Toggle("Use Low Priority", s_AcousticsParameters.UseLowPriorityNodes);

            if ((s_AcousticsParameters.SelectedVMType != oldVM) || (s_AcousticsParameters.NodeCount != oldNodeCount))
            {
                m_ProbesTab.UpdateComputeTimeEstimate();
            }

            // Update the serialized copy of this data
            MarkParametersDirty();

            if (m_ProbesTab.HasPreviewResults)
            {
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Probe Count", m_ProbesTab.NumProbes.ToString());
                EditorGUILayout.LabelField("Estimated Bake Time", GetFormattedTimeEstimate(m_ProbesTab.EstimatedTotalComputeTime), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Estimated Compute Cost", GetFormattedTimeEstimate(TimeSpan.FromTicks(m_ProbesTab.EstimatedTotalComputeTime.Ticks * s_AcousticsParameters.NodeCount)), EditorStyles.wordWrappedLabel);

                EditorGUI.indentLevel += 1;
                EditorGUILayout.LabelField("NOTE:", "Estimated cost and time are rough estimates only. They do not include pool or node startup and shutdown time. Estimates are not guaranteed.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel -= 1;
            }

            GUILayout.Space(20);
            GUILayout.Label($"The result file will be saved as {s_AcousticsParameters.DataFileBaseName}.ace.bytes in the acoustics data folder. Use the Probes tab to change location or name.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            // Disable "Clear State" button while job deletion is pending OR if there's no active job.
            using (new EditorGUI.DisabledScope(m_cloudJobDeleting || (m_timerStartValue == 0 && String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))))
            {
                if (GUILayout.Button(new GUIContent("Clear State", "Forget about the submitted job and stop checking status."), GUILayout.Width(buttonWidth)))
                {
                    m_cloudJobStatus = "Reset";
                    s_AcousticsParameters.ActiveJobID = null;
                    m_timerStartValue = 0;
                }
            }

            using (new EditorGUI.DisabledScope(m_cloudJobDeleting || (m_workerThread != null && m_workerThread.IsAlive))) // Disable the Bake/Cancel button if there is the job is deleting, or some work is being done (such as submitting or canceling a bake!)
            {
                if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    using (new EditorGUI.DisabledScope(!m_ProbesTab.PreviewShowing)) // Disable Bake if there is no preview
                    {
                        if (GUILayout.Button(new GUIContent("Bake", "Start the bake in the cloud"), GUILayout.Width(buttonWidth)))
                        {
                            if (!allCredsFilledIn)
                            {
                                EditorUtility.DisplayDialog("Acoustics Bake", "One or more Azure credentials fields are not filled in.\nPlease fill in all fields.", "OK");
                            }
                            else if (String.IsNullOrWhiteSpace(s_AcousticsParameters.AcousticsDataFolder) || String.IsNullOrWhiteSpace(s_AcousticsParameters.DataFileBaseName))
                            {
                                EditorUtility.DisplayDialog("Acoustics Bake", "The data folder and/or base filename fields on the Probes tab are not correct.\nPlease fill in all fields.", "OK");
                            }
                            else
                            {
                                StartBake();
                            }
                        }
                    }
                }
                else
                {
                    if (GUILayout.Button(new GUIContent("Cancel Job", "Cancel the currently running bake job"), GUILayout.Width(buttonWidth)))
                    {
                        if (EditorUtility.DisplayDialog("Cancel Azure Job?", "Are you sure you wish to cancel the submitted Azure job? Any calculations done so far will be lost, and you will still be charged for the time used. This cannot be undone.", "Yes - Do It!", "No - Leave It Alone"))
                        {
                            CancelBake();
                        }
                    }
                }
            }

            // Support for local bakes
            using (new EditorGUI.DisabledScope(!m_ProbesTab.PreviewShowing)) // Disable local bake button if there is no preview
            {
                GUIContent localBakeContent = new GUIContent("Prepare Local Bake", "Generate package for local bake");
                GUIStyle localBakeButtonStyle = GUI.skin.button;
                if (GUILayout.Button(localBakeContent, GUILayout.Width(localBakeButtonStyle.CalcSize(localBakeContent).x)))
                {
                    string localPath = EditorUtility.OpenFolderPanel("Select a folder for local bake package", "", "ProjectAcoustics");
                    if (!String.IsNullOrEmpty(localPath))
                    {
                        // Copy simulation input files
                        File.Copy(s_AcousticsParameters.VoxFilepath, Path.Combine(localPath, s_AcousticsParameters.VoxFilename), true);
                        File.Copy(s_AcousticsParameters.ConfigFilepath, Path.Combine(localPath, s_AcousticsParameters.ConfigFilename), true);

                        // Validate Triton Image first
                        if (s_AcousticsParameters.TritonImage == "")
                        {
                            s_AcousticsParameters.TritonImage = AcousticsParameters.DefaultTritonImage;
                        }

                        // Generate Windows batch file for local processing
                        using (StreamWriter writer = new StreamWriter(Path.Combine(localPath, "runlocalbake.bat")))
                        {
                            string command = String.Format(
                                "docker run --rm -w /acoustics/ -v \"%CD%\":/acoustics/working/ {0} ./tools/Triton.LocalProcessor --configfile {1} --workingdir working",
                                s_AcousticsParameters.TritonImage,
                                s_AcousticsParameters.ConfigFilename);
                            writer.WriteLine(command);
                            writer.WriteLine("del *.dat");
                            writer.WriteLine("del *.enc");
                        }

                        // Generate MacOS bash script for local processing
                        using (StreamWriter writer = new StreamWriter(Path.Combine(localPath, "runlocalbake.sh")))
                        {
                            string command = String.Format(
                                "docker run --rm -w /acoustics/ -v \"$PWD\":/acoustics/working/ {0} ./tools/Triton.LocalProcessor --configfile {1} --workingdir working",
                                s_AcousticsParameters.TritonImage,
                                s_AcousticsParameters.ConfigFilename);
                            writer.WriteLine("#!/bin/bash");
                            writer.WriteLine(command);
                            writer.WriteLine("rm *.dat");
                            writer.WriteLine("rm *.enc");
                        }

                        this.ShowNotification(new GUIContent("Files for local bake saved under " + Path.GetFullPath(localPath)));
                    }
                }
            }

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(15);

            GUILayout.Label("Azure Bake Status:", EditorStyles.whiteLabel);
            GUILayout.Label((String.IsNullOrEmpty(m_cloudJobStatus) ? "Not submitted" : m_cloudJobStatus), EditorStyles.boldLabel);
            GUILayout.Label(String.Format("   Last Updated: {0}", m_cloudLastUpdateTime.ToString("g")), EditorStyles.miniLabel);

            if (!String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
            {
                GUILayout.Space(15);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("Job ID:");
                EditorGUILayout.SelectableLabel(s_AcousticsParameters.ActiveJobID);
                EditorGUILayout.EndHorizontal();
            }
        }

        bool AreAllAzureCredentialsFilledIn()
        {
            return (!String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountName) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountKey) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.BatchAccountUrl) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountName) &&
                    !String.IsNullOrWhiteSpace(s_AcousticsParameters.AzureAccounts.StorageAccountKey));
        }

        string GetFormattedTimeEstimate(TimeSpan duration)
        {
            string totalTimeString = "";

            if (duration.Days > 0)
            {
                totalTimeString = String.Format("{0} day{1} ", duration.Days, (duration.Days == 1) ? "" : "s");
            }

            if (duration.Hours > 0 || duration.Days > 0)
            {
                totalTimeString += String.Format("{0} hour{1} ", duration.Hours, (duration.Hours == 1) ? "" : "s");
            }

            totalTimeString += String.Format("{0} minute{1}", duration.Minutes, (duration.Minutes == 1) ? "" : "s");

            return totalTimeString;
        }

        /// <summary>
        /// Used mostly for debugging our worker thread.
        /// </summary>
        void LogMessage()
        {
            Debug.Log(m_progressMessage);
        }

        /// <summary>
        /// Convert from Unity coordinates (Left-Handed, Y+ Up) to Maya/Triton coordinates (Right-Handed, Z+ Up)
        /// </summary>
        /// <param name="position">3D coordinate to translate.</param>
        /// <returns>Point in Triton space.</returns>
        public static Vector4 WorldToTriton(Vector4 position)
        {
            return (s_worldToTriton * position);
        }

        /// <summary>
        /// Convert from Maya/Triton coordinates to Unity coordinates.
        /// </summary>
        /// <param name="position">3D coordinate to translate.</param>
        /// <returns>Point in Unity space.</returns>
        public static Vector4 TritonToWorld(Vector4 position)
        {
            return (s_tritonToWorld * position);
        }

        public static AcousticsPALPublic.TritonVec3f TritonVec3fFromVector3(Vector3 vector)
        {
            return new AcousticsPALPublic.TritonVec3f(vector.x, vector.y, vector.z);
        }

        /// <summary>
        /// Launch the cloud status check thread.
        /// </summary>
        void StartAzureStatusCheck()
        {
            m_workerThread = new System.Threading.Thread(CheckAzureBakeStatus);

            m_workerThread.Start();
        }

        /// <summary>
        /// Called at intervals to check on the status of the cloud bake. If completed, the ACE file is downloaded.
        /// Runs in another thread since the download may occur during the call.
        /// </summary>
        void CheckAzureBakeStatus()
        {
            if (Monitor.TryEnter(this) == false)
            {
                return;
            }
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;

                if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
                {
                    // Nothing to do.
                    Debug.Log("Timer to check cloud bake expired but there is no job ID. Use 'Clear State' to disable further checks.");
                    return;
                }

                JobInformation jobInfo = null;

                m_cloudJobStatus = "";

                try
                {
                    Task<JobInformation> jobInfoTask = CloudProcessor.GetJobInformationAsync(s_AcousticsParameters.ActiveJobID, GetAzureBatchClient());

                    jobInfoTask.Wait();
                    jobInfo = jobInfoTask.Result;
                }
                catch (AggregateException ex)
                {
                    foreach (Exception e in ex.InnerExceptions)
                    {
                        if (e is ArgumentException)
                        {
                            m_cloudJobStatus = "Job deleted";
                            m_timerStartValue = 0;
                            s_AcousticsParameters.ActiveJobID = null;
                            m_cloudJobDeleting = false;
                            break;
                        }
                        else
                        {
                            m_cloudJobStatus = "Error checking status. See console.";
                            m_progressMessage = ex.ToString();
                            QueueUIThreadAction(LogMessage);

                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_cloudJobStatus = "Error checking status. See console.";
                    m_progressMessage = ex.ToString();
                    QueueUIThreadAction(LogMessage);

                    throw;
                }

                m_cloudLastUpdateTime = DateTime.Now;

                if (jobInfo == null)
                {
                    if (String.IsNullOrEmpty(m_cloudJobStatus))
                    {
                        m_cloudJobStatus = "Job Status Unavailable";
                    }

                    QueueUIThreadAction(Repaint);

                    return;
                }

                if (jobInfo.Status == JobStatus.InProgress)
                {
                    int totalCount = jobInfo.Tasks.Active + jobInfo.Tasks.Completed + jobInfo.Tasks.Running;
                    m_cloudJobStatus = $"Running - {jobInfo.Tasks.Completed}/{totalCount} tasks complete";

                    if (jobInfo.Tasks.Failed > 0)
                    {
                        m_cloudJobStatus += $" ({jobInfo.Tasks.Failed} failed)";
                    }
                }
                else if (jobInfo.Status == JobStatus.Pending)
                {
                    m_cloudJobStatus = "Waiting for nodes to initialize";
                }
                else if (jobInfo.Status == JobStatus.Deleting)
                {
                    // Needed to ensure the Cancel/Bake button stays disabled while a job is in the deleting state.
                    m_cloudJobDeleting = true;
                    m_cloudJobStatus = "Cleaning up job resources...";
                }
                else // JobStatus.Completed
                {
                    string failedTasks = "";

                    if (jobInfo.Tasks != null && jobInfo.Tasks.Failed > 0)
                    {
                        failedTasks = String.Format(" ({0} tasks failed)", jobInfo.Tasks.Failed);
                    }

                    m_cloudJobStatus = $"Completed{failedTasks}. Downloading...";
                    QueueUIThreadAction(Repaint);

                    string aceFilename = s_AcousticsParameters.DataFileBaseName + ".ace.bytes";

                    // Temporary location for download
                    string tempAcePath = Path.Combine(Path.GetTempPath(), aceFilename);

                    try
                    {
                        // DownloadAceFileAsync will throw on failure.
                        CloudProcessor.DownloadAceFileAsync(s_AcousticsParameters.ActiveJobID, tempAcePath, GetCloudBlobClient()).Wait();

                        // Copy to the project location and delete the temp file
                        string aceAsset = Path.Combine(s_AcousticsParameters.AcousticsDataFolder, aceFilename);
                        File.Copy(tempAcePath, aceAsset, true);
                        File.Delete(tempAcePath);
                    }
                    catch (Exception ex)
                    {
                        m_cloudJobStatus = "Error downloading ACE file, retrying. See console.";
                        m_progressMessage = ex.ToString();
                        QueueUIThreadAction(LogMessage, Repaint);
                        throw;
                    }

                    try
                    {
                        // Clean up the job and storage.
                        // This can sometimes throw with "service unavailable" in which case BakeService API will use re-try policy.
                        CloudProcessor.DeleteJobAsync(s_AcousticsParameters.ActiveJobID, GetAzureBatchClient(), GetCloudBlobClient()).Wait();
                    }
                    catch (Exception ex)
                    {
                        m_cloudJobStatus = "Failed to delete completed job, please use Azure Portal (https://portal.azure.com) to delete it. More info in the Console.";
                        m_progressMessage = ex.ToString();
                        QueueUIThreadAction(LogMessage, Repaint);
                        // Don't throw, we're done with the active job
                    }

                    m_cloudJobStatus = $"Downloaded{failedTasks}";
                    m_timerStartValue = 0;
                    s_AcousticsParameters.ActiveJobID = null;

                    QueueUIThreadAction(AddACEAsset);
                }

                QueueUIThreadAction(Repaint, MarkParametersDirty);
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
                Monitor.Exit(this);
            }
        }

        /// <summary>
        /// When the ACE file is downloaded from the cloud, it will take Unity a while to find it and add it to the asset database unless we import it.
        /// This function imports the file to the asset database. This causes it to show up in the UI's Project window.
        /// </summary>
        public void AddACEAsset()
        {
            string ACEPath = Path.Combine(s_AcousticsParameters.AcousticsDataFolder, s_AcousticsParameters.DataFileBaseName + ".ace.bytes");
            ACEPath = Path.GetFullPath(ACEPath); // Normalize to ensure it's using standard path delimiters

            // ImportAsset requires a path relative to the application root, under Assets.
            int subPathIndex = ACEPath.ToLower().IndexOf($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}") + 1;

            if (subPathIndex >= 0)
            {
                // Get a project relative path.
                string relativePath = ACEPath.Substring(subPathIndex);

                Debug.Log($"Importing file {relativePath}");
                AssetDatabase.ImportAsset(relativePath.Replace(Path.DirectorySeparatorChar, '/'), ImportAssetOptions.ForceUpdate);
            }
            else
            {
                Debug.LogWarning("The acoustics data folder is not inside the Assets folder!");
            }
        }

        /// <summary>
        /// Unity has a separate certificate store than the user's machine, so we have to do our own certificate validation in order to connect to Azure.
        /// </summary>
        public bool CertificateValidator(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            using (X509Certificate2 cert2 = new X509Certificate2(certificate))
            {
                // If verification fails (or throws e.g. on MacOS) we attempt to explicitly verify below 
                try
                {
                    // Certificate is trusted, return true
                    if (cert2.Verify())
                    {
                        return true;
                    }
                }
                catch (CryptographicException)
                {
                    // Eat the exception, we'll validate the cert below
                }

                // Earlier verification attempt fails, so check explicitly.
                // Only trust an Azure domain certificate issued by Microsoft.
                return (cert2.IssuerName.Name.Contains("O=Microsoft Corporation") &&
                    (cert2.SubjectName.Name.Contains(".batch.azure.com") ||
                    cert2.SubjectName.Name.Contains(".core.windows.net")));
            }
        }

        /// <summary>
        /// Starts the thread used to start a cloud bake.
        /// </summary>
        void StartBake()
        {
            if (!m_ProbesTab.HasPreviewResults)
            {
                EditorUtility.DisplayDialog("Probes calculation missing!", "You must have completed a calculation using the Probes tab before submitting the bake!", "OK");
                return;
            }

            if (s_AcousticsParameters.NodeCount > m_ProbesTab.NumProbes)
            {
                if (!EditorUtility.DisplayDialog("Node count too high!", "It is a waste of resources to request more nodes than probes.\n\nDo you wish to adjust the node count to match the number of nodes?\n\n" +
                    "If so, the value will be adjusted and the bake submitted.\n\nMake sure your Azure Batch account has enough allocation!", "Adjust", "Cancel"))
                {
                    return;
                }

                s_AcousticsParameters.NodeCount = m_ProbesTab.NumProbes;
                MarkParametersDirty();
                Repaint();
            }

            m_workerThread = new System.Threading.Thread(SubmitBakeToAzure);
            m_workerThread.Start();
        }

        /// <summary>
        /// Start the timer used to check on the cloud bake status.
        /// </summary>
        /// <remarks>We only want to do this once the job has successfully been submitted, and this must be done on the UI thread.</remarks>
        void StartAzureCheckTimer()
        {
            m_cloudLastUpdateTime = DateTime.Now;
            m_timerStartValue = EditorApplication.timeSinceStartup;

            // This function is called right when the job is submitted. Mark the parameters data dirty so the JobID is saved to disk.
            MarkParametersDirty();
            Repaint();
        }

        /// <summary>
        /// Let Unity know that the acoustic parameters data has been changed so it will save the changes to disk.
        /// </summary>
        void MarkParametersDirty()
        {
            EditorUtility.SetDirty(s_AcousticsParameters);
        }

        /// <summary>
        /// Submit the cloud bake.
        /// </summary>
        /// <param name="threadData">Unused</param>
        void SubmitBakeToAzure(object threadData)
        {
            ComputePoolConfiguration poolConfiguration = s_AcousticsParameters.GetPoolConfiguration();

            CloudProcessor.AcousticsDockerImageName = s_AcousticsParameters.TritonImage;
            
            // The creds might be non-null but empty, probably because they were deleted
            // Set it back to null which prevents empty creds being specified in the web request.
            if (string.IsNullOrEmpty(s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer))
            {
                s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer = null;
            }
            // Otherwise fill in the specified container registry details
            else
            {
                CloudProcessor.CustomContainerRegistry = new ContainerRegistry(s_AcousticsParameters.AzureAccounts.AzureContainerRegistryAccount,
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryServer,
                    s_AcousticsParameters.AzureAccounts.AzureContainerRegistryKey);
            }


            JobConfiguration jobConfig = new JobConfiguration
            {
                Prefix = "U3D" + DateTime.Now.ToString("yyMMdd-HHmmssfff"),
                VoxFilepath = s_AcousticsParameters.VoxFilepath,
                ConfigurationFilepath = s_AcousticsParameters.ConfigFilepath,
            };

            m_cloudJobStatus = "Submitting (please wait)...";
            m_cloudLastUpdateTime = DateTime.Now;

            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;
                Task<string> submitTask = CloudProcessor.SubmitForAnalysisAsync(poolConfiguration, jobConfig, GetAzureBatchClient(), GetCloudBlobClient());
                submitTask.Wait();

                s_AcousticsParameters.ActiveJobID = submitTask.Result;
            }
            catch (Exception ex)
            {
                m_timerStartValue = 0;
                Exception curException = ex;

                // Make sure we log the exception
                m_cloudJobStatus = "An error occurred. See Console output.";
                m_progressMessage = ex.ToString();
                QueueUIThreadAction(LogMessage);

                while (curException != null)
                {
                    if (curException is WebException)
                    {
                        WebException we = curException as WebException;

                        if (we.Status == WebExceptionStatus.TrustFailure)
                        {
                            // If you hit this failure, set a breakpoint in the Certificate Validator function above
                            // and see if the certificate information has changed. If the cert looks valid, update the code
                            // to reflect the correct organization name or subject name.
                            m_cloudJobStatus = "Azure trust failure. Check scripts.";
                            throw new WebException("Connections to Azure web services are not trusted. The certificate check in the scripts may no longer match Azure certificates.", we);
                        }
                    }
                    else if (curException is Microsoft.Azure.Batch.AddTaskCollectionTerminatedException)
                    {
                        m_cloudJobStatus = "Error: Too many probes!\nEnsure the scene has a navmesh and try reducing the number of Acoustic Geometry objects";
                    }

                    curException = curException.InnerException;
                }
                throw;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
            }

            m_cloudJobStatus = "Submitted Successfully.";

            QueueUIThreadAction(StartAzureCheckTimer);
        }

        /// <summary>
        /// Starts the thread that attempts to cancel a bake.
        /// </summary>
        void CancelBake()
        {
            if (String.IsNullOrEmpty(s_AcousticsParameters.ActiveJobID))
            {
                // Nothing to do
                return;
            }

            m_cloudJobStatus = "Canceling job (please wait)...";

            m_workerThread = new System.Threading.Thread(CancelBakeWorker);
            m_workerThread.Start();
        }

        /// <summary>
        /// Attempts to cancel a bake job in progress.
        /// </summary>
        void CancelBakeWorker()
        {
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = CertificateValidator;
                CloudProcessor.DeleteJobAsync(s_AcousticsParameters.ActiveJobID, GetAzureBatchClient(), GetCloudBlobClient()).Wait();
            }
            catch (Exception ex)
            {
                m_cloudJobStatus = "Job cancel failed. Use Azure Portal.";

                // Make sure we log the exception
                m_progressMessage = ex.ToString();
                QueueUIThreadAction(LogMessage);

                throw;
            }
            finally
            {
                ServicePointManager.ServerCertificateValidationCallback = null;
            }

            m_cloudJobStatus = "Cancel Request Sent.";

            QueueUIThreadAction(StartAzureCheckTimer, StartAzureStatusCheck);
        }

        private BatchClient GetAzureBatchClient()
        {
            var batchCred = new BatchSharedKeyCredentials(s_AcousticsParameters.AzureAccounts.BatchAccountUrl,
                s_AcousticsParameters.AzureAccounts.BatchAccountName,
                s_AcousticsParameters.AzureAccounts.BatchAccountKey);
            return BatchClient.Open(batchCred);
        }

        private CloudBlobClient GetCloudBlobClient()
        {
            var storageCred = new StorageCredentials(s_AcousticsParameters.AzureAccounts.StorageAccountName,
                s_AcousticsParameters.AzureAccounts.StorageAccountKey);
            CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCred, true);
            return cloudStorageAccount.CreateCloudBlobClient();
        }
    }
}
