// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace GaussianSplatting.Runtime
{
    class GaussianSplatRenderSystem
    {
        // ReSharper disable MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        internal static readonly ProfilerMarker s_ProfDraw = new(ProfilerCategory.Render, "GaussianSplat.Draw", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCompose = new(ProfilerCategory.Render, "GaussianSplat.Compose", MarkerFlags.SampleGPU);
        internal static readonly ProfilerMarker s_ProfCalcView = new(ProfilerCategory.Render, "GaussianSplat.CalcView", MarkerFlags.SampleGPU);
        // ReSharper restore MemberCanBePrivate.Global

        public static GaussianSplatRenderSystem instance => ms_Instance ??= new GaussianSplatRenderSystem();
        static GaussianSplatRenderSystem ms_Instance;

        readonly Dictionary<GaussianSplatRenderer, MaterialPropertyBlock> m_Splats = new();
        readonly HashSet<Camera> m_CameraCommandBuffersDone = new();
        readonly List<(GaussianSplatRenderer, MaterialPropertyBlock)> m_ActiveSplats = new();

        CommandBuffer m_CommandBuffer;

        public Camera gsCamera;

        public void RegisterSplat(GaussianSplatRenderer r)
        {
            if (m_Splats.Count == 0)
            {
                if (GraphicsSettings.currentRenderPipeline == null)
                    Camera.onPreCull += OnPreCullCamera;
            }

            m_Splats.Add(r, new MaterialPropertyBlock());
        }

        public void UnregisterSplat(GaussianSplatRenderer r)
        {
            if (!m_Splats.ContainsKey(r))
                return;
            m_Splats.Remove(r);
            if (m_Splats.Count == 0)
            {
                if (m_CameraCommandBuffersDone != null)
                {
                    if (m_CommandBuffer != null)
                    {
                        foreach (var cam in m_CameraCommandBuffersDone)
                        {
                            if (cam)
                                cam.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                        }
                    }
                    m_CameraCommandBuffersDone.Clear();
                }

                m_ActiveSplats.Clear();
                m_CommandBuffer?.Dispose();
                m_CommandBuffer = null;
                Camera.onPreCull -= OnPreCullCamera;
            }
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        public bool GatherSplatsForCamera(Camera cam)
        {
            if (cam.cameraType == CameraType.Preview)
                return false;
            // gather all active & valid splat objects
            m_ActiveSplats.Clear();
            foreach (var kvp in m_Splats)
            {
                var gs = kvp.Key;
                if (gs == null || !gs.isActiveAndEnabled || !gs.HasValidAsset || !gs.HasValidRenderSetup)
                    continue;
                m_ActiveSplats.Add((kvp.Key, kvp.Value));
            }
            if (m_ActiveSplats.Count == 0)
                return false;

            // sort them by depth from camera
            var camTr = cam.transform;
            m_ActiveSplats.Sort((a, b) =>
            {
                var trA = a.Item1.transform;
                var trB = b.Item1.transform;
                var posA = camTr.InverseTransformPoint(trA.position);
                var posB = camTr.InverseTransformPoint(trB.position);
                return posA.z.CompareTo(posB.z);
            });

            return true;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        public Material SortAndRenderSplats(Camera cam, CommandBuffer cmb)
        {
            
            Material matComposite = null;
            foreach (var kvp in m_ActiveSplats)
            {
                var gs = kvp.Item1;
                matComposite = gs.m_MatComposite;
                var mpb = kvp.Item2;

                // sort
                var matrix = gs.transform.localToWorldMatrix;
                if (gs.m_FrameCounter % gs.m_SortNthFrame == 0)
                    gs.SortPoints(cmb, cam, matrix);
                ++gs.m_FrameCounter;

                // cache view
                kvp.Item2.Clear();
                Material displayMat = gs.m_RenderMode switch
                {
                    GaussianSplatRenderer.RenderMode.DebugPoints => gs.m_MatDebugPoints,
                    GaussianSplatRenderer.RenderMode.DebugPointIndices => gs.m_MatDebugPoints,
                    GaussianSplatRenderer.RenderMode.DebugBoxes => gs.m_MatDebugBoxes,
                    GaussianSplatRenderer.RenderMode.DebugChunkBounds => gs.m_MatDebugBoxes,
                    _ => gs.m_MatSplats
                };
                if (displayMat == null)
                    continue;

                mpb.SetInteger(GaussianSplatRenderer.Props.DrawSplats, gs.m_DrawSplats ? 1 : 0);
                
                gs.SetAssetDataOnMaterial(mpb);
                mpb.SetBuffer(GaussianSplatRenderer.Props.SplatChunks, gs.m_GpuChunks);
                
                mpb.SetBuffer(GaussianSplatRenderer.Props.SplatViewData, gs.m_GpuView);

                mpb.SetBuffer(GaussianSplatRenderer.Props.OrderBuffer, gs.m_GpuSortKeys);
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatScale, gs.m_SplatScale);
                
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatOpacityScale, gs.m_OpacityScale);
                mpb.SetFloat(GaussianSplatRenderer.Props.SplatSize, gs.m_PointDisplaySize);
                mpb.SetInteger(GaussianSplatRenderer.Props.SHOrder, gs.m_SHOrder);
                mpb.SetInteger(GaussianSplatRenderer.Props.SHOnly, gs.m_SHOnly ? 1 : 0);
                mpb.SetInteger(GaussianSplatRenderer.Props.IgnoreGrid, gs.m_IgnoreGrid ? 1 : 0);
                mpb.SetInteger(GaussianSplatRenderer.Props.DisplayIndex, gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugPointIndices ? 1 : 0);
                mpb.SetInteger(GaussianSplatRenderer.Props.DisplayChunks, gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugChunkBounds ? 1 : 0);

                if (gs.m_Cutouts.Length != 0 && gs.m_Cutouts[0] != null)
                {
                    displayMat.SetMatrix(GaussianSplatRenderer.Props.SplatCutouts, gs.m_Cutouts[0].transform.worldToLocalMatrix * gs.transform.localToWorldMatrix);   
                }
                
                // additional dimmers for each band 
                mpb.SetFloat(GaussianSplatRenderer.Props.DimmerSH0, gs.m_DimmerSH0);
                mpb.SetFloat(GaussianSplatRenderer.Props.DimmerSH1, gs.m_DimmerSH1);
                mpb.SetFloat(GaussianSplatRenderer.Props.DimmerSH2, gs.m_DimmerSH2);
                mpb.SetFloat(GaussianSplatRenderer.Props.DimmerSH3, gs.m_DimmerSH3);
                
                mpb.SetFloat(GaussianSplatRenderer.Props.Hue, gs.m_Hue);
                mpb.SetFloat(GaussianSplatRenderer.Props.Saturation, gs.m_Saturation);
                mpb.SetFloat(GaussianSplatRenderer.Props.Brightness, gs.m_Brightness);
                
                // interpolation slider
                mpb.SetFloat(GaussianSplatRenderer.Props.InterpolationValue, gs.m_InterpolationValue);
                
                mpb.SetVector(GaussianSplatRenderer.Props.GridLeftLowerCornerPosition, gs.m_3DGridLeftLowerCornerPosition);
                mpb.SetInt(GaussianSplatRenderer.Props.GridWidth, gs.m_3DGridWidth);
                mpb.SetInt(GaussianSplatRenderer.Props.GridHeight, gs.m_3DGridHeight);
                mpb.SetInt(GaussianSplatRenderer.Props.GridDepth, gs.m_3DGridDepth);
                mpb.SetFloat(GaussianSplatRenderer.Props.GridCellWidth, gs.m_3DGridCellWidth);
                
                cmb.BeginSample(s_ProfCalcView);
                gs.CalcViewData(cmb, cam, matrix);
                cmb.EndSample(s_ProfCalcView);

                // draw
                int indexCount = 6;
                int instanceCount = gs.splatCount;
                MeshTopology topology = MeshTopology.Triangles;
                if (gs.m_RenderMode is GaussianSplatRenderer.RenderMode.DebugBoxes or GaussianSplatRenderer.RenderMode.DebugChunkBounds)
                    indexCount = 36;
                if (gs.m_RenderMode == GaussianSplatRenderer.RenderMode.DebugChunkBounds)
                    instanceCount = gs.m_GpuChunksValid ? gs.m_GpuChunks.count : 0;

                cmb.BeginSample(s_ProfDraw);
                cmb.DrawProcedural(gs.m_GpuIndexBuffer, matrix, displayMat, 0, topology, indexCount, instanceCount, mpb);
                cmb.EndSample(s_ProfDraw);
            }
            return matComposite;
        }

        // ReSharper disable once MemberCanBePrivate.Global - used by HDRP/URP features that are not always compiled
        // ReSharper disable once UnusedMethodReturnValue.Global - used by HDRP/URP features that are not always compiled
        public CommandBuffer InitialClearCmdBuffer(Camera cam)
        {
            m_CommandBuffer ??= new CommandBuffer {name = "RenderGaussianSplats"};
            if (GraphicsSettings.currentRenderPipeline == null && cam != null && !m_CameraCommandBuffersDone.Contains(cam))
            {
                cam.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, m_CommandBuffer);
                m_CameraCommandBuffersDone.Add(cam);
            }

            // get render target for all splats
            m_CommandBuffer.Clear();
            return m_CommandBuffer;
        }

        void OnPreCullCamera(Camera cam)
        {
            if (!GatherSplatsForCamera(cam))
                return;

            InitialClearCmdBuffer(cam);

            m_CommandBuffer.GetTemporaryRT(GaussianSplatRenderer.Props.GaussianSplatRT, -1, -1, 0, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat);
            m_CommandBuffer.SetRenderTarget(GaussianSplatRenderer.Props.GaussianSplatRT, BuiltinRenderTextureType.CurrentActive);
            m_CommandBuffer.ClearRenderTarget(RTClearFlags.Color, new Color(0, 0, 0, 0), 0, 0);

            // add sorting, view calc and drawing commands for each splat object
            Material matComposite = SortAndRenderSplats(cam, m_CommandBuffer);

            // compose
            m_CommandBuffer.BeginSample(s_ProfCompose);
            m_CommandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            m_CommandBuffer.DrawProcedural(Matrix4x4.identity, matComposite, 0, MeshTopology.Triangles, 3, 1);
            m_CommandBuffer.EndSample(s_ProfCompose);
            m_CommandBuffer.ReleaseTemporaryRT(GaussianSplatRenderer.Props.GaussianSplatRT);
        }
    }

    [ExecuteInEditMode]
    public class GaussianSplatRenderer : MonoBehaviour // This scripts sits on Gaussian Splat Renderer object, where the PLY file is dragged into.
    {
        public enum RenderMode
        {
            Splats,
            DebugPoints,
            DebugPointIndices,
            DebugBoxes,
            DebugChunkBounds,
        }

        public bool m_DrawSplats;
        public GaussianSplatAsset m_Asset;
        public GaussianSplatAsset m_Asset2;
        [Range(0f,1f)]public float m_InterpolationValue;

        [Range(0.1f, 2.0f)] [Tooltip("Additional scaling factor for the splats")]
        public float m_SplatScale = 1.0f;
        [Range(0.05f, 20.0f)]
        [Tooltip("Additional scaling factor for opacity")]
        public float m_OpacityScale = 1.0f;
        [Range(0, 3)] [Tooltip("Spherical Harmonics order to use")]
        public int m_SHOrder = 3;
        [Tooltip("Show only Spherical Harmonics contribution, using gray color")]
        public bool m_SHOnly;
        [Range(1,30)] [Tooltip("Sort splats only every N frames")]
        public int m_SortNthFrame = 1;

        public RenderMode m_RenderMode = RenderMode.Splats;
        [Range(1.0f,15.0f)] public float m_PointDisplaySize = 3.0f;

        public GaussianCutout[] m_Cutouts;

        public Shader m_ShaderSplats;
        public Shader m_ShaderComposite;
        public Shader m_ShaderDebugPoints;
        public Shader m_ShaderDebugBoxes;
        [Tooltip("Gaussian splatting compute shader")]
        public ComputeShader m_CSSplatUtilities;

        int m_SplatCount; // initially same as asset splat count, but editing can change this
        GraphicsBuffer m_GpuSortDistances;          
        internal GraphicsBuffer m_GpuSortKeys;      
        GraphicsBuffer m_GpuPosData;
        GraphicsBuffer m_GpuPosData2;
        public GraphicsBuffer m_GpuOtherData;          
        public GraphicsBuffer m_GpuOtherData2;
        GraphicsBuffer m_GpuSHData;             
        GraphicsBuffer m_GpuSHData2;
        Texture m_GpuColorData;
        Texture m_GpuColorData2;
        internal GraphicsBuffer m_GpuChunks;    
        internal bool m_GpuChunksValid;         
        internal GraphicsBuffer m_GpuView;
        
        internal GraphicsBuffer m_GpuIndexBuffer;   

        // We add a proxy camera object (PCO), which mimics the position of a camera - this position is passed on to SplatUtilities.compute and replaces _VecWorldSpaceCameraPos
        // by the position of the proxy camera object (so that the real camera can have a fix position), this behaviour can be turned on or off
        public Camera m_ProxyCameraObject;
        public bool m_UseProxyCamObj;
        public Camera m_Camera;
        
        // adding adjustable variables for dimming
        [Range(0.6f,1.2f)]
        [Tooltip("This is a factor, by which the color is multiplied - it acts as a dimmer.")]
        public float m_DimmerSH0;

        [Range(0.0f, 2.0f)]
        [Tooltip("This is a factor for the summand added for SH1.")]
        public float m_DimmerSH1;
        
        [Range(0.0f, 2.0f)]
        [Tooltip("This is a factor for the summand added for SH2.")]
        public float m_DimmerSH2;
        
        [Range(0.0f, 2.0f)]
        [Tooltip("This is a factor for the summand added for SH3.")]
        public float m_DimmerSH3;
        
        // adding color temperature based on https://tannerhelland.com/2012/09/18/convert-temperature-rgb-algorithm-code.html
        [Range(-0.4f, 0.4f)] public float m_Hue;
        [Range(-0.4f, 0.4f)] public float m_Saturation;
        [Range(-0.4f, 0.4f)] public float m_Brightness;
        
        // adding grid structure, which gives a framework for the analysis of gs scenes
        public Vector3 m_3DGridLeftLowerCornerPosition;
        public Vector3 m_3DGridLeftLowerCornerRotation;
        public int m_3DGridWidth;
        public int m_3DGridHeight;
        public int m_3DGridDepth;
        public float m_3DGridCellWidth;
        public bool m_ShowGrid;
        public bool m_ShowGridIndices;
        public bool m_IgnoreGrid;
        public GameObject m_debugPrefabSphere;
        public int m_CameraIndex = 0;

        //[Range(0f,1f)]
        //public float m_InterpolationSlider;
        
        // grid data buffer
        private ComputeBuffer m_gridDataBuffer;
        
        // these buffers are only for splat editing, and are lazily created 
        GraphicsBuffer m_GpuEditCutouts;
        GraphicsBuffer m_GpuEditCountsBounds;
        GraphicsBuffer m_GpuEditSelected;
        GraphicsBuffer m_GpuEditDeleted;
        GraphicsBuffer m_GpuEditSelectedMouseDown; // selection state at start of operation
        GraphicsBuffer m_GpuEditPosMouseDown; // position state at start of operation
        GraphicsBuffer m_GpuEditOtherMouseDown; // rotation/scale state at start of operation

        GpuSorting m_Sorter;
        GpuSorting.Args m_SorterArgs;

        internal Material m_MatSplats;
        internal Material m_MatComposite;
        internal Material m_MatDebugPoints;
        internal Material m_MatDebugBoxes;

        internal int m_FrameCounter;
        GaussianSplatAsset m_PrevAsset;
        Hash128 m_PrevHash;

        static readonly ProfilerMarker s_ProfSort = new(ProfilerCategory.Render, "GaussianSplat.Sort", MarkerFlags.SampleGPU);

        // These are the variables stored as integer, in order to facilitate and secure the usage of shader variables.
        internal static class Props
        {
            public static readonly int DrawSplats = Shader.PropertyToID("_DrawSplats");
            
            public static readonly int SplatPos = Shader.PropertyToID("_SplatPos");
            public static readonly int SplatPos2 = Shader.PropertyToID("_SplatPos2");
            
            public static readonly int SplatOther = Shader.PropertyToID("_SplatOther");
            public static readonly int SplatOther2 = Shader.PropertyToID("_SplatOther2");
            
            public static readonly int SplatSH = Shader.PropertyToID("_SplatSH");
            public static readonly int SplatSH2 = Shader.PropertyToID("_SplatSH2");
            
            public static readonly int SplatColor = Shader.PropertyToID("_SplatColor");
            public static readonly int SplatColor2 = Shader.PropertyToID("_SplatColor2");
            
            public static readonly int SplatSelectedBits = Shader.PropertyToID("_SplatSelectedBits");
            public static readonly int SplatDeletedBits = Shader.PropertyToID("_SplatDeletedBits");
            public static readonly int SplatBitsValid = Shader.PropertyToID("_SplatBitsValid");
            public static readonly int SplatFormat = Shader.PropertyToID("_SplatFormat");
            public static readonly int SplatChunks = Shader.PropertyToID("_SplatChunks");
            public static readonly int SplatChunkCount = Shader.PropertyToID("_SplatChunkCount");
            public static readonly int SplatViewData = Shader.PropertyToID("_SplatViewData");
            
            public static readonly int OrderBuffer = Shader.PropertyToID("_OrderBuffer");
            public static readonly int SplatScale = Shader.PropertyToID("_SplatScale");
            public static readonly int SplatOpacityScale = Shader.PropertyToID("_SplatOpacityScale");
            public static readonly int SplatSize = Shader.PropertyToID("_SplatSize");
            public static readonly int SplatCount = Shader.PropertyToID("_SplatCount");
            public static readonly int SHOrder = Shader.PropertyToID("_SHOrder");
            public static readonly int SHOnly = Shader.PropertyToID("_SHOnly");
            public static readonly int DisplayIndex = Shader.PropertyToID("_DisplayIndex");
            public static readonly int DisplayChunks = Shader.PropertyToID("_DisplayChunks");
            public static readonly int GaussianSplatRT = Shader.PropertyToID("_GaussianSplatRT");
            public static readonly int SplatSortKeys = Shader.PropertyToID("_SplatSortKeys");
            public static readonly int SplatSortDistances = Shader.PropertyToID("_SplatSortDistances");
            public static readonly int SrcBuffer = Shader.PropertyToID("_SrcBuffer");
            public static readonly int DstBuffer = Shader.PropertyToID("_DstBuffer");
            public static readonly int BufferSize = Shader.PropertyToID("_BufferSize");
            public static readonly int MatrixVP = Shader.PropertyToID("_MatrixVP");
            public static readonly int MatrixMV = Shader.PropertyToID("_MatrixMV");
            public static readonly int MatrixP = Shader.PropertyToID("_MatrixP");
            public static readonly int MatrixObjectToWorld = Shader.PropertyToID("_MatrixObjectToWorld");
            public static readonly int MatrixWorldToObject = Shader.PropertyToID("_MatrixWorldToObject");
            public static readonly int VecScreenParams = Shader.PropertyToID("_VecScreenParams");
            public static readonly int VecWorldSpaceCameraPos = Shader.PropertyToID("_VecWorldSpaceCameraPos");
            public static readonly int SelectionCenter = Shader.PropertyToID("_SelectionCenter");
            public static readonly int SelectionDelta = Shader.PropertyToID("_SelectionDelta");
            public static readonly int SelectionDeltaRot = Shader.PropertyToID("_SelectionDeltaRot");
            public static readonly int SplatCutoutsCount = Shader.PropertyToID("_SplatCutoutsCount");
            public static readonly int SplatCutouts = Shader.PropertyToID("_SplatCutouts");
            public static readonly int SelectionMode = Shader.PropertyToID("_SelectionMode");
            public static readonly int SplatPosMouseDown = Shader.PropertyToID("_SplatPosMouseDown");
            public static readonly int SplatOtherMouseDown = Shader.PropertyToID("_SplatOtherMouseDown");

            public static readonly int DimmerSH0 = Shader.PropertyToID("_DimmerSH0");
            public static readonly int DimmerSH1 = Shader.PropertyToID("_DimmerSH1");
            public static readonly int DimmerSH2 = Shader.PropertyToID("_DimmerSH2");
            public static readonly int DimmerSH3 = Shader.PropertyToID("_DimmerSH3");

            // hsv color palette for gaussian adjustments
            public static readonly int Hue = Shader.PropertyToID("_Hue");
            public static readonly int Saturation = Shader.PropertyToID("_Saturation");
            public static readonly int Brightness = Shader.PropertyToID("_Brightness");
            
            // interpolation slider
            public static readonly int InterpolationValue = Shader.PropertyToID("_InterpolationValue");
            
            // Grid properties
            public static readonly int GridLeftLowerCornerPosition =
                Shader.PropertyToID("_3DGridLeftLowerCornerPosition");
            public static readonly int GridLeftLowerCornerRotation =
                Shader.PropertyToID("_3DGridLeftLowerCornerRotation");
            public static readonly int GridWidth = Shader.PropertyToID("_3DGridWidth");
            public static readonly int GridHeight = Shader.PropertyToID("_3DGridHeight");
            public static readonly int GridDepth = Shader.PropertyToID("_3DGridDepth");
            public static readonly int GridCellWidth = Shader.PropertyToID("_3DGridCellWidth");
            public static readonly int IgnoreGrid = Shader.PropertyToID("_IgnoreGrid");
        }

        [field: NonSerialized] public bool editModified { get; private set; }
        [field: NonSerialized] public uint editSelectedSplats { get; private set; }
        [field: NonSerialized] public uint editDeletedSplats { get; private set; }
        [field: NonSerialized] public uint editCutSplats { get; private set; }
        [field: NonSerialized] public Bounds editSelectedBounds { get; private set; }

        public GaussianSplatAsset asset => m_Asset;
        public GaussianSplatAsset asset2 => m_Asset2;
        public int splatCount => m_SplatCount;

        enum KernelIndices
        {
            SetIndices,
            CalcDistances,
            CalcViewData,
            UpdateEditData,
            InitEditData,
            ClearBuffer,
            InvertSelection,
            SelectAll,
            OrBuffers,
            SelectionUpdate,
            TranslateSelection,
            RotateSelection,
            ScaleSelection,
            ExportData,
            CopySplats
        }

        public bool HasValidAsset =>
            m_Asset != null &&
            m_Asset.splatCount > 0 &&
            m_Asset.formatVersion == GaussianSplatAsset.kCurrentVersion &&
            m_Asset.posData != null &&
            m_Asset.otherData != null &&
            m_Asset.shData != null &&
            m_Asset.colorData != null;
        public bool HasValidRenderSetup => m_GpuPosData != null && m_GpuOtherData != null && m_GpuChunks != null;

        const int kGpuViewDataSize = 40;

        void CreateResourcesForAsset(int assetIndex= 0)
        {
            if (!HasValidAsset)
                return;

            Debug.Log("Create Resources For Asset");
            
            m_SplatCount = asset.splatCount;
            m_GpuPosData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset.posData.dataSize / 4), 4) { name = "GaussianPosData" };
            m_GpuPosData.SetData(asset.posData.GetData<uint>());
            m_GpuPosData2 = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset2.posData.dataSize / 4), 4) { name = "GaussianPosData2" };
            m_GpuPosData2.SetData(asset2.posData.GetData<uint>());
            
            m_GpuOtherData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset.otherData.dataSize / 4), 4) { name = "GaussianOtherData" };
            m_GpuOtherData.SetData(asset.otherData.GetData<uint>());
            
            m_GpuOtherData2 = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, (int) (asset2.otherData.dataSize / 4), 4) { name = "GaussianOtherData2" };
            m_GpuOtherData2.SetData(asset2.otherData.GetData<uint>());
            
            m_GpuSHData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int) (asset.shData.dataSize / 4), 4) { name = "GaussianSHData" };
            m_GpuSHData.SetData(asset.shData.GetData<uint>());
            
            m_GpuSHData2 = new GraphicsBuffer(GraphicsBuffer.Target.Raw, (int) (asset2.shData.dataSize / 4), 4) { name = "GaussianSHData2" };
            m_GpuSHData2.SetData(asset.shData.GetData<uint>());
            
            var (texWidth, texHeight) = GaussianSplatAsset.CalcTextureSize(asset.splatCount);
            var texFormat = GaussianSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var tex = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.IgnoreMipmapLimit | TextureCreationFlags.DontUploadUponCreate) { name = "GaussianColorData" };
            tex.SetPixelData(asset.colorData.GetData<byte>(), 0);
            tex.Apply(false, true);
            m_GpuColorData = tex;
            
            var tex2 = new Texture2D(texWidth, texHeight, texFormat, TextureCreationFlags.DontInitializePixels | TextureCreationFlags.IgnoreMipmapLimit | TextureCreationFlags.DontUploadUponCreate) { name = "GaussianColorData2" };
            tex2.SetPixelData(asset2.colorData.GetData<byte>(), 0);
            tex2.Apply(false, true);
            m_GpuColorData2 = tex2;
            
            if (asset.chunkData != null && asset.chunkData.dataSize != 0)
            {
                m_GpuChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    (int) (asset.chunkData.dataSize / UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()),
                    UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()) {name = "GaussianChunkData"};
                m_GpuChunks.SetData(asset.chunkData.GetData<GaussianSplatAsset.ChunkInfo>());
                m_GpuChunksValid = true;
            }
            else
            {
                // just a dummy chunk buffer
                m_GpuChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1,
                    UnsafeUtility.SizeOf<GaussianSplatAsset.ChunkInfo>()) {name = "GaussianChunkData"};
                m_GpuChunksValid = false;
            }

            m_GpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_Asset.splatCount, kGpuViewDataSize);
            
            m_GpuIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, 36, 2);
            // cube indices, most often we use only the first quad
            m_GpuIndexBuffer.SetData(new ushort[]
            {
                0, 1, 2, 1, 3, 2,
                4, 6, 5, 5, 6, 7,
                0, 2, 4, 4, 2, 6,
                1, 5, 3, 5, 7, 3,
                0, 4, 1, 4, 5, 1,
                2, 3, 6, 3, 7, 6
            });

            InitSortBuffers(splatCount);
        }

        void InitSortBuffers(int count)
        {
            m_GpuSortDistances?.Dispose();
            m_GpuSortKeys?.Dispose();
            m_SorterArgs.resources.Dispose();

            m_GpuSortDistances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "GaussianSplatSortDistances" };
            m_GpuSortKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 4) { name = "GaussianSplatSortIndices" };

            // init keys buffer to splat indices
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.SetIndices, Props.SplatSortKeys, m_GpuSortKeys);
            m_CSSplatUtilities.SetInt(Props.SplatCount, m_GpuSortDistances.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.SetIndices, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.SetIndices, (m_GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

            m_SorterArgs.inputKeys = m_GpuSortDistances;
            m_SorterArgs.inputValues = m_GpuSortKeys;
            m_SorterArgs.count = (uint)count;
            if (m_Sorter.Valid)
                m_SorterArgs.resources = GpuSorting.SupportResources.Load((uint)count);
        }

        public void OnEnable()
        {
            m_FrameCounter = 0;
            if (m_ShaderSplats == null || m_ShaderComposite == null || m_ShaderDebugPoints == null || m_ShaderDebugBoxes == null || m_CSSplatUtilities == null)
                return;
            if (!SystemInfo.supportsComputeShaders)
                return;

            m_MatSplats = new Material(m_ShaderSplats) {name = "GaussianSplats"};
            m_MatComposite = new Material(m_ShaderComposite) {name = "GaussianClearDstAlpha"};
            m_MatDebugPoints = new Material(m_ShaderDebugPoints) {name = "GaussianDebugPoints"};
            m_MatDebugBoxes = new Material(m_ShaderDebugBoxes) {name = "GaussianDebugBoxes"};

            m_Sorter = new GpuSorting(m_CSSplatUtilities);
            GaussianSplatRenderSystem.instance.RegisterSplat(this);
            
            CreateResourcesForAsset();
            
            GetSplatDataInGrid();
        }
        
        void SetAssetDataOnCS(CommandBuffer cmb, KernelIndices kernel)
        {
            ComputeShader cs = m_CSSplatUtilities;
            int kernelIndex = (int) kernel;
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatPos, m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatPos2, m_GpuPosData2);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatChunks, m_GpuChunks);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatOther, m_GpuOtherData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatOther2, m_GpuOtherData2);
            
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSH, m_GpuSHData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSH2, m_GpuSHData2);
            
            cmb.SetComputeTextureParam(cs, kernelIndex, Props.SplatColor, m_GpuColorData);
            cmb.SetComputeTextureParam(cs, kernelIndex, Props.SplatColor2, m_GpuColorData2);
            
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);
            
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatViewData, m_GpuView);
            
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.OrderBuffer, m_GpuSortKeys);
            cmb.SetComputeIntParam(cs, Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            uint format = (uint)m_Asset.posFormat | ((uint)m_Asset.scaleFormat << 8) | ((uint)m_Asset.shFormat << 16);
            cmb.SetComputeIntParam(cs, Props.SplatFormat, (int)format);
            cmb.SetComputeIntParam(cs, Props.SplatCount, m_SplatCount);
            cmb.SetComputeIntParam(cs, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);

            UpdateCutoutsBuffer();
            cmb.SetComputeIntParam(cs, Props.SplatCutoutsCount, m_Cutouts?.Length ?? 0);
            cmb.SetComputeBufferParam(cs, kernelIndex, Props.SplatCutouts, m_GpuEditCutouts);
        }

        internal void SetAssetDataOnMaterial(MaterialPropertyBlock mat)
        {
            mat.SetBuffer(Props.SplatPos, m_GpuPosData);
            mat.SetBuffer(Props.SplatPos2, m_GpuPosData2);
            
            mat.SetBuffer(Props.SplatOther, m_GpuOtherData);
            mat.SetBuffer(Props.SplatOther2, m_GpuOtherData2);
            
            mat.SetBuffer(Props.SplatSH, m_GpuSHData);
            mat.SetBuffer(Props.SplatSH2, m_GpuSHData2);
            
            mat.SetTexture(Props.SplatColor, m_GpuColorData);
            mat.SetTexture(Props.SplatColor2, m_GpuColorData2);
            
            mat.SetBuffer(Props.SplatSelectedBits, m_GpuEditSelected ?? m_GpuPosData);
            mat.SetBuffer(Props.SplatDeletedBits, m_GpuEditDeleted ?? m_GpuPosData);
            mat.SetInt(Props.SplatBitsValid, m_GpuEditSelected != null && m_GpuEditDeleted != null ? 1 : 0);
            uint format = (uint)m_Asset.posFormat | ((uint)m_Asset.scaleFormat << 8) | ((uint)m_Asset.shFormat << 16);
            mat.SetInteger(Props.SplatFormat, (int)format);
            mat.SetInteger(Props.SplatCount, m_SplatCount);
            mat.SetInteger(Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
        }

        static void DisposeBuffer(ref GraphicsBuffer buf)
        {
            buf?.Dispose();
            buf = null;
        }

        void DisposeResourcesForAsset()
        {
            DestroyImmediate(m_GpuColorData);

            DisposeBuffer(ref m_GpuPosData);
            DisposeBuffer(ref m_GpuPosData2);
            
            DisposeBuffer(ref m_GpuOtherData);
            DisposeBuffer(ref m_GpuOtherData2);
            
            DisposeBuffer(ref m_GpuSHData);
            DisposeBuffer(ref m_GpuSHData2);
            
            DisposeBuffer(ref m_GpuChunks);

            DisposeBuffer(ref m_GpuView);
            
            DisposeBuffer(ref m_GpuIndexBuffer);
            DisposeBuffer(ref m_GpuSortDistances);
            DisposeBuffer(ref m_GpuSortKeys);

            DisposeBuffer(ref m_GpuEditSelectedMouseDown);
            DisposeBuffer(ref m_GpuEditPosMouseDown);
            DisposeBuffer(ref m_GpuEditOtherMouseDown);
            DisposeBuffer(ref m_GpuEditSelected);
            DisposeBuffer(ref m_GpuEditDeleted);
            DisposeBuffer(ref m_GpuEditCountsBounds);
            DisposeBuffer(ref m_GpuEditCutouts);
            
            m_SorterArgs.resources.Dispose();

            m_SplatCount = 0;
            m_GpuChunksValid = false;

            editSelectedSplats = 0;
            editDeletedSplats = 0;
            editCutSplats = 0;
            editModified = false;
            editSelectedBounds = default;
        }

        public void OnDisable()
        {
            DisposeResourcesForAsset();
            GaussianSplatRenderSystem.instance.UnregisterSplat(this);
            
            DestroyImmediate(m_MatSplats);
            DestroyImmediate(m_MatComposite);
            DestroyImmediate(m_MatDebugPoints);
            DestroyImmediate(m_MatDebugBoxes);
        }
        
        #region GridPropertyFunctions
        
        public struct SplatViewData
        {
            public float4 pos;
            public float2 axis1, axis2;
            public uint2 color;
        }
        
        struct SplatSHData
        {
            half3 col, sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, sh10, sh11, sh12, sh13, sh14, sh15;
        };
        
        // TODO: Add position and rotation Handle for the grid.
        // TODO: Decouple that functions from this scripts and make it a own class (package).
        // TODO: make the gridCellDepth parametrizable, so we are more flexible about the resolution in width x height
        
        private void OnDrawGizmos()
        {
            if (!m_ShowGrid)
                return;

            // Create rotation matrix
            Quaternion rotation = Quaternion.Euler(m_3DGridLeftLowerCornerRotation);

            // Set the color for the grid
            Gizmos.color = new Color(0, 1, 0, 0.2f);

            // Draw a sphere at the starting point
            Gizmos.DrawSphere(m_3DGridLeftLowerCornerPosition, m_3DGridCellWidth * 0.3f);
            
            // Draw the grid of cubes
            for (int x = 0; x < m_3DGridWidth; x++)
            {
                for (int y = 0; y < m_3DGridHeight; y++)
                {
                    for (int z = 0; z < m_3DGridDepth; z++)
                    {
                        Vector3 localPosition = new Vector3(
                            (x + 0.5f) * m_3DGridCellWidth,
                            (y + 0.5f) * m_3DGridCellWidth,
                            (z + 0.5f) * m_3DGridCellWidth
                        );

                        // Apply rotation to the local position
                        Vector3 rotatedPosition = rotation * localPosition;

                        // Calculate the world position of the cube center
                        Vector3 cubeCenter = m_3DGridLeftLowerCornerPosition + rotatedPosition;

                        var index = x + (y * m_3DGridWidth) + (z * m_3DGridWidth * m_3DGridHeight);
                        
                        if (m_ShowGridIndices)
                        {
                            // display index
                            GUIStyle indexStyle = new GUIStyle();
                            indexStyle.fontSize = 15;
                            indexStyle.normal.textColor = colorGradientsForIndex[index];
                            Handles.Label(cubeCenter, "" + index, indexStyle);
                            
                            // display splat count
                            GUIStyle splatcountStyle = new GUIStyle();
                            splatcountStyle.fontSize = 13;
                            splatcountStyle.normal.textColor = Color.white;
                            var leftLowerCorner =
                                cubeCenter - new Vector3(0, m_3DGridCellWidth/2, m_3DGridCellWidth/2);
                            Handles.Label(leftLowerCorner, "splatCnt: " + countSplatsInGridCell[index], splatcountStyle);
                            
                            // display splat view color
                            float offsetY = (index % 2 == 0) ? m_3DGridCellWidth / 3 : m_3DGridCellWidth / 6; 
                            var splatViewColorTextPos = leftLowerCorner + new Vector3(0, offsetY, 0);
                            Handles.Label(splatViewColorTextPos, "avgCol: " + averageColorInCell[index], splatcountStyle);
                            
                            // display splat SH data
                            var splatSHDataColTextPos = splatViewColorTextPos + new Vector3(m_3DGridCellWidth / 8, 0);
                            // Handles.Label();
                        }

                        Gizmos.color = new Color(0, 1, 0, 0.2f);
                        
                        // Draw the rotated wire cube
                        Gizmos.matrix = Matrix4x4.TRS(cubeCenter, rotation, Vector3.one);
                        Gizmos.DrawWireCube(Vector3.zero, Vector3.one * m_3DGridCellWidth);
                    }
                }
            }

            // Reset Gizmos matrix
            Gizmos.matrix = Matrix4x4.identity;
            
            
        }
        
        public SplatViewData[] GetSplatViewData()
        {
            if (m_GpuView == null)
                return null;

            SplatViewData[] viewData = new SplatViewData[m_SplatCount];
            m_GpuView.GetData(viewData);
            return viewData;
        }
        
        
        
        public static float HalfToFloat(ushort half)
        {
            int mant = half & 0x03FF;
            int exp = half & 0x7C00;
            if (exp == 0x7C00) exp = 0x3FC00;
            else if (exp != 0)
            {
                exp += 0x1C000;
                if (mant == 0 && exp > 0x1C400)
                    return BitConverter.Int32BitsToSingle((half & 0x8000) << 16 | exp << 13 | 0x3FF);
            }
            else if (mant != 0)
            {
                exp = 0x1C400;
                do
                {
                    mant <<= 1;
                    exp -= 0x400;
                } while ((mant & 0x400) == 0);
                mant &= 0x3FF;
            }
            return BitConverter.Int32BitsToSingle((half & 0x8000) << 16 | (exp | mant) << 13);
        }
        
        public static Vector4 UnpackColor(uint2 packedColor)
        {
            ushort r = (ushort)(packedColor.x >> 16);
            ushort g = (ushort)(packedColor.x & 0xFFFF);
            ushort b = (ushort)(packedColor.y >> 16);
            ushort a = (ushort)(packedColor.y & 0xFFFF);

            return new Vector4(
                HalfToFloat(r),
                HalfToFloat(g),
                HalfToFloat(b),
                HalfToFloat(a)
            );
        }
        
        // source for matrices: https://en.wikipedia.org/wiki/Rotation_matrix
        // This is the same function as we defined it in our ComputeShader. The only difference is that we need to use a 4x4 matrix, instead of a 3x3,
        // and that the declaration is done differently in here.
        private Vector3 ApplyRotationMatrix(Vector3 eulerAngle, Vector3 input)
        {
            // Convert Euler angles to radians
            Vector3 radAngles = eulerAngle * Mathf.PI / 180f;

            // Create rotation matrices
            Matrix4x4 rx = Matrix4x4.identity;
            rx.m00 = 1; rx.m01 = 0; rx.m02 = 0; rx.m03 = 0;
            rx.m10 = 0; rx.m11 = Mathf.Cos(radAngles.x); rx.m12 = Mathf.Sin(radAngles.x); rx.m13 = 0;
            rx.m20 = 0; rx.m21 = -Mathf.Sin(radAngles.x); rx.m22 = Mathf.Cos(radAngles.x); rx.m23 = 0;
            rx.m30 = 0; rx.m31 = 0; rx.m32 = 0; rx.m33 = 1;

            Matrix4x4 ry = Matrix4x4.identity;
            ry.m00 = Mathf.Cos(radAngles.y); ry.m01 = 0; ry.m02 = -Mathf.Sin(radAngles.y); ry.m03 = 0;
            ry.m10 = 0; ry.m11 = 1; ry.m12 = 0; ry.m13 = 0;
            ry.m20 = Mathf.Sin(radAngles.y); ry.m21 = 0; ry.m22 = Mathf.Cos(radAngles.y); ry.m23 = 0;
            ry.m30 = 0; ry.m31 = 0; ry.m32 = 0; ry.m33 = 1;

            Matrix4x4 rz = Matrix4x4.identity;
            rz.m00 = Mathf.Cos(radAngles.z); rz.m01 = Mathf.Sin(radAngles.z); rz.m02 = 0; rz.m03 = 0;
            rz.m10 = -Mathf.Sin(radAngles.z); rz.m11 = Mathf.Cos(radAngles.z); rz.m12 = 0; rz.m13 = 0;
            rz.m20 = 0; rz.m21 = 0; rz.m22 = 1; rz.m23 = 0;
            rz.m30 = 0; rz.m31 = 0; rz.m32 = 0; rz.m33 = 1;

            // Apply rotations in reverse order
            Vector4 inputVector = new Vector4(input.x, input.y, input.z, 1f);
            Vector4 rotatedPoint = rz * inputVector;
            rotatedPoint = ry * rotatedPoint;
            rotatedPoint = rx * rotatedPoint;

            // Convert result back to Vector3
            return new Vector3(rotatedPoint.x, rotatedPoint.y, rotatedPoint.z);
        }
        
        int GetGridCellIndex(Vector3 worldPos)
        {
            float3 localPos = worldPos - m_3DGridLeftLowerCornerPosition;
    
            // first we do not apply any rotation, to check whether at least the positions work
            float3 localPosRot = ApplyRotationMatrix(m_3DGridLeftLowerCornerRotation, localPos);
            
            float x = localPosRot.x;
            float y = localPosRot.y;
            float z = localPosRot.z;

            int cellCoordX = Mathf.FloorToInt(x / m_3DGridCellWidth);
            int cellCoordY = Mathf.FloorToInt(y / m_3DGridCellWidth);
            int cellCoordZ = Mathf.FloorToInt(z / m_3DGridCellWidth);

            // handle cases which are outside the grid, we return -1 to indicate its outside
            if(cellCoordX < 0 || cellCoordY < 0 || cellCoordZ < 0 ||
               x > m_3DGridWidth * m_3DGridCellWidth || y > m_3DGridHeight * m_3DGridCellWidth || z > m_3DGridDepth * m_3DGridCellWidth)
            {
                return -1;
            }

            return cellCoordX + cellCoordY * m_3DGridWidth + cellCoordZ * m_3DGridWidth * m_3DGridHeight;
        }
        
        public static Color[] GenerateColorGradient(int[] values, int minValue, int maxValue)
        {
            // Ensure min <= max
            if (minValue > maxValue)
            {
                (minValue, maxValue) = (maxValue, minValue);
            }

            // Calculate range
            float range = Mathf.Abs(maxValue - minValue);

            // Handle edge case where range is zero
            if (range == 0)
            {
                return Enumerable.Repeat(Color.gray, values.Length).ToArray();
            }

            // Initialize color array
            Color[] colors = new Color[values.Length];

            // Iterate over input values
            for (int i = 0; i < values.Length; i++)
            {
                // Calculate position within range
                float position = Mathf.InverseLerp(minValue, maxValue, values[i]);

                // Map position to color gradient
                Color color = Color.Lerp(Color.red, Color.green, position);

                colors[i] = color;
            }

            return colors;
        }
        
        public static Color[] GenerateColorGradient(int[] values)
        {
            return GenerateColorGradient(values, values.Min(), values.Max());
        }

        private Color[] colorGradientsForIndex;
        int[] countSplatsInGridCell;
        Vector4[] averageColorInCell;
        public void GetSplatDataInGrid()
        {
            // Get the positional data from the GraphicsBuffer
            float3[] posData = new float3[m_GpuPosData.count / 3];
            m_GpuPosData.GetData(posData);
            
            // Get the ViewData for the resulting color
            SplatViewData[] viewData = GetSplatViewData();

            // As the object where the GaussianSplatRenderer sits on may have some adjustments in (pos,rot,scale)
            // we apply a matrix operation to the points, so the transform component is taken into consideration
            var matrixObj2World = transform.localToWorldMatrix;
            Vector3[] centerWorldPositions = new Vector3[asset.posData.dataSize / 4 / 3];

            // SplatSHData[] splatSHData = new SplatSHData[m_GpuSHData.count / 16];
            // m_GpuSHData.GetData(splatSHData);
            
            // TODO: Make the centerWorldPos more accurate, it currently just shows two digits after comma.
            int countSplatsInGrid = 0;
            
            Vector4[] accumulatedColorsInGridCell = new Vector4[m_3DGridWidth * m_3DGridHeight * m_3DGridDepth];
            averageColorInCell = new Vector4[m_3DGridWidth * m_3DGridHeight * m_3DGridDepth];
            countSplatsInGridCell = new int[m_3DGridWidth * m_3DGridHeight * m_3DGridDepth];
            for (int i = 0; i < posData.Length; i++)
            {
                var splatPos4D = new Vector4(posData[i].x, posData[i].y, posData[i].z, 1);
                Vector4 centerWorldPos4D = matrixObj2World * splatPos4D;
                Vector3 centerWorldPos = new Vector3(centerWorldPos4D.x, centerWorldPos4D.y, centerWorldPos4D.z);
                centerWorldPositions[i] = centerWorldPos;
                var cellGridIndex = GetGridCellIndex(centerWorldPos);
                if (cellGridIndex != -1)
                {
                    /* // This if for debugging, whether the centerWorldPos is the actual splatPosition in the scene
                       // Do not uncomment, unless there are just a few splats, you might crash Unity otherwise!
                    var splatPos = Instantiate(m_debugPrefabSphere);
                    splatPos.transform.position = centerWorldPos;
                    */
                    accumulatedColorsInGridCell[cellGridIndex] += UnpackColor(viewData[i].color);
                    countSplatsInGridCell[cellGridIndex] += 1;
                    countSplatsInGrid++;
                }
            }

            colorGradientsForIndex = GenerateColorGradient(countSplatsInGridCell, 4000, 55000);

            for (int i = 0; i < countSplatsInGridCell.Length; i++)
            {
                averageColorInCell[i] = AverageColorInCell(accumulatedColorsInGridCell[i], countSplatsInGridCell[i]);
            }

        }
        
        #region AverageFunctions

        private Vector4 AverageColorInCell(Vector4 accumulatedColors, int gridCellElements)
        {
            return accumulatedColors / gridCellElements;
        }
        
        #endregion AverageFunctions
        
        #endregion GridPropertyFunctions
        
        internal void CalcViewData(CommandBuffer cmb, Camera cam, Matrix4x4 matrix)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            var tr = transform;
            
            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            Vector4 screenPar = new Vector4(screenW, screenH, 0, 0);
            
            // Depending on m_UseProxyCamObj, we use a different camera position for the shading of the Gaussians,
            // so that we can have a fix camera and nevertheless change the appearance of the gaussians based on the proxyGameObject
            // also some error-handling here:
            Vector4 camPos = Vector4.zero;
            if (m_UseProxyCamObj && m_ProxyCameraObject == null)
            {
                Debug.LogError("If you set UseProxyCamObj to true, please also assign a proxy camera object.");
                camPos = cam.transform.position;
            } else if (m_UseProxyCamObj)
            {
                camPos = m_ProxyCameraObject.transform.position;
            }
            else
            {
                camPos = cam.transform.position;
            }

            // calculate view dependent data for each splat
            SetAssetDataOnCS(cmb, KernelIndices.CalcViewData);
            
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.DrawSplats, m_DrawSplats ? 1 : 0);
            
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixVP, matProj * matView);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, matView * matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixP, matProj);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecScreenParams, screenPar);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecWorldSpaceCameraPos, camPos);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.SplatScale, m_SplatScale);
            
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.SplatOpacityScale, m_OpacityScale);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SHOrder, m_SHOrder);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SHOnly, m_SHOnly ? 1 : 0);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.IgnoreGrid, m_IgnoreGrid ? 1 :0);
            
            // additional dimmers for each band
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.DimmerSH0, m_DimmerSH0);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.DimmerSH1, m_DimmerSH1);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.DimmerSH2, m_DimmerSH2);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.DimmerSH3, m_DimmerSH3);

            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.Hue, m_Hue);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.Saturation, m_Saturation);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.Brightness, m_Brightness);
            
            // interpolation slider
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.InterpolationValue, m_InterpolationValue);
            
            // 3D grid structure
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.GridLeftLowerCornerPosition,
                m_3DGridLeftLowerCornerPosition);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.GridLeftLowerCornerRotation, m_3DGridLeftLowerCornerRotation);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.GridWidth, m_3DGridWidth);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.GridHeight, m_3DGridHeight);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.GridDepth, m_3DGridDepth);
            cmb.SetComputeFloatParam(m_CSSplatUtilities, Props.GridCellWidth, m_3DGridCellWidth);
            
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.CalcViewData, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.CalcViewData, (m_GpuView.count + (int)gsX - 1)/(int)gsX, 1, 1);
        }

        internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix)
        {
            if (cam.cameraType == CameraType.Preview)
                return;

            Matrix4x4 worldToCamMatrix = cam.worldToCameraMatrix;
            worldToCamMatrix.m20 *= -1;
            worldToCamMatrix.m21 *= -1;
            worldToCamMatrix.m22 *= -1;
            
            // calculate distance to the camera for each splat
            cmd.BeginSample(s_ProfSort);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortDistances, m_GpuSortDistances);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatSortKeys, m_GpuSortKeys);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatChunks, m_GpuChunks);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatPos, m_GpuPosData);
            cmd.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, Props.SplatPos, m_GpuPosData2);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatFormat, (int)m_Asset.posFormat);
            cmd.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, worldToCamMatrix * matrix);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatCount, m_SplatCount);
            cmd.SetComputeIntParam(m_CSSplatUtilities, Props.SplatChunkCount, m_GpuChunksValid ? m_GpuChunks.count : 0);
            //Debug.Log($"(int)KernelIndices.CalcDistances = {(int)KernelIndices.CalcDistances}");
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.CalcDistances, out uint gsX, out _, out _);
            cmd.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.CalcDistances, (m_GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

            // sort the splats
            m_Sorter.Dispatch(cmd, m_SorterArgs);
            cmd.EndSample(s_ProfSort);
        }

        public void Update()
        {
            var curHash = m_Asset ? m_Asset.dataHash : new Hash128();
            if (m_PrevAsset != m_Asset || m_PrevHash != curHash)
            {
                m_PrevAsset = m_Asset;
                m_PrevHash = curHash;
                DisposeResourcesForAsset();
                CreateResourcesForAsset();
            }
        }

        public void ActivateCamera(int index)
        {
            Camera mainCam = m_Camera;
            if (!mainCam)
                return;
            if (!m_Asset || m_Asset.cameras == null)
                return;

            var selfTr = transform;
            var camTr = mainCam.transform;
            var prevParent = camTr.parent;
            var cam = m_Asset.cameras[index];
            camTr.parent = selfTr;
            camTr.localPosition = cam.pos;
            camTr.localRotation = Quaternion.LookRotation(cam.axisZ, cam.axisY);
            camTr.parent = prevParent;
            camTr.localScale = Vector3.one;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(camTr);
#endif
        }

        void ClearGraphicsBuffer(GraphicsBuffer buf)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.ClearBuffer, Props.DstBuffer, buf);
            m_CSSplatUtilities.SetInt(Props.BufferSize, buf.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.ClearBuffer, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.ClearBuffer, (int)((buf.count+gsX-1)/gsX), 1, 1);
        }

        void UnionGraphicsBuffers(GraphicsBuffer dst, GraphicsBuffer src)
        {
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.SrcBuffer, src);
            m_CSSplatUtilities.SetBuffer((int)KernelIndices.OrBuffers, Props.DstBuffer, dst);
            m_CSSplatUtilities.SetInt(Props.BufferSize, dst.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.OrBuffers, out uint gsX, out _, out _);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.OrBuffers, (int)((dst.count+gsX-1)/gsX), 1, 1);
        }

        static float SortableUintToFloat(uint v)
        {
            uint mask = ((v >> 31) - 1) | 0x80000000u;
            return math.asfloat(v ^ mask);
        }

        public void UpdateEditCountsAndBounds()
        {
            if (m_GpuEditSelected == null)
            {
                editSelectedSplats = 0;
                editDeletedSplats = 0;
                editCutSplats = 0;
                editModified = false;
                editSelectedBounds = default;
                return;
            }

            m_CSSplatUtilities.SetBuffer((int)KernelIndices.InitEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            m_CSSplatUtilities.Dispatch((int)KernelIndices.InitEditData, 1, 1, 1);

            using CommandBuffer cmb = new CommandBuffer();
            SetAssetDataOnCS(cmb, KernelIndices.UpdateEditData);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, Props.DstBuffer, m_GpuEditCountsBounds);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)KernelIndices.UpdateEditData, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)KernelIndices.UpdateEditData, (int)((m_GpuEditSelected.count+gsX-1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);

            uint[] res = new uint[m_GpuEditCountsBounds.count];
            m_GpuEditCountsBounds.GetData(res);
            editSelectedSplats = res[0];
            editDeletedSplats = res[1];
            editCutSplats = res[2];
            Vector3 min = new Vector3(SortableUintToFloat(res[3]), SortableUintToFloat(res[4]), SortableUintToFloat(res[5]));
            Vector3 max = new Vector3(SortableUintToFloat(res[6]), SortableUintToFloat(res[7]), SortableUintToFloat(res[8]));
            Bounds bounds = default;
            bounds.SetMinMax(min, max);
            if (bounds.extents.sqrMagnitude < 0.01)
                bounds.extents = new Vector3(0.1f,0.1f,0.1f);
            editSelectedBounds = bounds;
        }

        void UpdateCutoutsBuffer()
        {
            int bufferSize = m_Cutouts?.Length ?? 0;
            if (bufferSize == 0)
                bufferSize = 1;
            if (m_GpuEditCutouts == null || m_GpuEditCutouts.count != bufferSize)
            {
                m_GpuEditCutouts?.Dispose();
                m_GpuEditCutouts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, bufferSize, UnsafeUtility.SizeOf<GaussianCutout.ShaderData>()) { name = "GaussianCutouts" };
            }

            NativeArray<GaussianCutout.ShaderData> data = new(bufferSize, Allocator.Temp);
            if (m_Cutouts != null)
            {
                var matrix = transform.localToWorldMatrix;
                for (var i = 0; i < m_Cutouts.Length; ++i)
                {
                    data[i] = GaussianCutout.GetShaderData(m_Cutouts[i], matrix);
                }
            }

            m_GpuEditCutouts.SetData(data);
            data.Dispose();
        }

        bool EnsureEditingBuffers()
        {
            if (!HasValidAsset || !HasValidRenderSetup)
                return false;

            if (m_GpuEditSelected == null)
            {
                var target = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource |
                             GraphicsBuffer.Target.CopyDestination;
                var size = (m_SplatCount + 31) / 32;
                m_GpuEditSelected = new GraphicsBuffer(target, size, 4) {name = "GaussianSplatSelected"};
                m_GpuEditSelectedMouseDown = new GraphicsBuffer(target, size, 4) {name = "GaussianSplatSelectedInit"};
                m_GpuEditDeleted = new GraphicsBuffer(target, size, 4) {name = "GaussianSplatDeleted"};
                m_GpuEditCountsBounds = new GraphicsBuffer(target, 3 + 6, 4) {name = "GaussianSplatEditData"}; // selected count, deleted bound, cut count, float3 min, float3 max
                ClearGraphicsBuffer(m_GpuEditSelected);
                ClearGraphicsBuffer(m_GpuEditSelectedMouseDown);
                ClearGraphicsBuffer(m_GpuEditDeleted);
            }
            return m_GpuEditSelected != null;
        }

        public void EditStoreSelectionMouseDown()
        {
            if (!EnsureEditingBuffers()) return;
            Graphics.CopyBuffer(m_GpuEditSelected, m_GpuEditSelectedMouseDown);
        }

        public void EditStorePosMouseDown()
        {
            if (m_GpuEditPosMouseDown == null)
            {
                m_GpuEditPosMouseDown = new GraphicsBuffer(m_GpuPosData.target | GraphicsBuffer.Target.CopyDestination, m_GpuPosData.count, m_GpuPosData.stride) {name = "GaussianSplatEditPosMouseDown"};
            }
            Graphics.CopyBuffer(m_GpuPosData, m_GpuEditPosMouseDown);
        }
        public void EditStoreOtherMouseDown()
        {
            if (m_GpuEditOtherMouseDown == null)
            {
                m_GpuEditOtherMouseDown = new GraphicsBuffer(m_GpuOtherData.target | GraphicsBuffer.Target.CopyDestination, m_GpuOtherData.count, m_GpuOtherData.stride) {name = "GaussianSplatEditOtherMouseDown"};
            }
            Graphics.CopyBuffer(m_GpuOtherData, m_GpuEditOtherMouseDown);
        }

        public void EditUpdateSelection(Vector2 rectMin, Vector2 rectMax, Camera cam, bool subtract)
        {
            if (!EnsureEditingBuffers()) return;

            Graphics.CopyBuffer(m_GpuEditSelectedMouseDown, m_GpuEditSelected);

            var tr = transform;
            Matrix4x4 matView = cam.worldToCameraMatrix;
            Matrix4x4 matProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
            Matrix4x4 matO2W = tr.localToWorldMatrix;
            Matrix4x4 matW2O = tr.worldToLocalMatrix;
            int screenW = cam.pixelWidth, screenH = cam.pixelHeight;
            Vector4 screenPar = new Vector4(screenW, screenH, 0, 0);
            Vector4 camPos = cam.transform.position;

            using var cmb = new CommandBuffer { name = "SplatSelectionUpdate" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectionUpdate);

            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixVP, matProj * matView);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixMV, matView * matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixP, matProj);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, matO2W);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, matW2O);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecScreenParams, screenPar);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.VecWorldSpaceCameraPos, camPos);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_SelectionRect", new Vector4(rectMin.x, rectMax.y, rectMax.x, rectMin.y));
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.SelectionMode, subtract ? 0 : 1);

            DispatchUtilsAndExecute(cmb, KernelIndices.SelectionUpdate, m_SplatCount);
            UpdateEditCountsAndBounds();
        }

        public void EditTranslateSelection(Vector3 localSpacePosDelta)
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatTranslateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.TranslateSelection);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, localSpacePosDelta);

            DispatchUtilsAndExecute(cmb, KernelIndices.TranslateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        public void EditRotateSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Quaternion rotation)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null || m_GpuEditOtherMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatRotateSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.RotateSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.RotateSelection, Props.SplatOtherMouseDown, m_GpuEditOtherMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDeltaRot, new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));

            DispatchUtilsAndExecute(cmb, KernelIndices.RotateSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }


        public void EditScaleSelection(Vector3 localSpaceCenter, Matrix4x4 localToWorld, Matrix4x4 worldToLocal, Vector3 scale)
        {
            if (!EnsureEditingBuffers()) return;
            if (m_GpuEditPosMouseDown == null) return; // should have captured initial state

            using var cmb = new CommandBuffer { name = "SplatScaleSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.ScaleSelection);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ScaleSelection, Props.SplatPosMouseDown, m_GpuEditPosMouseDown);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionCenter, localSpaceCenter);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, localToWorld);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixWorldToObject, worldToLocal);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, Props.SelectionDelta, scale);

            DispatchUtilsAndExecute(cmb, KernelIndices.ScaleSelection, m_SplatCount);
            UpdateEditCountsAndBounds();
            editModified = true;
        }

        public void EditDeleteSelected()
        {
            if (!EnsureEditingBuffers()) return;
            UnionGraphicsBuffers(m_GpuEditDeleted, m_GpuEditSelected);
            EditDeselectAll();
            UpdateEditCountsAndBounds();
            if (editDeletedSplats != 0)
                editModified = true;
        }

        public void EditSelectAll()
        {
            if (!EnsureEditingBuffers()) return;
            using var cmb = new CommandBuffer { name = "SplatSelectAll" };
            SetAssetDataOnCS(cmb, KernelIndices.SelectAll);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.SelectAll, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.SelectAll, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public void EditDeselectAll()
        {
            if (!EnsureEditingBuffers()) return;
            ClearGraphicsBuffer(m_GpuEditSelected);
            UpdateEditCountsAndBounds();
        }

        public void EditInvertSelection()
        {
            if (!EnsureEditingBuffers()) return;

            using var cmb = new CommandBuffer { name = "SplatInvertSelection" };
            SetAssetDataOnCS(cmb, KernelIndices.InvertSelection);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.InvertSelection, Props.DstBuffer, m_GpuEditSelected);
            cmb.SetComputeIntParam(m_CSSplatUtilities, Props.BufferSize, m_GpuEditSelected.count);
            DispatchUtilsAndExecute(cmb, KernelIndices.InvertSelection, m_GpuEditSelected.count);
            UpdateEditCountsAndBounds();
        }

        public bool EditExportData(GraphicsBuffer dstData, bool bakeTransform)
        {
            if (!EnsureEditingBuffers()) return false;

            int flags = 0;
            var tr = transform;
            Quaternion bakeRot = tr.localRotation;
            Vector3 bakeScale = tr.localScale;

            if (bakeTransform)
                flags = 1;

            using var cmb = new CommandBuffer { name = "SplatExportData" };
            SetAssetDataOnCS(cmb, KernelIndices.ExportData);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_ExportTransformFlags", flags);
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformRotation", new Vector4(bakeRot.x, bakeRot.y, bakeRot.z, bakeRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_ExportTransformScale", bakeScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, Props.MatrixObjectToWorld, tr.localToWorldMatrix);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.ExportData, "_ExportBuffer", dstData);

            DispatchUtilsAndExecute(cmb, KernelIndices.ExportData, m_SplatCount);
            return true;
        }

        public void EditSetSplatCount(int newSplatCount)
        {
            if (newSplatCount <= 0 || newSplatCount > GaussianSplatAsset.kMaxSplats)
            {
                Debug.LogError($"Invalid new splat count: {newSplatCount}");
                return;
            }
            if (asset.chunkData != null)
            {
                Debug.LogError("Only splats with VeryHigh quality can be resized");
                return;
            }
            if (newSplatCount == splatCount)
                return;

            int posStride = (int)(asset.posData.dataSize / asset.splatCount);
            int otherStride = (int)(asset.otherData.dataSize / asset.splatCount);
            int shStride = (int) (asset.shData.dataSize / asset.splatCount);

            // create new GPU buffers
            var newPosData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * posStride / 4, 4) { name = "GaussianPosData" };
            var newOtherData = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, newSplatCount * otherStride / 4, 4) { name = "GaussianOtherData" };
            var newSHData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, newSplatCount * shStride / 4, 4) { name = "GaussianSHData" };

            // new texture is a RenderTexture so we can write to it from a compute shader
            var (texWidth, texHeight) = GaussianSplatAsset.CalcTextureSize(newSplatCount);
            var texFormat = GaussianSplatAsset.ColorFormatToGraphics(asset.colorFormat);
            var newColorData = new RenderTexture(texWidth, texHeight, texFormat, GraphicsFormat.None) { name = "GaussianColorData", enableRandomWrite = true };
            newColorData.Create();

            // selected/deleted buffers
            var selTarget = GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.CopyDestination;
            var selSize = (newSplatCount + 31) / 32;
            var newEditSelected = new GraphicsBuffer(selTarget, selSize, 4) {name = "GaussianSplatSelected"};
            var newEditSelectedMouseDown = new GraphicsBuffer(selTarget, selSize, 4) {name = "GaussianSplatSelectedInit"};
            var newEditDeleted = new GraphicsBuffer(selTarget, selSize, 4) {name = "GaussianSplatDeleted"};
            ClearGraphicsBuffer(newEditSelected);
            ClearGraphicsBuffer(newEditSelectedMouseDown);
            ClearGraphicsBuffer(newEditDeleted);

            var newGpuView = new GraphicsBuffer(GraphicsBuffer.Target.Structured, newSplatCount, kGpuViewDataSize);
            InitSortBuffers(newSplatCount);

            // copy existing data over into new buffers
            EditCopySplats(transform, newPosData, newOtherData, newSHData, newColorData, newEditDeleted, newSplatCount, 0, 0, m_SplatCount);

            // use the new buffers and the new splat count
            m_GpuPosData.Dispose();
            m_GpuPosData2.Dispose();
            
            m_GpuOtherData.Dispose();
            m_GpuOtherData2.Dispose();
            
            m_GpuSHData.Dispose();
            m_GpuSHData2.Dispose();
            
            DestroyImmediate(m_GpuColorData);
            m_GpuView.Dispose();

            m_GpuEditSelected?.Dispose();
            m_GpuEditSelectedMouseDown?.Dispose();
            m_GpuEditDeleted?.Dispose();

            m_GpuPosData = newPosData;
            m_GpuOtherData = newOtherData;
            m_GpuSHData = newSHData;
            m_GpuColorData = newColorData;
            m_GpuView = newGpuView;
            m_GpuEditSelected = newEditSelected;
            m_GpuEditSelectedMouseDown = newEditSelectedMouseDown;
            m_GpuEditDeleted = newEditDeleted;

            DisposeBuffer(ref m_GpuEditPosMouseDown);
            DisposeBuffer(ref m_GpuEditOtherMouseDown);

            m_SplatCount = newSplatCount;
            editModified = true;
        }

        public void EditCopySplatsInto(GaussianSplatRenderer dst, int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            EditCopySplats(
                dst.transform,
                dst.m_GpuPosData, dst.m_GpuOtherData, dst.m_GpuSHData, dst.m_GpuColorData, dst.m_GpuEditDeleted,
                dst.splatCount,
                copySrcStartIndex, copyDstStartIndex, copyCount);
            dst.editModified = true;
        }

        public void EditCopySplats(
            Transform dstTransform,
            GraphicsBuffer dstPos, GraphicsBuffer dstOther, GraphicsBuffer dstSH, Texture dstColor,
            GraphicsBuffer dstEditDeleted,
            int dstSize,
            int copySrcStartIndex, int copyDstStartIndex, int copyCount)
        {
            if (!EnsureEditingBuffers()) return;

            Matrix4x4 copyMatrix = dstTransform.worldToLocalMatrix * transform.localToWorldMatrix;
            Quaternion copyRot = copyMatrix.rotation;
            Vector3 copyScale = copyMatrix.lossyScale;

            using var cmb = new CommandBuffer { name = "SplatCopy" };
            SetAssetDataOnCS(cmb, KernelIndices.CopySplats);

            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstPos", dstPos);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstOther", dstOther);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstSH", dstSH);
            cmb.SetComputeTextureParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstColor", dstColor);
            cmb.SetComputeBufferParam(m_CSSplatUtilities, (int)KernelIndices.CopySplats, "_CopyDstEditDeleted", dstEditDeleted);

            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstSize", dstSize);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopySrcStartIndex", copySrcStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyDstStartIndex", copyDstStartIndex);
            cmb.SetComputeIntParam(m_CSSplatUtilities, "_CopyCount", copyCount);

            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformRotation", new Vector4(copyRot.x, copyRot.y, copyRot.z, copyRot.w));
            cmb.SetComputeVectorParam(m_CSSplatUtilities, "_CopyTransformScale", copyScale);
            cmb.SetComputeMatrixParam(m_CSSplatUtilities, "_CopyTransformMatrix", copyMatrix);

            DispatchUtilsAndExecute(cmb, KernelIndices.CopySplats, copyCount);
        }

        void DispatchUtilsAndExecute(CommandBuffer cmb, KernelIndices kernel, int count)
        {
            m_CSSplatUtilities.GetKernelThreadGroupSizes((int)kernel, out uint gsX, out _, out _);
            cmb.DispatchCompute(m_CSSplatUtilities, (int)kernel, (int)((count + gsX - 1)/gsX), 1, 1);
            Graphics.ExecuteCommandBuffer(cmb);
        }

        public GraphicsBuffer GpuEditDeleted => m_GpuEditDeleted;
        
        
        
    }
}