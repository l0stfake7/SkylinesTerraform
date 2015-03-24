﻿using ColossalFramework.IO;
using ColossalFramework;
using System.IO;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerraformTool
{
    public class InGameTerrainTool : ToolBase
    {
        public enum Mode
        {
            Shift,
            Level,
            Soften,
            Slope
        }
        private struct UndoStroke
        {
            public int xmin;
            public int xmax;
            public int zmin;
            public int zmax;
            public int pointer;
            public int total_cost;
        }
        private struct ToolSettings
        {
            public ToolSettings(float m_brushSize, float m_strength)
            {
                this.m_strength = m_strength;
                this.m_brushSize = m_brushSize;
            }
            public float m_brushSize;
            public float m_strength;

        }
        private Dictionary<InGameTerrainTool.Mode, ToolSettings> ModeSettings;

        public InGameTerrainTool.Mode m_mode;
        public bool m_free = false;
        public float m_brushSize = 1f;
        public float m_strength = 0.5f;
        public Texture2D m_brush;
        public CursorInfo m_shiftCursor;
        public CursorInfo m_levelCursor;
        public CursorInfo m_softenCursor;
        public CursorInfo m_slopeCursor;
        private Vector3 m_mousePosition;
        internal Vector3 m_startPosition;
        private Vector3 m_endPosition;
        private Ray m_mouseRay;
        private float m_mouseRayLength;
        private bool m_mouseLeftDown;
        private bool m_mouseRightDown;
        private bool m_mouseRayValid;
        private bool m_strokeEnded;
        private int m_strokeXmin;
        private int m_strokeXmax;
        private int m_strokeZmin;
        private int m_strokeZmax;
        private int m_undoBufferFreePointer;
        private List<InGameTerrainTool.UndoStroke> m_undoList;
        private bool m_strokeInProgress;
        private bool m_undoRequest;
        private long m_lastCash;

        private SavedInputKey m_UndoKey = new SavedInputKey(Settings.mapEditorTerrainUndo, Settings.inputSettingsFile, DefaultSettings.mapEditorTerrainUndo, true);
        private SavedInputKey m_IncreaseBrushSizeKey = new SavedInputKey(Settings.mapEditorIncreaseBrushSize, Settings.inputSettingsFile, DefaultSettings.mapEditorIncreaseBrushSize, true);
        private SavedInputKey m_DecreaseBrushSizeKey = new SavedInputKey(Settings.mapEditorDecreaseBrushSize, Settings.inputSettingsFile, DefaultSettings.mapEditorDecreaseBrushSize, true);
        private SavedInputKey m_IncreaseBrushStrengthKey = new SavedInputKey(Settings.mapEditorIncreaseBrushStrength, Settings.inputSettingsFile, DefaultSettings.mapEditorIncreaseBrushStrength, true);
        private SavedInputKey m_DecreaseBrushStrengthKey = new SavedInputKey(Settings.mapEditorDecreaseBrushStrength, Settings.inputSettingsFile, DefaultSettings.mapEditorDecreaseBrushStrength, true);

        private long m_totalCost;
        private int m_costMultiplier = 500;
        public static ConfigData Myconfig;

        public InGameTerrainTool()
        {
            if (InGameTerrainTool.Myconfig == null & File.Exists(ConfigData.GetConfigPath()))
            {
                InGameTerrainTool.Myconfig = ConfigData.Deserialize();
            }
            else
            {
                //InGameTerrainTool.Myconfig = new ConfigData();
                //ConfigData.Serialize(InGameTerrainTool.Myconfig);
            }
            if (InGameTerrainTool.Myconfig != null)
            {
                this.m_costMultiplier = InGameTerrainTool.Myconfig.MoneyModifer;
                this.m_free = InGameTerrainTool.Myconfig.Free;
            }
        }

        public bool IsUndoAvailable()
        {
            return this.m_undoList != null && this.m_undoList.Count > 0;
        }
        public void Undo()
        {
            this.m_undoRequest = true;
        }

        public override void RenderOverlay(RenderManager.CameraInfo cameraInfo)
        {

            base.RenderOverlay(cameraInfo);
        }

        public void ResetUndoBuffer()
        {
            BuildingAI bb = new BuildingAI();

            this.m_undoList.Clear();

            ushort[] backupHeights = Singleton<TerrainManager>.instance.BackupHeights;
            ushort[] rawHeights = Singleton<TerrainManager>.instance.RawHeights;
            for (int i = 0; i <= 1080; i++)
            {
                for (int j = 0; j <= 1080; j++)
                {
                    int num = i * 1081 + j;
                    backupHeights[num] = rawHeights[num];
                }
            }
        }
        protected override void Awake()
        {
            base.Awake();
            ModeSettings = new Dictionary<Mode, ToolSettings>();
            ModeSettings[Mode.Level] = new ToolSettings(25, 0.5f);
            ModeSettings[Mode.Shift] = new ToolSettings(25, 0.01f);
            ModeSettings[Mode.Soften] = new ToolSettings(50, 0.5f);
            ModeSettings[Mode.Slope] = new ToolSettings(25, 0.5f);

            this.m_undoList = new List<InGameTerrainTool.UndoStroke>();
            if (Singleton<LoadingManager>.exists)
            {
                Singleton<LoadingManager>.instance.m_levelLoaded += new LoadingManager.LevelLoadedHandler(this.OnLevelLoaded);
            }
        }
        public void ApplySettings()
        {
            this.m_strength = ModeSettings[m_mode].m_strength;
            this.m_brushSize = ModeSettings[m_mode].m_brushSize;
        }

        private void UpdateSettings()
        {
            ModeSettings[this.m_mode] = new ToolSettings(m_brushSize, m_strength);
        }

        protected override void OnToolGUI()
        {
            Event current = Event.current;

            if (!this.m_toolController.IsInsideUI && current.type == EventType.MouseDown)
            {
                if (current.button == 0)
                {
                    m_lastCash = EconomyManager.instance.LastCashAmount;


                    this.m_mouseLeftDown = true;
                    this.m_endPosition = this.m_mousePosition;
                }
                else if (current.button == 1)
                {
                    if (this.m_mode == InGameTerrainTool.Mode.Shift || this.m_mode == InGameTerrainTool.Mode.Soften)
                    {
                        this.m_mouseRightDown = true;
                    }
                    else if (this.m_mode == InGameTerrainTool.Mode.Level || this.m_mode == InGameTerrainTool.Mode.Slope)
                    {
                        this.m_startPosition = this.m_mousePosition;
                    }
                }
            }
            else if (current.type == EventType.MouseUp)
            {
                if (current.button == 0)
                {
                    this.m_mouseLeftDown = false;
                    if (!this.m_mouseRightDown)
                    {
                        this.m_strokeEnded = true;
                    }
                }
                else if (current.button == 1)
                {
                    this.m_mouseRightDown = false;
                    if (!this.m_mouseLeftDown)
                    {
                        this.m_strokeEnded = true;
                    }
                }
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown)
            {
                current.Use();
                this.enabled = false;

            }
            if (this.m_UndoKey.IsPressed(current) && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown && this.IsUndoAvailable())
            {
                this.Undo();
            }
            if (this.m_IncreaseBrushSizeKey.IsPressed(current) && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown)
            {
                m_brushSize = Mathf.Min(1250, m_brushSize + 5);
                UpdateSettings();
            }
            if (this.m_DecreaseBrushSizeKey.IsPressed(current) && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown)
            {
                m_brushSize = Mathf.Max(25, m_brushSize - 5);
                UpdateSettings();
            }
            if (this.m_IncreaseBrushStrengthKey.IsPressed(current) && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown)
            {
                m_strength = Mathf.Min(1, m_strength + 0.05f);
                UpdateSettings();
            }
            if (this.m_DecreaseBrushStrengthKey.IsPressed(current) && !this.m_undoRequest && !this.m_mouseLeftDown && !this.m_mouseRightDown)
            {
                m_strength = Mathf.Max(0.01f, m_strength - 0.05f);
                UpdateSettings();
            }
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            this.m_toolController.SetBrush(this.m_brush, this.m_mousePosition, this.m_brushSize);
            this.m_strokeXmin = 1080;
            this.m_strokeXmax = 0;
            this.m_strokeZmin = 1080;
            this.m_strokeZmax = 0;
            ushort[] backupHeights = TerrainManager.instance.BackupHeights;
            ushort[] rawHeights = TerrainManager.instance.RawHeights;
            for (int i = 0; i <= 1080; i++)
            {
                for (int j = 0; j <= 1080; j++)
                {
                    int num = i * 1081 + j;
                    backupHeights[num] = rawHeights[num];
                }
            }
            TerrainManager.instance.TransparentWater = true;
        }
        private void OnLevelLoaded(SimulationManager.UpdateMode mode)
        {
            this.ResetUndoBuffer();
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            base.ToolCursor = null;
            this.m_toolController.SetBrush(null, Vector3.zero, 1f);
            this.m_mouseLeftDown = false;
            this.m_mouseRightDown = false;
            this.m_mouseRayValid = false;
            Singleton<TerrainManager>.instance.TransparentWater = false;
            ResetUndoBuffer();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Singleton<LoadingManager>.exists)
            {
                Singleton<LoadingManager>.instance.m_levelLoaded -= new LoadingManager.LevelLoadedHandler(this.OnLevelLoaded);
            }
        }
        protected override void OnToolUpdate()
        {
            switch (this.m_mode)
            {
                case InGameTerrainTool.Mode.Shift:
                    base.ToolCursor = this.m_shiftCursor;
                    break;
                case InGameTerrainTool.Mode.Level:
                    base.ToolCursor = this.m_levelCursor;
                    break;
                case InGameTerrainTool.Mode.Soften:
                    base.ToolCursor = this.m_softenCursor;
                    break;
                case InGameTerrainTool.Mode.Slope:
                    base.ToolCursor = this.m_slopeCursor;
                    break;
            }
        }

        protected override void OnToolLateUpdate()
        {
            if (EconomyManager.instance.LastCashAmount == Int64.MaxValue)
            {
                m_free = true;
            }
            else
            {
                m_free = false;
            }

            Vector3 mousePosition = Input.mousePosition;
            this.m_mouseRay = Camera.main.ScreenPointToRay(mousePosition);
            this.m_mouseRayLength = Camera.main.farClipPlane;
            this.m_mouseRayValid = (!this.m_toolController.IsInsideUI && Cursor.visible);
            this.m_toolController.SetBrush(this.m_brush, this.m_mousePosition, this.m_brushSize);
        }

        public override void SimulationStep()
        {
            ToolBase.RaycastInput input = new ToolBase.RaycastInput(this.m_mouseRay, this.m_mouseRayLength);
            ToolBase.RaycastOutput raycastOutput;
            if (this.m_undoRequest && !this.m_strokeInProgress)
            {
                this.ApplyUndo();
                this.m_undoRequest = false;
            }
            else if (this.m_strokeEnded)
            {
                this.EndStroke();
                this.m_strokeEnded = false;
                this.m_strokeInProgress = false;
            }
            else if (this.m_mouseRayValid && ToolBase.RayCast(input, out raycastOutput))
            {
                this.m_mousePosition = raycastOutput.m_hitPos;
                if (this.m_mouseLeftDown != this.m_mouseRightDown)
                {
                    this.m_strokeInProgress = true;
                    ApplyBrush();
                }
            }
        }


        private int GetFreeUndoSpace()
        {
            int num = Singleton<TerrainManager>.instance.UndoBuffer.Length;
            if (this.m_undoList.Count > 0)
            {
                return (num + this.m_undoList[0].pointer - this.m_undoBufferFreePointer) % num - 1;
            }
            return num - 1;
        }
        private void EndStroke()
        {
            int num = Singleton<TerrainManager>.instance.UndoBuffer.Length;
            int num2 = Math.Max(0, 1 + this.m_strokeXmax - this.m_strokeXmin) * Math.Max(0, 1 + this.m_strokeZmax - this.m_strokeZmin);
            if (num2 < 1)
            {
                return;
            }
            int num3 = 0;
            while (this.GetFreeUndoSpace() < num2 && num3 < 10000)
            {
                this.m_undoList.RemoveAt(0);
                num3++;
            }
            if (num3 >= 10000)
            {
                Debug.Log("InGameTerrainTool:EndStroke: unexpectedly terminated freeing loop, might be a bug.");
                return;
            }
            InGameTerrainTool.UndoStroke item = default(InGameTerrainTool.UndoStroke);
            item.xmin = this.m_strokeXmin;
            item.xmax = this.m_strokeXmax;
            item.zmin = this.m_strokeZmin;
            item.zmax = this.m_strokeZmax;
            item.total_cost = (int)m_totalCost;
            item.pointer = this.m_undoBufferFreePointer;
            this.m_undoList.Add(item);
            ushort[] undoBuffer = Singleton<TerrainManager>.instance.UndoBuffer;
            ushort[] backupHeights = Singleton<TerrainManager>.instance.BackupHeights;
            ushort[] rawHeights = Singleton<TerrainManager>.instance.RawHeights;
            for (int i = this.m_strokeZmin; i <= this.m_strokeZmax; i++)
            {
                for (int j = this.m_strokeXmin; j <= this.m_strokeXmax; j++)
                {
                    int num4 = i * 1081 + j;
                    undoBuffer[this.m_undoBufferFreePointer++] = backupHeights[num4];
                    backupHeights[num4] = rawHeights[num4];
                    this.m_undoBufferFreePointer %= num;
                }
            }
            this.m_strokeXmin = 1080;
            this.m_strokeXmax = 0;
            this.m_strokeZmin = 1080;
            this.m_strokeZmax = 0;
            this.m_totalCost = 0;
        }
        public void ApplyUndo()
        {
            if (this.m_undoList.Count < 1)
            {
                return;
            }
            InGameTerrainTool.UndoStroke undoStroke = this.m_undoList[this.m_undoList.Count - 1];
            this.m_undoList.RemoveAt(this.m_undoList.Count - 1);
            ushort[] undoBuffer = Singleton<TerrainManager>.instance.UndoBuffer;
            ushort[] backupHeights = Singleton<TerrainManager>.instance.BackupHeights;
            ushort[] rawHeights = Singleton<TerrainManager>.instance.RawHeights;
            int num = Singleton<TerrainManager>.instance.UndoBuffer.Length;
            int num2 = Singleton<TerrainManager>.instance.RawHeights.Length;
            int num3 = undoStroke.pointer;
            for (int i = undoStroke.zmin; i <= undoStroke.zmax; i++)
            {
                for (int j = undoStroke.xmin; j <= undoStroke.xmax; j++)
                {
                    int num4 = i * 1081 + j;
                    rawHeights[num4] = undoBuffer[num3];
                    backupHeights[num4] = undoBuffer[num3];
                    num3++;
                    num3 %= num;
                }
            }
            this.m_undoBufferFreePointer = undoStroke.pointer;
            for (int k = 0; k < num2; k++)
            {
                backupHeights[k] = rawHeights[k];
            }
            int num5 = 128;
            undoStroke.xmin = Math.Max(0, undoStroke.xmin - 2);
            undoStroke.xmax = Math.Min(1080, undoStroke.xmax + 2);
            undoStroke.zmin = Math.Max(0, undoStroke.zmin - 2);
            undoStroke.zmax = Math.Min(1080, undoStroke.zmax + 2);
            for (int l = undoStroke.zmin; l <= undoStroke.zmax; l += num5 + 1)
            {
                for (int m = undoStroke.xmin; m <= undoStroke.xmax; m += num5 + 1)
                {
                    TerrainModify.UpdateArea(m, l, m + num5, l + num5, true, false, false);
                }
            }
            this.m_strokeXmin = 1080;
            this.m_strokeXmax = 0;
            this.m_strokeZmin = 1080;
            this.m_strokeZmax = 0;
            if ( m_free != true )
            {
                EconomyManager.instance.FetchResource(EconomyManager.Resource.Construction, -undoStroke.total_cost, ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
            }

        }
        private void ApplyBrush()
        {
            long m_applyCost = 0;
            bool outOfMoney = false;
            bool applied = false;
            float[] brushData = this.m_toolController.BrushData;
            float num = this.m_brushSize * 0.5f;
            float num2 = 16f;
            int num3 = 1080;
            ushort[] rawHeights = TerrainManager.instance.RawHeights;
            ushort[] finalHeights = TerrainManager.instance.FinalHeights;

            float strength = this.m_strength;
            int smoothStrength = 3;

            float num5 = 0.015625f;
            float num6 = 64f;

            Vector3 mousePosition = this.m_mousePosition;
            Vector3 vector = this.m_endPosition - this.m_startPosition;
            vector.y = 0f;
            float num7 = vector.sqrMagnitude;
            if (num7 != 0f)
            {
                num7 = 1f / num7;
            }
            float num8 = 20f;
            int minX = Mathf.Max((int)((mousePosition.x - num) / num2 + (float)num3 * 0.5f), 0);
            int minZ = Mathf.Max((int)((mousePosition.z - num) / num2 + (float)num3 * 0.5f), 0);
            int maxX = Mathf.Min((int)((mousePosition.x + num) / num2 + (float)num3 * 0.5f) + 1, num3);
            int maxZ = Mathf.Min((int)((mousePosition.z + num) / num2 + (float)num3 * 0.5f) + 1, num3);
            if (this.m_mode == InGameTerrainTool.Mode.Shift)
            {
                if (this.m_mouseRightDown)
                {
                    num8 = -num8;
                }
            }
            else if (this.m_mode == InGameTerrainTool.Mode.Soften && this.m_mouseRightDown)
            {
                smoothStrength = 10;
            }
            for (int i = minZ; i <= maxZ; i++)
            {
                float num13 = (((float)i - (float)num3 * 0.5f) * num2 - mousePosition.z + num) / this.m_brushSize * 64f - 0.5f;
                int num14 = Mathf.Clamp(Mathf.FloorToInt(num13), 0, 63);
                int num15 = Mathf.Clamp(Mathf.CeilToInt(num13), 0, 63);
                for (int j = minX; j <= maxX; j++)
                {
                    float num16 = (((float)j - (float)num3 * 0.5f) * num2 - mousePosition.x + num) / this.m_brushSize * 64f - 0.5f;
                    int num17 = Mathf.Clamp(Mathf.FloorToInt(num16), 0, 63);
                    int num18 = Mathf.Clamp(Mathf.CeilToInt(num16), 0, 63);
                    float num19 = brushData[num14 * 64 + num17];
                    float num20 = brushData[num14 * 64 + num18];
                    float num21 = brushData[num15 * 64 + num17];
                    float num22 = brushData[num15 * 64 + num18];
                    float num23 = num19 + (num20 - num19) * (num16 - (float)num17);
                    float num24 = num21 + (num22 - num21) * (num16 - (float)num17);
                    float num25 = num23 + (num24 - num23) * (num13 - (float)num14);
                    float num26 = (float)rawHeights[i * (num3 + 1) + j] * num5;
                    float num27 = 0f;
                    if (this.m_mode == InGameTerrainTool.Mode.Shift)
                    {
                        num27 = num26 + num8;
                    }
                    else if (this.m_mode == InGameTerrainTool.Mode.Level)
                    {
                        num27 = this.m_startPosition.y;
                    }
                    else if (this.m_mode == InGameTerrainTool.Mode.Soften)
                    {
                        int num28 = Mathf.Max(j - smoothStrength, 0);
                        int num29 = Mathf.Max(i - smoothStrength, 0);
                        int num30 = Mathf.Min(j + smoothStrength, num3);
                        int num31 = Mathf.Min(i + smoothStrength, num3);
                        float num32 = 0f;
                        for (int k = num29; k <= num31; k++)
                        {
                            for (int l = num28; l <= num30; l++)
                            {
                                float num33 = 1f - (float)((l - j) * (l - j) + (k - i) * (k - i)) / (float)(smoothStrength * smoothStrength);
                                if (num33 > 0f)
                                {
                                    num27 += (float)finalHeights[k * (num3 + 1) + l] * (num5 * num33);
                                    num32 += num33;
                                }
                            }
                        }
                        num27 /= num32;
                    }
                    else if (this.m_mode == InGameTerrainTool.Mode.Slope)
                    {
                        float num34 = ((float)j - (float)num3 * 0.5f) * num2;
                        float num35 = ((float)i - (float)num3 * 0.5f) * num2;
                        float t = ((num34 - this.m_startPosition.x) * vector.x + (num35 - this.m_startPosition.z) * vector.z) * num7;
                        num27 = Mathf.Lerp(this.m_startPosition.y, this.m_endPosition.y, t);
                    }
                    num27 = Mathf.Lerp(num26, num27, strength * num25);
                    ushort orig = rawHeights[i * (num3 + 1) + j];


                    var oldHeight = rawHeights[i * (num3 + 1) + j];
                    var newHeight = (ushort)Mathf.Clamp(Mathf.RoundToInt(num27 * num6), 0, 65535);

                    if (!outOfMoney)
                        m_applyCost += Mathf.Abs(newHeight - oldHeight) * m_costMultiplier;

                    if ((m_applyCost + m_totalCost < m_lastCash && m_applyCost + m_totalCost < Int32.MaxValue) || m_free == true)
                    {
                        rawHeights[i * (num3 + 1) + j] = newHeight;
                        this.m_strokeXmin = Math.Min(this.m_strokeXmin, j);
                        this.m_strokeXmax = Math.Max(this.m_strokeXmax, j);
                        this.m_strokeZmin = Math.Min(this.m_strokeZmin, i);
                        this.m_strokeZmax = Math.Max(this.m_strokeZmax, i);
                        applied = true;
                    }
                    else
                    {
                        outOfMoney = true;
                    }
                    
                }
            }
            TerrainModify.UpdateArea(minX, minZ, maxX, maxZ, true, false, false);
            if(applied)
            {
                if (m_free != true)
                {
                    EconomyManager.instance.FetchResource(EconomyManager.Resource.Construction, (int)m_applyCost, ItemClass.Service.None, ItemClass.SubService.None, ItemClass.Level.None);
                    m_totalCost += m_applyCost;
                }
            }
            


        }
    }

}