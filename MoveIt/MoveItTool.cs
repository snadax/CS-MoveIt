﻿using ColossalFramework;
using ColossalFramework.Math;
using ColossalFramework.IO;
using ColossalFramework.Plugins;
using ColossalFramework.UI;
using MoveItIntegration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Serialization;
using UnityEngine;

namespace MoveIt
{
    public partial class MoveItTool : ToolBase
    {
        public static List<MoveItIntegrationBase> Integrations { get; } = IntegrationHelper.GetIntegrations();
        public static MoveItIntegrationBase GetIntegrationByID(string ID) => Integrations.Where(item => item.ID == ID).FirstOrDefault();

        public enum ToolAction
        {
            None,
            Do,
            Undo,
            Redo
        }

        public enum ToolStates
        {
            Default,
            MouseDragging,
            RightDraggingClone,
            DrawingSelection,
            Cloning,
            Aligning,
            Picking,
            ToolActive
        }

        public enum MT_Tools
        {
            Off,
            Height,
            Inplace,
            Group,
            Slope,
            Mirror,
            MoveTo
        }

        public const string settingsFileName = "MoveItTool";
        public static readonly string saveFolder = Path.Combine(DataLocation.localApplicationData, "MoveItExports");
        public const int UI_Filter_CB_Height = 25;
        public const int Fastmove_Max = 100;

        public static MoveItTool instance;
        public static SavedBool hideChangesWindow = new SavedBool("hideChanges290", settingsFileName, false, true);
        public static SavedBool autoCloseAlignTools = new SavedBool("autoCloseAlignTools", settingsFileName, false, true);
        public static SavedBool POShowDeleteWarning = new SavedBool("POShowDeleteWarning", settingsFileName, true, true);
        public static SavedBool useCardinalMoves = new SavedBool("useCardinalMoves", settingsFileName, false, true);
        public static SavedBool rmbCancelsCloning = new SavedBool("rmbCancelsCloning", settingsFileName, false, true);
        public static SavedBool advancedPillarControl = new SavedBool("advancedPillarControl", settingsFileName, false, true);
        public static SavedBool fastMove = new SavedBool("fastMove", settingsFileName, false, true);
        public static SavedBool altSelectNodeBuildings = new SavedBool("altSelectNodeBuildings", settingsFileName, false, true);
        public static SavedBool altSelectSegmentNodes = new SavedBool("altSelectSegmentNodes", settingsFileName, true, true);
        public static SavedBool followTerrainModeEnabled = new SavedBool("followTerrainModeEnabled", settingsFileName, true, true);
        public static SavedBool showDebugPanel = new SavedBool("showDebugPanel", settingsFileName, false, true);

        public static bool filterPicker = false;
        public static bool filterBuildings = true;
        public static bool filterProps = true;
        public static bool filterDecals = true;
        public static bool filterSurfaces = true;
        public static bool filterTrees = true;
        public static bool filterNodes = true;
        public static bool filterSegments = true;
        public static bool filterNetworks = false;
        public static bool filterProcs = true;

        public static bool followTerrain = true;
        public static bool marqueeSelection = false;
        internal static bool dragging = false;
        public static bool treeSnapping = false;

        public static StepOver stepOver;
        internal static DebugPanel m_debugPanel;
        internal static MoveToPanel m_moveToPanel;

        public int segmentUpdateCountdown = -1;
        public HashSet<ushort> segmentsToUpdate = new HashSet<ushort>();

        public int areaUpdateCountdown = -1;
        public HashSet<Bounds> areasToUpdate = new HashSet<Bounds>();

        internal static Color m_hoverColor = new Color32(0, 181, 255, 255);
        internal static Color m_selectedColor = new Color32(95, 166, 0, 244);
        internal static Color m_moveColor = new Color32(125, 196, 30, 244);
        internal static Color m_removeColor = new Color32(255, 160, 47, 191);
        internal static Color m_despawnColor = new Color32(255, 160, 47, 191);
        internal static Color m_alignColor = new Color32(255, 255, 255, 244);
        internal static Color m_POhoverColor = new Color32(240, 140, 255, 230);
        internal static Color m_POselectedColor = new Color32(225, 130, 240, 125);

        internal static PO_Manager PO = null;
        internal static NS_Manager NS = null;
        private static int _POProcessing = 0;
        private static float POProcessingStart = 0;
        internal static int POProcessing
        {
            get
            {
                return _POProcessing;
            }
            set
            {
                _POProcessing = value;
                POProcessingStart = Time.time;
                if (m_debugPanel != null)
                {
                    m_debugPanel.UpdatePanel();
                }
            }
        }

        private const float XFACTOR = 0.25f;
        private const float YFACTOR = 0.015625f; // 1/64
        private const float ZFACTOR = 0.25f;

        public static ToolStates ToolState { get; set; } = ToolStates.Default;
        private static MT_Tools m_toolsMode = MT_Tools.Off;
        public static MT_Tools MT_Tool
        {
            get => m_toolsMode;
            set
            {
                m_toolsMode = value;
                if (m_debugPanel != null)
                {
                    m_debugPanel.UpdatePanel();
                }
            }
        }
        private static ushort m_alignToolPhase = 0;
        public static ushort AlignToolPhase
        {
            get => m_alignToolPhase;
            set
            {
                m_alignToolPhase = value;
                if (m_debugPanel != null)
                {
                    m_debugPanel.UpdatePanel();
                }
            }
        }

        private bool m_snapping = false;
        public bool snapping
        {
            get
            {
                if (ToolState == ToolStates.MouseDragging ||
                    ToolState == ToolStates.Cloning || ToolState == ToolStates.RightDraggingClone)
                {
                    return m_snapping != Event.current.alt;
                }
                return m_snapping;
            }

            set
            {
                m_snapping = value;
            }
        }

        public static bool gridVisible
        {
            get
            {
                return TerrainManager.instance.RenderZones;
            }

            set
            {
                TerrainManager.instance.RenderZones = value;
            }
        }

        public static bool tunnelVisible
        {
            get
            {
                return InfoManager.instance.CurrentMode == InfoManager.InfoMode.Underground;
            }

            set
            {
                if (value)
                {
                    m_prevInfoMode = InfoManager.instance.CurrentMode;
                    InfoManager.instance.SetCurrentMode(InfoManager.InfoMode.Underground, InfoManager.instance.CurrentSubMode);
                }
                else
                {
                    InfoManager.instance.SetCurrentMode(m_prevInfoMode, InfoManager.instance.CurrentSubMode);
                }
            }
        }

        internal UIMoveItButton m_button;
        private UIComponent m_pauseMenu;

        private Quad3 m_selection; // Marquee selection box
        public Instance m_hoverInstance;
        internal Instance m_lastInstance;
        private HashSet<Instance> m_marqueeInstances;

        internal static bool m_isLowSensitivity;
        private Vector3 m_dragStartRelative; // Where the current drag started, relative to selection center
        private Vector3 m_clickPositionAbs; // Where the current drag started, absolute
        private Vector3 m_sensitivityTogglePosAbs; // Where sensitivity was last toggled, absolute

        private float m_mouseStartX;
        private float m_startAngle;
        private float m_sensitivityTogglePosX; // Where sensitivity was last toggled, X-axis absolute
        private float m_sensitivityAngleOffset; // Accumulated angle offset from low sensitivity

        private NetSegment m_segmentGuide;

        private bool m_prevRenderZones;
        private ToolBase m_prevTool;

        private static InfoManager.InfoMode m_prevInfoMode;

        private long m_keyTime;
        private long m_scaleKeyTime;
        private long m_rightClickTime;
        private long m_middleClickTime;
        private long m_leftClickTime;

        internal static Dictionary<ushort, ushort> m_pillarMap; // Building -> First Node

        protected static NetSegment[] segmentBuffer = Singleton<NetManager>.instance.m_segments.m_buffer;
        protected static NetNode[] nodeBuffer = Singleton<NetManager>.instance.m_nodes.m_buffer;
        protected static Building[] buildingBuffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;

        public ToolAction m_nextAction = ToolAction.None;

        private static System.Random _rand = null;
        internal static System.Random Rand
        {
            get
            {
                if (_rand == null)
                    _rand = new System.Random();
                return _rand;
            }
        }

        protected override void Awake()
        {
            ActionQueue.instance = new ActionQueue();

            m_toolController = FindObjectOfType<ToolController>();
            enabled = false;

            m_button = UIView.GetAView().AddUIComponent(typeof(UIMoveItButton)) as UIMoveItButton;

            followTerrain = followTerrainModeEnabled;
            if (!isTreeAnarchyEnabled())
            {
                treeSnapping = isTreeSnappingEnabled();
            }
        }

        protected override void OnEnable()
        {
            if (PO == null)
            {
                PO = new PO_Manager();
            }
            if (NS == null)
            {
                NS = new NS_Manager();
            }

            if (UIToolOptionPanel.instance == null)
            {
                UIComponent TSBar = UIView.GetAView().FindUIComponent<UIComponent>("TSBar");
                TSBar.AddUIComponent<UIToolOptionPanel>();
            }
            else
            {
                UIToolOptionPanel.instance.isVisible = true;
            }

            if (!hideChangesWindow && UIChangesWindow.instance != null)
            {
                UIChangesWindow.instance.isVisible = true;
            }

            m_pauseMenu = UIView.library.Get("PauseMenu");

            m_prevInfoMode = InfoManager.instance.CurrentMode;
            InfoManager.SubInfoMode subInfoMode = InfoManager.instance.CurrentSubMode;

            m_prevRenderZones = TerrainManager.instance.RenderZones;
            m_prevTool = m_toolController.CurrentTool == this ? ToolsModifierControl.GetTool<DefaultTool>() : m_toolController.CurrentTool;

            m_toolController.CurrentTool = this;

            InfoManager.instance.SetCurrentMode(m_prevInfoMode, subInfoMode);

            if (UIToolOptionPanel.instance != null && UIToolOptionPanel.instance.grid != null)
            {
                gridVisible = UIToolOptionPanel.instance.grid.activeStateIndex == 1;
                tunnelVisible = UIToolOptionPanel.instance.underground.activeStateIndex == 1;
            }

            if (PO.Active)
            {
                PO.ToolEnabled();
                if (POProcessing > 0 && Time.time > POProcessingStart + 300)
                { // If it's been more than 5 mins since PO last started copying, give up and reset
                    Log.Info($"Timing out PO Processing");
                    POProcessing = 0;
                }
                ActionQueue.instance.Push(new TransformAction());
            }

            UIMoreTools.UpdateMoreTools();
            UpdatePillarMap();

            //string msg = $"Assemblies:";
            //foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            //{
            //    msg += $"\n{assembly.GetName().Name.ToLower()}";
            //}
            //Log.Debug(msg);

            // msg = "Plugins:";
            //foreach (PluginManager.PluginInfo pi in PluginManager.instance.GetPluginsInfo())
            //{
            //    msg += $"\n{pi.publishedFileID.AsUInt64} - {pi.name} ({pi.isEnabled})" +
            //        $"\n - {pi.modPath}";
            //}
            //Log.Debug(msg);
        }

        protected override void OnDisable()
        {
            lock (ActionQueue.instance)
            {
                if (ToolState == ToolStates.Cloning || ToolState == ToolStates.RightDraggingClone)
                {
                    // Cancel cloning
                    ActionQueue.instance.Undo();
                    ActionQueue.instance.Invalidate();
                }

                if (ToolState == ToolStates.MouseDragging)
                {
                    ((TransformAction)ActionQueue.instance.current).FinaliseDrag();
                }

                UpdateAreas();
                UpdateSegments();
                SetToolState();

                if (UIChangesWindow.instance != null)
                {
                    UIChangesWindow.instance.isVisible = false;
                }

                if (UIToolOptionPanel.instance != null)
                {
                    UIToolOptionPanel.instance.isVisible = false;
                }

                if (m_moveToPanel != null)
                {
                    m_moveToPanel.Visible(false);
                }

                InfoManager.instance.SetCurrentMode(m_prevInfoMode, InfoManager.instance.CurrentSubMode);

                if (m_toolController.NextTool == null && m_prevTool != null && m_prevTool != this)
                {
                    TerrainManager.instance.RenderZones = m_prevRenderZones;
                    m_prevTool.enabled = true;
                }
                m_prevTool = null;

                UIMoreTools.UpdateMoreTools();
                UIToolOptionPanel.RefreshCloneButton();
            }
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {
            if (!enabled)
            {
                return;
            }

            if (ToolState == ToolStates.Default || ToolState == ToolStates.Aligning || ToolState == ToolStates.Picking || ToolState == ToolStates.ToolActive)
            {
                // Reset all PO
                //if (PO.Active && POHighlightUnselected)
                //{
                //    foreach (PO_Object obj in PO.Objects)
                //    {
                //        obj.Selected = false;
                //    }
                //}

                // Debug overlays
                foreach (DebugOverlay d in DebugBoxes)
                {
                    Singleton<RenderManager>.instance.OverlayEffect.DrawQuad(cameraInfo, d.color, d.quad, 0, 1000, false, false);
                }
                foreach (Vector3 v in DebugPoints)
                {
                    Singleton<RenderManager>.instance.OverlayEffect.DrawCircle(cameraInfo, new Color32(255, 255, 255, 63), v, 8, 0, 1000, false, false);
                }

                ActionQueue.instance.current?.Overlays(cameraInfo, m_alignColor, m_despawnColor);

                if (Action.selection.Count > 0)
                {
                    // Highlight Selected Items
                    foreach (Instance instance in Action.selection)
                    {
                        if (instance.isValid && instance != m_hoverInstance)
                        {
                            if (instance is MoveableProc mpo)
                            {
                                if (m_hoverInstance == null || (m_hoverInstance.isValid && (mpo.id != m_hoverInstance.id)))
                                {
                                    mpo.RenderOverlay(cameraInfo, m_POselectedColor, m_despawnColor);
                                    mpo.m_procObj.Selected = true;
                                }
                            }
                            else
                            {
                                instance.RenderOverlay(cameraInfo, m_selectedColor, m_despawnColor);
                            }
                        }
                    }
                    if (ToolState == ToolStates.Aligning && MT_Tool == MT_Tools.Slope && AlignToolPhase == 2)
                    {
                        AlignSlopeAction action = ActionQueue.instance.current as AlignSlopeAction;
                        action.PointA.RenderOverlay(cameraInfo, m_alignColor, m_despawnColor);
                    }

                    Vector3 center = Action.GetCenter();
                    center.y = TerrainManager.instance.SampleRawHeightSmooth(center);
                    RenderManager.instance.OverlayEffect.DrawCircle(cameraInfo, m_selectedColor, center, 1f, -1f, 1280f, false, true);
                }

                if (m_hoverInstance != null && m_hoverInstance.isValid)
                {
                    Color color = m_hoverColor;
                    if (m_hoverInstance is MoveableProc mpo)
                    {
                        color = m_POhoverColor;
                        mpo.m_procObj.Selected = true;
                    }

                    if (ToolState == ToolStates.Aligning || ToolState == ToolStates.Picking)
                    {
                        color = m_alignColor;
                    }
                    else if (Action.selection.Contains(m_hoverInstance))
                    {
                        if (Event.current.shift)
                        {
                            color = m_removeColor;
                        }
                    }

                    m_hoverInstance.RenderOverlay(cameraInfo, color, m_despawnColor);
                }
            }
            else if (ToolState == ToolStates.MouseDragging)
            {
                if (Action.selection.Count > 0)
                {
                    foreach (Instance instance in Action.selection)
                    {
                        if (instance.isValid && instance != m_hoverInstance)
                        {
                            instance.RenderOverlay(cameraInfo, m_moveColor, m_despawnColor);
                        }
                    }

                    if (!m_isLowSensitivity)
                    {
                        Vector3 center = Action.GetCenter();
                        center.y = TerrainManager.instance.SampleRawHeightSmooth(center);
                        RenderManager.instance.OverlayEffect.DrawCircle(cameraInfo, m_selectedColor, center, 1f, -1f, 1280f, false, true);

                        if (snapping && m_segmentGuide.m_startNode != 0 && m_segmentGuide.m_endNode != 0)
                        {
                            NetManager netManager = NetManager.instance;
                            NetNode[] nodeBuffer = netManager.m_nodes.m_buffer;

                            Bezier3 bezier;
                            bezier.a = nodeBuffer[m_segmentGuide.m_startNode].m_position;
                            bezier.d = nodeBuffer[m_segmentGuide.m_endNode].m_position;

                            bool smoothStart = ((nodeBuffer[m_segmentGuide.m_startNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None);
                            bool smoothEnd = ((nodeBuffer[m_segmentGuide.m_endNode].m_flags & NetNode.Flags.Middle) != NetNode.Flags.None);

                            NetSegment.CalculateMiddlePoints(
                                bezier.a, m_segmentGuide.m_startDirection,
                                bezier.d, m_segmentGuide.m_endDirection,
                                smoothStart, smoothEnd, out bezier.b, out bezier.c);

                            RenderManager.instance.OverlayEffect.DrawBezier(cameraInfo, m_selectedColor, bezier, 0f, 100000f, -100000f, -1f, 1280f, false, true);
                        }
                    }
                }
            }
            else if (ToolState == ToolStates.DrawingSelection)
            {
                bool removing = Event.current.alt;
                bool adding = Event.current.shift;

                if ((removing || adding) && Action.selection.Count > 0)
                {
                    foreach (Instance instance in Action.selection)
                    {
                        if (instance.isValid)
                        {
                            if (adding || (removing && !m_marqueeInstances.Contains(instance)))
                            {
                                instance.RenderOverlay(cameraInfo, m_selectedColor, m_despawnColor);
                            }
                        }
                    }

                    Vector3 center = Action.GetCenter();
                    center.y = TerrainManager.instance.SampleRawHeightSmooth(center);

                    RenderManager.instance.OverlayEffect.DrawCircle(cameraInfo, m_selectedColor, center, 1f, -1f, 1280f, false, true);
                }

                Color color = m_hoverColor;
                if (removing)
                {
                    color = m_removeColor;
                }

                if (m_selection.a != m_selection.c)
                {
                    RenderManager.instance.OverlayEffect.DrawQuad(cameraInfo, color, m_selection, -1f, 1280f, false, true);
                }

                if (m_marqueeInstances != null)
                {
                    foreach (Instance instance in m_marqueeInstances)
                    {
                        if (instance.isValid)
                        {
                            if (instance is MoveableProc mpo)
                            {
                                if (mpo.m_procObj.Group != null && mpo.m_procObj.Group.root != mpo.m_procObj)
                                    continue;
                            }

                            bool contains = Action.selection.Contains(instance);
                            if ((adding && !contains) || (removing && contains) || (!adding && !removing))
                            {
                                instance.RenderOverlay(cameraInfo, color, m_despawnColor);
                            }
                        }
                    }
                }
            }
            else if (ToolState == ToolStates.Cloning || ToolState == ToolStates.RightDraggingClone)
            {
                CloneActionBase action = ActionQueue.instance.current as CloneActionBase;

                Matrix4x4 matrix4x = default;
                matrix4x.SetTRS(action.center + action.moveDelta, Quaternion.AngleAxis(action.angleDelta * Mathf.Rad2Deg, Vector3.down), Vector3.one);

                foreach (InstanceState state in action.m_states)
                {
                    Color color = m_hoverColor;
                    if (state is ProcState)
                    {
                        color = m_POhoverColor;
                    }

                    state.instance.RenderCloneOverlay(state, ref matrix4x, action.moveDelta, action.angleDelta, action.center, followTerrain, cameraInfo, color);
                }
            }
        }

        public override void RenderGeometry(RenderManager.CameraInfo cameraInfo)
        {
            if (ToolState == ToolStates.Cloning || ToolState == ToolStates.RightDraggingClone)
            {
                CloneActionBase action = ActionQueue.instance.current as CloneActionBase;

                Matrix4x4 matrix4x = default;
                matrix4x.SetTRS(action.center + action.moveDelta, Quaternion.AngleAxis(action.angleDelta * Mathf.Rad2Deg, Vector3.down), Vector3.one);

                foreach (InstanceState state in action.m_states)
                {
                    state.instance.RenderCloneGeometry(state, ref matrix4x, action.moveDelta, action.angleDelta, action.center, followTerrain, cameraInfo, m_hoverColor);
                }
            }
            else if (ToolState == ToolStates.MouseDragging)
            {
                TransformAction action = ActionQueue.instance.current as TransformAction;

                foreach (InstanceState state in action.m_states)
                {
                    state.instance?.RenderGeometry(cameraInfo, m_hoverColor);
                }
            }
        }

        public override void SimulationStep()
        {
            lock (ActionQueue.instance)
            {
                try
                {
                    switch (m_nextAction)
                    {
                        case ToolAction.Undo:
                            {
                                ActionQueue.instance.Undo();
                                break;
                            }
                        case ToolAction.Redo:
                            {
                                ActionQueue.instance.Redo();
                                break;
                            }
                        case ToolAction.Do:
                            {
                                ActionQueue.instance.Do();

                                if (ActionQueue.instance.current is CloneAction)
                                {
                                    StartCloning();
                                }
                                break;
                            }
                    }

                    bool inputHeld = m_scaleKeyTime != 0 || m_keyTime != 0 || m_leftClickTime != 0 || m_rightClickTime != 0;

                    if (segmentUpdateCountdown == 0)
                    {
                        UpdateSegments();
                    }

                    if (!inputHeld && segmentUpdateCountdown >= 0)
                    {
                        segmentUpdateCountdown--;
                    }

                    if (areaUpdateCountdown == 0)
                    {
                        UpdateAreas();
                    }

                    if (!inputHeld && areaUpdateCountdown >= 0)
                    {
                        areaUpdateCountdown--;
                    }
                }
                catch (Exception e)
                {
                    DebugUtils.Log("SimulationStep failed");
                    DebugUtils.LogException(e);
                }

                m_nextAction = ToolAction.None;
            }
        }

        public void UpdateAreas()
        {
            //foreach (Bounds b in areasToUpdate)
            //{
            //    AddDebugBox(b, new Color32(255, 31, 31, 31));
            //}
            HashSet<Bounds> merged = MergeBounds(areasToUpdate);
            //foreach (Bounds b in merged)
            //{
            //    b.Expand(4f);
            //    AddDebugBox(b, new Color32(31, 31, 255, 31));
            //}

            foreach (Bounds bounds in merged)
            {
                try
                {
                    bounds.Expand(64f);
                    Singleton<VehicleManager>.instance.UpdateParkedVehicles(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                    TerrainModify.UpdateArea(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z, true, true, false);
                    UpdateRender(bounds);
                    bounds.Expand(512f);
                    Singleton<ElectricityManager>.instance.UpdateGrid(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                    Singleton<WaterManager>.instance.UpdateGrid(bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
                }
                catch (IndexOutOfRangeException)
                {
                    Log.Error($"Failed to update bounds {bounds}");
                }
            }

            areasToUpdate.Clear();
        }

        public void UpdateSegments()
        {
            foreach (ushort segment in segmentsToUpdate)
            {
                NetSegment[] segmentBuffer = NetManager.instance.m_segments.m_buffer;
                if (segmentBuffer[segment].m_flags != NetSegment.Flags.None)
                {
                    ReleaseSegmentBlock(segment, ref segmentBuffer[segment].m_blockStartLeft);
                    ReleaseSegmentBlock(segment, ref segmentBuffer[segment].m_blockStartRight);
                    ReleaseSegmentBlock(segment, ref segmentBuffer[segment].m_blockEndLeft);
                    ReleaseSegmentBlock(segment, ref segmentBuffer[segment].m_blockEndRight);
                }

                segmentBuffer[segment].Info.m_netAI.CreateSegment(segment, ref segmentBuffer[segment]);
            }
            segmentsToUpdate.Clear();
        }

        public override ToolErrors GetErrors()
        {
            return ToolErrors.None;
        }

        internal static Vector3 RaycastMouseLocation()
        {
            return RaycastMouseLocation(Camera.main.ScreenPointToRay(Input.mousePosition));
        }

        internal static Vector3 RaycastMouseLocation(Ray mouseRay)
        {
            RaycastInput input = new RaycastInput(mouseRay, Camera.main.farClipPlane)
            {
                m_ignoreTerrain = false
            };
            RayCast(input, out RaycastOutput output);

            return output.m_hitPos;
        }

        private static void UpdateRender(Bounds bounds)
        {
            int num1 = Mathf.Clamp((int)(bounds.min.x / 64f + 135f), 0, 269);
            int num2 = Mathf.Clamp((int)(bounds.min.z / 64f + 135f), 0, 269);
            int x0 = num1 * 45 / 270 - 1;
            int z0 = num2 * 45 / 270 - 1;

            num1 = Mathf.Clamp((int)(bounds.max.x / 64f + 135f), 0, 269);
            num2 = Mathf.Clamp((int)(bounds.max.z / 64f + 135f), 0, 269);
            int x1 = num1 * 45 / 270 + 1;
            int z1 = num2 * 45 / 270 + 1;

            RenderManager renderManager = Singleton<RenderManager>.instance;
            RenderGroup[] renderGroups = renderManager.m_groups;

            for (int i = z0; i < z1; i++)
            {
                for (int j = x0; j < x1; j++)
                {
                    int n = Mathf.Clamp(i * 45 + j, 0, renderGroups.Length - 1);

                    if (n < 0)
                    {
                        continue;
                    }
                    else if (n >= renderGroups.Length)
                    {
                        break;
                    }

                    if (renderGroups[n] != null)
                    {
                        renderGroups[n].SetAllLayersDirty();
                        renderManager.m_updatedGroups1[n >> 6] |= 1uL << n;
                        renderManager.m_groupsUpdated1 = true;
                    }
                }
            }
        }
    }
}
