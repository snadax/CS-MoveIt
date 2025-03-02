﻿using ColossalFramework.Globalization;
using ICities;
using MoveIt.Localization;
using UnityEngine;

namespace MoveIt
{
    public class MoveItLoader : LoadingExtensionBase
    {
        public static bool IsGameLoaded { get; private set; } = false;
        public static LoadMode loadMode;
        private static GameObject DebugGameObject, MoveToToolObject;

        public override void OnLevelLoaded(LoadMode mode)
        {
            loadMode = mode;
            InstallMod();
        }

        public override void OnLevelUnloading()
        {
            UninstallMod();
        }

        public static void InstallMod()
        {
            if (MoveItTool.instance == null)
            {
                // Creating the instance
                ToolController toolController = Object.FindObjectOfType<ToolController>();

                MoveItTool.instance = toolController.gameObject.AddComponent<MoveItTool>();
            }
            else
            {
                Log.Error($"InstallMod with existing instance!");
            }

            MoveItTool.stepOver = new StepOver();

            DebugGameObject = new GameObject("MIT_DebugPanel");
            DebugGameObject.AddComponent<DebugPanel>();
            MoveItTool.m_debugPanel = DebugGameObject.GetComponent<DebugPanel>();

            MoveToToolObject = new GameObject("MIT_MoveToPanel");
            MoveToToolObject.AddComponent<MoveToPanel>();
            MoveItTool.m_moveToPanel = MoveToToolObject.GetComponent<MoveToPanel>();

            UIFilters.FilterCBs.Clear();
            UIFilters.NetworkCBs.Clear();

            Filters.Picker = new PickerFilter();

            MoveItTool.filterBuildings = true;
            MoveItTool.filterProps = true;
            MoveItTool.filterDecals = true;
            MoveItTool.filterSurfaces = true;
            MoveItTool.filterTrees = true;
            MoveItTool.filterNodes = true;
            MoveItTool.filterSegments = true;
            MoveItTool.filterNetworks = false;

            IsGameLoaded = true;

            // Touch each prop to ensure lights are functional
            for (ushort i = 0; i < ushort.MaxValue; i++)
            {
                PropManager.instance.UpdateProp(i);
            }
        }

        public static void UninstallMod()
        {
            if (ToolsModifierControl.toolController.CurrentTool is MoveItTool)
                ToolsModifierControl.SetTool<DefaultTool>();

            MoveItTool.m_debugPanel = null;
            Object.Destroy(DebugGameObject);
            Object.Destroy(MoveToToolObject);
            if (PO_Manager.gameObject != null)
            {
                Object.Destroy(PO_Manager.gameObject);
            }
            UIToolOptionPanel.instance = null;
            UIMoreTools.MoreToolsPanel = null;
            UIMoreTools.MoreToolsBtn = null;
            Action.selection.Clear();
            Filters.Picker = null;
            MoveItTool.PO = null;
            Object.Destroy(MoveItTool.instance.m_button);

            UILoadWindow.Close();
            UISaveWindow.Close();

            if (MoveItTool.instance != null)
            {
                MoveItTool.instance.enabled = false;
                MoveItTool.instance = null;
            }

            IsGameLoaded = false;

            LocaleManager.eventLocaleChanged -= LocaleChanged;
        }

        internal static void LocaleChanged()
        {
            Log.Debug($"Move It Locale changed {Str.Culture?.Name}->{ModInfo.Culture.Name}");
            Str.Culture = ModInfo.Culture;
        }
    }
}
