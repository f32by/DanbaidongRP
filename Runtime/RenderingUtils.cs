using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Contains properties and helper functions that you can use when rendering.
    /// </summary>
    public static class RenderingUtils
    {
        static List<ShaderTagId> m_LegacyShaderPassNames = new List<ShaderTagId>
        {
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
            new ShaderTagId("PrepassBase"),
            new ShaderTagId("Vertex"),
            new ShaderTagId("VertexLMRGBM"),
            new ShaderTagId("VertexLM"),
        };

        static AttachmentDescriptor s_EmptyAttachment = new AttachmentDescriptor(GraphicsFormat.None);
        internal static AttachmentDescriptor emptyAttachment
        {
            get
            {
                return s_EmptyAttachment;
            }
        }

        static Mesh s_FullscreenMesh = null;

        /// <summary>
        /// Returns a mesh that you can use with <see cref="CommandBuffer.DrawMesh(Mesh, Matrix4x4, Material)"/> to render full-screen effects.
        /// </summary>
        [Obsolete("Use Blitter.BlitCameraTexture instead of CommandBuffer.DrawMesh(fullscreenMesh, ...)")]
        public static Mesh fullscreenMesh
        {
            get
            {
                if (s_FullscreenMesh != null)
                    return s_FullscreenMesh;

                float topV = 1.0f;
                float bottomV = 0.0f;

                s_FullscreenMesh = new Mesh { name = "Fullscreen Quad" };
                s_FullscreenMesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f,  1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f,  1.0f, 0.0f)
                });

                s_FullscreenMesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0.0f, bottomV),
                    new Vector2(0.0f, topV),
                    new Vector2(1.0f, bottomV),
                    new Vector2(1.0f, topV)
                });

                s_FullscreenMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                s_FullscreenMesh.UploadMeshData(true);
                return s_FullscreenMesh;
            }
        }

        internal static bool useStructuredBuffer
        {
            // There are some performance issues with StructuredBuffers in some platforms.
            // We fallback to UBO in those cases.
            get
            {
                // TODO: For now disabling SSBO until figure out Vulkan binding issues.
                // When enabling this also enable USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA in shader side in Input.hlsl
                return false;

                // We don't use SSBO in D3D because we can't figure out without adding shader variants if platforms is D3D10.
                //GraphicsDeviceType deviceType = SystemInfo.graphicsDeviceType;
                //return !Application.isMobilePlatform &&
                //    (deviceType == GraphicsDeviceType.Metal || deviceType == GraphicsDeviceType.Vulkan ||
                //     deviceType == GraphicsDeviceType.PlayStation4 || deviceType == GraphicsDeviceType.PlayStation5 || deviceType == GraphicsDeviceType.XboxOne);
            }
        }

        internal static bool SupportsLightLayers(GraphicsDeviceType type)
        {
            // GLES2 does not support bitwise operations.
            return type != GraphicsDeviceType.OpenGLES2;
        }

        static Material s_ErrorMaterial;
        static Material errorMaterial
        {
            get
            {
                if (s_ErrorMaterial == null)
                {
                    // TODO: When importing project, AssetPreviewUpdater::CreatePreviewForAsset will be called multiple times.
                    // This might be in a point that some resources required for the pipeline are not finished importing yet.
                    // Proper fix is to add a fence on asset import.
                    try
                    {
                        s_ErrorMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/FallbackError"));
                    }
                    catch { }
                }

                return s_ErrorMaterial;
            }
        }

        /// <summary>
        /// Set view and projection matrices.
        /// This function will set <c>UNITY_MATRIX_V</c>, <c>UNITY_MATRIX_P</c>, <c>UNITY_MATRIX_VP</c> to given view and projection matrices.
        /// If <c>setInverseMatrices</c> is set to true this function will also set <c>UNITY_MATRIX_I_V</c> and <c>UNITY_MATRIX_I_VP</c>.
        /// </summary>
        /// <param name="cmd">CommandBuffer to submit data to GPU.</param>
        /// <param name="viewMatrix">View matrix to be set.</param>
        /// <param name="projectionMatrix">Projection matrix to be set.</param>
        /// <param name="setInverseMatrices">Set this to true if you also need to set inverse camera matrices.</param>
        public static void SetViewAndProjectionMatrices(CommandBuffer cmd, Matrix4x4 viewMatrix, Matrix4x4 projectionMatrix, bool setInverseMatrices)
        {
            Matrix4x4 viewAndProjectionMatrix = projectionMatrix * viewMatrix;
            cmd.SetGlobalMatrix(ShaderPropertyId.viewMatrix, viewMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.projectionMatrix, projectionMatrix);
            cmd.SetGlobalMatrix(ShaderPropertyId.viewAndProjectionMatrix, viewAndProjectionMatrix);

            if (setInverseMatrices)
            {
                Matrix4x4 inverseViewMatrix = Matrix4x4.Inverse(viewMatrix);
                Matrix4x4 inverseProjectionMatrix = Matrix4x4.Inverse(projectionMatrix);
                Matrix4x4 inverseViewProjection = inverseViewMatrix * inverseProjectionMatrix;
                cmd.SetGlobalMatrix(ShaderPropertyId.inverseViewMatrix, inverseViewMatrix);
                cmd.SetGlobalMatrix(ShaderPropertyId.inverseProjectionMatrix, inverseProjectionMatrix);
                cmd.SetGlobalMatrix(ShaderPropertyId.inverseViewAndProjectionMatrix, inverseViewProjection);
            }
        }

        internal static void SetScaleBiasRt(CommandBuffer cmd, in RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            // SetRenderTarget has logic to flip projection matrix when rendering to render texture. Flip the uv to account for that case.
            CameraData cameraData = renderingData.cameraData;
            bool isCameraColorFinalTarget = (cameraData.cameraType == CameraType.Game && renderer.cameraColorTargetHandle.nameID == BuiltinRenderTextureType.CameraTarget && cameraData.camera.targetTexture == null);
            bool yflip = !isCameraColorFinalTarget;
            float flipSign = yflip ? -1.0f : 1.0f;
            Vector4 scaleBiasRt = (flipSign < 0.0f)
                ? new Vector4(flipSign, 1.0f, -1.0f, 1.0f)
                : new Vector4(flipSign, 0.0f, 1.0f, 1.0f);
            cmd.SetGlobalVector(Shader.PropertyToID("_ScaleBiasRt"), scaleBiasRt);
        }

        internal static void Blit(CommandBuffer cmd,
            RTHandle source,
            Rect viewport,
            RTHandle destination,
            RenderBufferLoadAction loadAction,
            RenderBufferStoreAction storeAction,
            ClearFlag clearFlag,
            Color clearColor,
            Material material,
            int passIndex = 0)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            CoreUtils.SetRenderTarget(cmd, destination, loadAction, storeAction, ClearFlag.None, Color.clear);
            cmd.SetViewport(viewport);
            Blitter.BlitTexture(cmd, source, viewportScale, material, passIndex);
        }

        internal static void Blit(CommandBuffer cmd,
            RTHandle source,
            Rect viewport,
            RTHandle destinationColor,
            RenderBufferLoadAction colorLoadAction,
            RenderBufferStoreAction colorStoreAction,
            RTHandle destinationDepthStencil,
            RenderBufferLoadAction depthStencilLoadAction,
            RenderBufferStoreAction depthStencilStoreAction,
            ClearFlag clearFlag,
            Color clearColor,
            Material material,
            int passIndex = 0)
        {
            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;
            CoreUtils.SetRenderTarget(cmd,
                destinationColor, colorLoadAction, colorStoreAction,
                destinationDepthStencil, depthStencilLoadAction, depthStencilStoreAction,
                clearFlag, clearColor); // implicit depth=1.0f stencil=0x0
            cmd.SetViewport(viewport);
            Blitter.BlitTexture(cmd, source, viewportScale, material, passIndex);
        }

        internal static void FinalBlit(
            CommandBuffer cmd,
            ref CameraData cameraData,
            RTHandle source,
            RTHandle destination,
            RenderBufferLoadAction loadAction,
            RenderBufferStoreAction storeAction,
            Material material, int passIndex)
        {
            bool isRenderToBackBufferTarget = !cameraData.isSceneViewCamera;
#if ENABLE_VR && ENABLE_XR_MODULE
                if (cameraData.xr.enabled)
                    isRenderToBackBufferTarget = new RenderTargetIdentifier(destination.nameID, 0, CubemapFace.Unknown, -1) == new RenderTargetIdentifier(cameraData.xr.renderTarget, 0, CubemapFace.Unknown, -1);
#endif

            Vector2 viewportScale = source.useScaling ? new Vector2(source.rtHandleProperties.rtHandleScale.x, source.rtHandleProperties.rtHandleScale.y) : Vector2.one;

            // We y-flip if
            // 1) we are blitting from render texture to back buffer(UV starts at bottom) and
            // 2) renderTexture starts UV at top
            bool yflip = isRenderToBackBufferTarget && cameraData.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
            Vector4 scaleBias = yflip ? new Vector4(viewportScale.x, -viewportScale.y, 0, viewportScale.y) : new Vector4(viewportScale.x, viewportScale.y, 0, 0);
            CoreUtils.SetRenderTarget(cmd, destination, loadAction, storeAction, ClearFlag.None, Color.clear);
            if (isRenderToBackBufferTarget)
                cmd.SetViewport(cameraData.pixelRect);

            // cmd.Blit must be used in Scene View for wireframe mode to make the full screen draw with fill mode
            // This branch of the if statement must be removed for render graph and the new command list with a novel way of using Blitter with fill mode
            if (GL.wireframe && cameraData.isSceneViewCamera)
            {
                // This set render target is necessary so we change the LOAD state to DontCare.
                cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget,
                    loadAction, storeAction, // color
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare); // depth
                cmd.Blit(source.nameID, destination.nameID);
            }
            else if (source.rt == null)
                Blitter.BlitTexture(cmd, source.nameID, scaleBias, material, passIndex);  // Obsolete usage of RTHandle aliasing a RenderTargetIdentifier
            else
                Blitter.BlitTexture(cmd, source, scaleBias, material, passIndex);
        }

        // This is used to render materials that contain built-in shader passes not compatible with URP.
        // It will render those legacy passes with error/pink shader.
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        internal static void RenderObjectsWithError(ScriptableRenderContext context, ref CullingResults cullResults, Camera camera, FilteringSettings filterSettings, SortingCriteria sortFlags)
        {
            // TODO: When importing project, AssetPreviewUpdater::CreatePreviewForAsset will be called multiple times.
            // This might be in a point that some resources required for the pipeline are not finished importing yet.
            // Proper fix is to add a fence on asset import.
            if (errorMaterial == null)
                return;

            SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortFlags };
            DrawingSettings errorSettings = new DrawingSettings(m_LegacyShaderPassNames[0], sortingSettings)
            {
                perObjectData = PerObjectData.None,
                overrideMaterial = errorMaterial,
                overrideMaterialPassIndex = 0
            };
            for (int i = 1; i < m_LegacyShaderPassNames.Count; ++i)
                errorSettings.SetShaderPassName(i, m_LegacyShaderPassNames[i]);

            context.DrawRenderers(cullResults, ref errorSettings, ref filterSettings);
        }

        // Caches render texture format support. SystemInfo.SupportsRenderTextureFormat and IsFormatSupported allocate memory due to boxing.
        static Dictionary<RenderTextureFormat, bool> m_RenderTextureFormatSupport = new Dictionary<RenderTextureFormat, bool>();
        static Dictionary<GraphicsFormat, Dictionary<FormatUsage, bool>> m_GraphicsFormatSupport = new Dictionary<GraphicsFormat, Dictionary<FormatUsage, bool>>();

        internal static void ClearSystemInfoCache()
        {
            m_RenderTextureFormatSupport.Clear();
            m_GraphicsFormatSupport.Clear();
        }

        /// <summary>
        /// Checks if a render texture format is supported by the run-time system.
        /// Similar to <see cref="SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat)"/>, but doesn't allocate memory.
        /// </summary>
        /// <param name="format">The format to look up.</param>
        /// <returns>Returns true if the graphics card supports the given <c>RenderTextureFormat</c></returns>
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format)
        {
            if (!m_RenderTextureFormatSupport.TryGetValue(format, out var support))
            {
                support = SystemInfo.SupportsRenderTextureFormat(format);
                m_RenderTextureFormatSupport.Add(format, support);
            }

            return support;
        }

        /// <summary>
        /// Checks if a texture format is supported by the run-time system.
        /// Similar to <see cref="SystemInfo.IsFormatSupported"/>, but doesn't allocate memory.
        /// </summary>
        /// <param name="format">The format to look up.</param>
        /// <param name="usage">The format usage to look up.</param>
        /// <returns>Returns true if the graphics card supports the given <c>GraphicsFormat</c></returns>
        public static bool SupportsGraphicsFormat(GraphicsFormat format, FormatUsage usage)
        {
            bool support = false;
            if (!m_GraphicsFormatSupport.TryGetValue(format, out var uses))
            {
                uses = new Dictionary<FormatUsage, bool>();
                support = SystemInfo.IsFormatSupported(format, usage);
                uses.Add(usage, support);
                m_GraphicsFormatSupport.Add(format, uses);
            }
            else
            {
                if (!uses.TryGetValue(usage, out support))
                {
                    support = SystemInfo.IsFormatSupported(format, usage);
                    uses.Add(usage, support);
                }
            }

            return support;
        }

        /// <summary>
        /// Return the last colorBuffer index actually referring to an existing RenderTarget
        /// </summary>
        /// <param name="colorBuffers"></param>
        /// <returns></returns>
        internal static int GetLastValidColorBufferIndex(RenderTargetIdentifier[] colorBuffers)
        {
            int i = colorBuffers.Length - 1;
            for (; i >= 0; --i)
            {
                if (colorBuffers[i] != 0)
                    break;
            }
            return i;
        }

        /// <summary>
        /// Return the number of items in colorBuffers actually referring to an existing RenderTarget
        /// </summary>
        /// <param name="colorBuffers"></param>
        /// <returns></returns>
        [Obsolete("Use RTHandles for colorBuffers")]
        internal static uint GetValidColorBufferCount(RenderTargetIdentifier[] colorBuffers)
        {
            uint nonNullColorBuffers = 0;
            if (colorBuffers != null)
            {
                foreach (var identifier in colorBuffers)
                {
                    if (identifier != 0)
                        ++nonNullColorBuffers;
                }
            }
            return nonNullColorBuffers;
        }

        /// <summary>
        /// Return the number of items in colorBuffers actually referring to an existing RenderTarget
        /// </summary>
        /// <param name="colorBuffers"></param>
        /// <returns></returns>
        internal static uint GetValidColorBufferCount(RTHandle[] colorBuffers)
        {
            uint nonNullColorBuffers = 0;
            if (colorBuffers != null)
            {
                foreach (var identifier in colorBuffers)
                {
                    if (identifier != null && identifier.nameID != 0)
                        ++nonNullColorBuffers;
                }
            }
            return nonNullColorBuffers;
        }

        /// <summary>
        /// Return true if colorBuffers is an actual MRT setup
        /// </summary>
        /// <param name="colorBuffers"></param>
        /// <returns></returns>
        [Obsolete("Use RTHandles for colorBuffers")]
        internal static bool IsMRT(RenderTargetIdentifier[] colorBuffers)
        {
            return GetValidColorBufferCount(colorBuffers) > 1;
        }

        /// <summary>
        /// Return true if colorBuffers is an actual MRT setup
        /// </summary>
        /// <param name="colorBuffers"></param>
        /// <returns></returns>
        internal static bool IsMRT(RTHandle[] colorBuffers)
        {
            return GetValidColorBufferCount(colorBuffers) > 1;
        }

        /// <summary>
        /// Return true if value can be found in source (without recurring to Linq)
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool Contains(RenderTargetIdentifier[] source, RenderTargetIdentifier value)
        {
            foreach (var identifier in source)
            {
                if (identifier == value)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Return the index where value was found source. Otherwise, return -1. (without recurring to Linq)
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static int IndexOf(RenderTargetIdentifier[] source, RenderTargetIdentifier value)
        {
            for (int i = 0; i < source.Length; ++i)
            {
                if (source[i] == value)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Return the number of RenderTargetIdentifiers in "source" that are valid (not 0) and different from "value" (without recurring to Linq)
        /// </summary>
        /// <param name="source"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static uint CountDistinct(RenderTargetIdentifier[] source, RenderTargetIdentifier value)
        {
            uint count = 0;
            for (int i = 0; i < source.Length; ++i)
            {
                if (source[i] != value && source[i] != 0)
                    ++count;
            }
            return count;
        }

        /// <summary>
        /// Return the index of last valid (i.e different from 0) RenderTargetIdentifiers in "source" (without recurring to Linq)
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        internal static int LastValid(RenderTargetIdentifier[] source)
        {
            for (int i = source.Length - 1; i >= 0; --i)
            {
                if (source[i] != 0)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Return true if ClearFlag a contains ClearFlag b
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        internal static bool Contains(ClearFlag a, ClearFlag b)
        {
            return (a & b) == b;
        }

        /// <summary>
        /// Return true if "left" and "right" are the same (without recurring to Linq)
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        internal static bool SequenceEqual(RenderTargetIdentifier[] left, RenderTargetIdentifier[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; ++i)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        internal static bool MultisampleDepthResolveSupported()
        {
            // Temporarily disabling depth resolve a driver bug on OSX when using some AMD graphics cards. Temporarily disabling depth resolve on that platform
            // TODO: re-enable once the issue is investigated/fixed
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                return false;

            // Should we also check if the format has stencil and check stencil resolve capability only in that case?
            return SystemInfo.supportsMultisampleResolveDepth && SystemInfo.supportsMultisampleResolveStencil;
        }

        /// <summary>
        /// Return true if handle does not match descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="isShadowMap">Set to true if the depth buffer should be used as a shadow map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <param name="scaled">Check if the RTHandle has auto scaling enabled if not, check the widths and heights</param>
        /// <returns></returns>
        internal static bool RTHandleNeedsReAlloc(
            RTHandle handle,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode,
            TextureWrapMode wrapMode,
            bool isShadowMap,
            int anisoLevel,
            float mipMapBias,
            string name,
            bool scaled)
        {
            if (handle == null || handle.rt == null)
                return true;
            if (handle.useScaling != scaled)
                return true;
            if (!scaled && (handle.rt.width != descriptor.width || handle.rt.height != descriptor.height))
                return true;
            return
                handle.rt.descriptor.depthBufferBits != descriptor.depthBufferBits ||
                (handle.rt.descriptor.depthBufferBits == (int)DepthBits.None && !isShadowMap && handle.rt.descriptor.graphicsFormat != descriptor.graphicsFormat) ||
                handle.rt.descriptor.dimension != descriptor.dimension ||
                handle.rt.descriptor.enableRandomWrite != descriptor.enableRandomWrite ||
                handle.rt.descriptor.useMipMap != descriptor.useMipMap ||
                handle.rt.descriptor.autoGenerateMips != descriptor.autoGenerateMips ||
                handle.rt.descriptor.msaaSamples != descriptor.msaaSamples ||
                handle.rt.descriptor.bindMS != descriptor.bindMS ||
                handle.rt.descriptor.useDynamicScale != descriptor.useDynamicScale ||
                handle.rt.descriptor.memoryless != descriptor.memoryless ||
                handle.rt.filterMode != filterMode ||
                handle.rt.wrapMode != wrapMode ||
                handle.rt.anisoLevel != anisoLevel ||
                handle.rt.mipMapBias != mipMapBias ||
                handle.name != name;
        }

        /// <summary>
        /// Returns the RenderTargetIdentifier of the current camera target.
        /// </summary>
        /// <param name="renderingData"></param>
        /// <returns></returns>
        internal static RenderTargetIdentifier GetCameraTargetIdentifier(ref RenderingData renderingData)
        {
            // Note: We need to get the cameraData.targetTexture as this will get the targetTexture of the camera stack.
            // Overlay cameras need to output to the target described in the base camera while doing camera stack.
            ref CameraData cameraData = ref renderingData.cameraData;

            RenderTargetIdentifier cameraTarget = (cameraData.targetTexture != null) ? new RenderTargetIdentifier(cameraData.targetTexture) : BuiltinRenderTextureType.CameraTarget;
#if ENABLE_VR && ENABLE_XR_MODULE
            if (cameraData.xr.enabled)
            {
                if (cameraData.xr.singlePassEnabled)
                {
                    cameraTarget = cameraData.xr.renderTarget;
                }
                else
                {
                    int depthSlice = cameraData.xr.GetTextureArraySlice();
                    cameraTarget = new RenderTargetIdentifier(cameraData.xr.renderTarget, 0, CubemapFace.Unknown, depthSlice);
                }
            }
#endif

            return cameraTarget;
        }

        /// <summary>
        /// Re-allocate fixed-size RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="isShadowMap">Set to true if the depth buffer should be used as a shadow map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns></returns>
        public static bool ReAllocateIfNeeded(
            ref RTHandle handle,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            bool isShadowMap = false,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            if (RTHandleNeedsReAlloc(handle, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name, false))
            {
                handle?.Release();
                handle = RTHandles.Alloc(descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Re-allocate dynamically resized RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="scaleFactor">Constant scale for the RTHandle size computation.</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="isShadowMap">Set to true if the depth buffer should be used as a shadow map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If the RTHandle should be re-allocated</returns>
        public static bool ReAllocateIfNeeded(
            ref RTHandle handle,
            Vector2 scaleFactor,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            bool isShadowMap = false,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            var usingConstantScale = handle != null && handle.useScaling && handle.scaleFactor == scaleFactor;
            if (!usingConstantScale || RTHandleNeedsReAlloc(handle, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name, true))
            {
                handle?.Release();
                handle = RTHandles.Alloc(scaleFactor, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Re-allocate dynamically resized RTHandle if it is not allocated or doesn't match the descriptor
        /// </summary>
        /// <param name="handle">RTHandle to check (can be null)</param>
        /// <param name="scaleFunc">Function used for the RTHandle size computation.</param>
        /// <param name="descriptor">Descriptor for the RTHandle to match</param>
        /// <param name="filterMode">Filtering mode of the RTHandle.</param>
        /// <param name="wrapMode">Addressing mode of the RTHandle.</param>
        /// <param name="isShadowMap">Set to true if the depth buffer should be used as a shadow map.</param>
        /// <param name="anisoLevel">Anisotropic filtering level.</param>
        /// <param name="mipMapBias">Bias applied to mipmaps during filtering.</param>
        /// <param name="name">Name of the RTHandle.</param>
        /// <returns>If an allocation was done</returns>
        public static bool ReAllocateIfNeeded(
            ref RTHandle handle,
            ScaleFunc scaleFunc,
            in RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Point,
            TextureWrapMode wrapMode = TextureWrapMode.Repeat,
            bool isShadowMap = false,
            int anisoLevel = 1,
            float mipMapBias = 0,
            string name = "")
        {
            var usingScaleFunction = handle != null && handle.useScaling && handle.scaleFactor == Vector2.zero;
            if (!usingScaleFunction || RTHandleNeedsReAlloc(handle, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name, true))
            {
                handle?.Release();
                handle = RTHandles.Alloc(scaleFunc, descriptor, filterMode, wrapMode, isShadowMap, anisoLevel, mipMapBias, name);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates <c>DrawingSettings</c> based on current the rendering state.
        /// </summary>
        /// <param name="shaderTagId">Shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns></returns>
        /// <seealso cref="DrawingSettings"/>
        static public DrawingSettings CreateDrawingSettings(ShaderTagId shaderTagId, ref RenderingData renderingData, SortingCriteria sortingCriteria)
        {
            Camera camera = renderingData.cameraData.camera;
            SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortingCriteria };
            DrawingSettings settings = new DrawingSettings(shaderTagId, sortingSettings)
            {
                perObjectData = renderingData.perObjectData,
                mainLightIndex = renderingData.lightData.mainLightIndex,
                enableDynamicBatching = renderingData.supportsDynamicBatching,

                // Disable instancing for preview cameras. This is consistent with the built-in forward renderer. Also fixes case 1127324.
                enableInstancing = camera.cameraType == CameraType.Preview ? false : true,
            };
            return settings;
        }

        /// <summary>
        /// Creates <c>DrawingSettings</c> based on current rendering state.
        /// </summary>
        /// /// <param name="shaderTagIdList">List of shader pass tag to render.</param>
        /// <param name="renderingData">Current rendering state.</param>
        /// <param name="sortingCriteria">Criteria to sort objects being rendered.</param>
        /// <returns></returns>
        /// <seealso cref="DrawingSettings"/>
        static public DrawingSettings CreateDrawingSettings(List<ShaderTagId> shaderTagIdList,
            ref RenderingData renderingData, SortingCriteria sortingCriteria)
        {
            if (shaderTagIdList == null || shaderTagIdList.Count == 0)
            {
                Debug.LogWarning("ShaderTagId list is invalid. DrawingSettings is created with default pipeline ShaderTagId");
                return CreateDrawingSettings(new ShaderTagId("UniversalPipeline"), ref renderingData, sortingCriteria);
            }

            DrawingSettings settings = CreateDrawingSettings(shaderTagIdList[0], ref renderingData, sortingCriteria);
            for (int i = 1; i < shaderTagIdList.Count; ++i)
                settings.SetShaderPassName(i, shaderTagIdList[i]);
            return settings;
        }

        /// <summary>
        /// PackedMipChainInfo from HDRP HDUtils.cs
        /// </summary>
        internal struct PackedMipChainInfo
        {
            public Vector2Int textureSize;
            public int mipLevelCount;
            public Vector2Int[] mipLevelSizes;
            public Vector2Int[] mipLevelOffsets;

            private Vector2 cachedTextureScale;
            private Vector2Int cachedHardwareTextureSize;

            private bool m_OffsetBufferWillNeedUpdate;

            public void Allocate()
            {
                mipLevelOffsets = new Vector2Int[15];
                mipLevelSizes = new Vector2Int[15];
                m_OffsetBufferWillNeedUpdate = true;
            }

            // We pack all MIP levels into the top MIP level to avoid the Pow2 MIP chain restriction.
            // We compute the required size iteratively.
            // This function is NOT fast, but it is illustrative, and can be optimized later.
            public void ComputePackedMipChainInfo(Vector2Int viewportSize)
            {
                bool isHardwareDrsOn = DynamicResolutionHandler.instance.HardwareDynamicResIsEnabled();
                Vector2Int hardwareTextureSize = isHardwareDrsOn ? DynamicResolutionHandler.instance.ApplyScalesOnSize(viewportSize) : viewportSize;
                Vector2 textureScale = isHardwareDrsOn ? new Vector2((float)viewportSize.x / (float)hardwareTextureSize.x, (float)viewportSize.y / (float)hardwareTextureSize.y) : new Vector2(1.0f, 1.0f);

                // No work needed.
                if (cachedHardwareTextureSize == hardwareTextureSize && cachedTextureScale == textureScale)
                    return;

                cachedHardwareTextureSize = hardwareTextureSize;
                cachedTextureScale = textureScale;

                mipLevelSizes[0] = hardwareTextureSize;
                mipLevelOffsets[0] = Vector2Int.zero;

                int mipLevel = 0;
                Vector2Int mipSize = hardwareTextureSize;

                do
                {
                    mipLevel++;

                    // Round up.
                    mipSize.x = Math.Max(1, (mipSize.x + 1) >> 1);
                    mipSize.y = Math.Max(1, (mipSize.y + 1) >> 1);

                    mipLevelSizes[mipLevel] = mipSize;

                    Vector2Int prevMipBegin = mipLevelOffsets[mipLevel - 1];
                    Vector2Int prevMipEnd = prevMipBegin + mipLevelSizes[mipLevel - 1];

                    Vector2Int mipBegin = new Vector2Int();

                    if ((mipLevel & 1) != 0) // Odd
                    {
                        mipBegin.x = prevMipBegin.x;
                        mipBegin.y = prevMipEnd.y;
                    }
                    else // Even
                    {
                        mipBegin.x = prevMipEnd.x;
                        mipBegin.y = prevMipBegin.y;
                    }

                    mipLevelOffsets[mipLevel] = mipBegin;

                    hardwareTextureSize.x = Math.Max(hardwareTextureSize.x, mipBegin.x + mipSize.x);
                    hardwareTextureSize.y = Math.Max(hardwareTextureSize.y, mipBegin.y + mipSize.y);
                }
                while ((mipSize.x > 1) || (mipSize.y > 1));

                textureSize = new Vector2Int(
                    (int)Mathf.Ceil((float)hardwareTextureSize.x * textureScale.x), (int)Mathf.Ceil((float)hardwareTextureSize.y * textureScale.y));

                mipLevelCount = mipLevel + 1;
                m_OffsetBufferWillNeedUpdate = true;
            }

            public ComputeBuffer GetOffsetBufferData(ComputeBuffer mipLevelOffsetsBuffer)
            {
                if (m_OffsetBufferWillNeedUpdate)
                {
                    mipLevelOffsetsBuffer.SetData(mipLevelOffsets);
                    m_OffsetBufferWillNeedUpdate = false;
                }

                return mipLevelOffsetsBuffer;
            }
        }

        internal static float ComputeViewportScale(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Scale by (vp_dim / buf_dim).
            return viewportSize * rcpBufferSize;
        }

        internal static float ComputeViewportLimit(int viewportSize, int bufferSize)
        {
            float rcpBufferSize = 1.0f / bufferSize;

            // Clamp to (vp_dim - 0.5) / buf_dim.
            return (viewportSize - 0.5f) * rcpBufferSize;
        }

        internal static Vector4 ComputeViewportScaleAndLimit(Vector2Int viewportSize, Vector2Int bufferSize)
        {
            return new Vector4(ComputeViewportScale(viewportSize.x, bufferSize.x),  // Scale(x)
                ComputeViewportScale(viewportSize.y, bufferSize.y),                 // Scale(y)
                ComputeViewportLimit(viewportSize.x, bufferSize.x),                 // Limit(x)
                ComputeViewportLimit(viewportSize.y, bufferSize.y));                // Limit(y)
        }

        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(float verticalFoV, Vector2 lensShift, Vector4 screenSize, Matrix4x4 worldToViewMatrix, bool renderToCubemap, float aspectRatio = -1, bool isOrthographic = false)
        {
            Matrix4x4 viewSpaceRasterTransform;

            if (isOrthographic)
            {
                // For ortho cameras, project the skybox with no perspective
                // the same way as builtin does (case 1264647)
                viewSpaceRasterTransform = new Matrix4x4(
                    new Vector4(-2.0f * screenSize.z, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, -2.0f * screenSize.w, 0.0f, 0.0f),
                    new Vector4(1.0f, 1.0f, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 0.0f));
            }
            else
            {
                // Compose the view space version first.
                // V = -(X, Y, Z), s.t. Z = 1,
                // X = (2x / resX - 1) * tan(vFoV / 2) * ar = x * [(2 / resX) * tan(vFoV / 2) * ar] + [-tan(vFoV / 2) * ar] = x * [-m00] + [-m20]
                // Y = (2y / resY - 1) * tan(vFoV / 2)      = y * [(2 / resY) * tan(vFoV / 2)]      + [-tan(vFoV / 2)]      = y * [-m11] + [-m21]

                aspectRatio = aspectRatio < 0 ? screenSize.x * screenSize.w : aspectRatio;
                float tanHalfVertFoV = Mathf.Tan(0.5f * verticalFoV);

                // Compose the matrix.
                float m21 = (1.0f - 2.0f * lensShift.y) * tanHalfVertFoV;
                float m11 = -2.0f * screenSize.w * tanHalfVertFoV;

                float m20 = (1.0f - 2.0f * lensShift.x) * tanHalfVertFoV * aspectRatio;
                float m00 = -2.0f * screenSize.z * tanHalfVertFoV * aspectRatio;

                if (renderToCubemap)
                {
                    // Flip Y.
                    m11 = -m11;
                    m21 = -m21;
                }

                viewSpaceRasterTransform = new Matrix4x4(new Vector4(m00, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, m11, 0.0f, 0.0f),
                    new Vector4(m20, m21, -1.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            }

            // Remove the translation component.
            var homogeneousZero = new Vector4(0, 0, 0, 1);
            worldToViewMatrix.SetColumn(3, homogeneousZero);

            // Flip the Z to make the coordinate system left-handed.
            worldToViewMatrix.SetRow(2, -worldToViewMatrix.GetRow(2));

            // Transpose for HLSL.
            return Matrix4x4.Transpose(worldToViewMatrix.transpose * viewSpaceRasterTransform);
        }

        /// <summary>
        /// Compute the matrix from screen space (pixel) to world space direction (RHS).
        /// 
        /// You can use this matrix on the GPU to compute the direction to look in a cubemap for a specific
        /// screen pixel.
        /// </summary>
        /// <param name="camera"></param>
        /// <param name="viewMatrix"></param>
        /// <param name="gpuProjMatrix"></param>
        /// <param name="resolution"></param>
        /// <param name="aspect">
        /// The aspect ratio to use.
        ///
        /// if negative, then the aspect ratio of <paramref name="resolution"/> will be used.
        ///
        /// It is different from the aspect ratio of <paramref name="resolution"/> for anamorphic projections.
        /// </param>
        /// <returns></returns>
        internal static Matrix4x4 ComputePixelCoordToWorldSpaceViewDirectionMatrix(Camera camera, Matrix4x4 viewMatrix, Matrix4x4 gpuProjMatrix, Vector4 resolution, float aspect = -1)
        {
            //// In XR mode, or if explicitely required, use a more generic matrix to account for asymmetry in the projection
            //var useGenericMatrix = xr.enabled || frameSettings.IsEnabled(FrameSettingsField.AsymmetricProjection);

            // Asymmetry is also possible from a user-provided projection, so we must check for it too.
            // Note however, that in case of physical camera, the lens shift term is the only source of
            // asymmetry, and this is accounted for in the optimized path below. Additionally, Unity C++ will
            // automatically disable physical camera when the projection is overridden by user.
            //useGenericMatrix |= HDUtils.IsProjectionMatrixAsymmetric(viewConstants.projMatrix) && !camera.usePhysicalProperties;

            //if (useGenericMatrix)
            //{
            //    var viewSpaceRasterTransform = new Matrix4x4(
            //        new Vector4(2.0f * resolution.z, 0.0f, 0.0f, -1.0f),
            //        new Vector4(0.0f, -2.0f * resolution.w, 0.0f, 1.0f),
            //        new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
            //        new Vector4(0.0f, 0.0f, 0.0f, 1.0f));

            //    var transformT = viewConstants.invViewProjMatrix.transpose * Matrix4x4.Scale(new Vector3(-1.0f, -1.0f, -1.0f));
            //    return viewSpaceRasterTransform * transformT;
            //}

            float verticalFoV = camera.GetGateFittedFieldOfView() * Mathf.Deg2Rad;
            if (!camera.usePhysicalProperties)
            {
                verticalFoV = Mathf.Atan(-1.0f / gpuProjMatrix[1, 1]) * 2;
            }
            Vector2 lensShift = camera.GetGateFittedLensShift();

            return ComputePixelCoordToWorldSpaceViewDirectionMatrix(verticalFoV, lensShift, resolution, viewMatrix, false, aspect, camera.orthographic);
        }

        /// <summary>Get the aspect ratio of a projection matrix.</summary>
        /// <param name="matrix"></param>
        /// <returns></returns>
        internal static float ProjectionMatrixAspect(in Matrix4x4 matrix)
            => -matrix.m11 / matrix.m00;

        /// <summary>
        /// Compute kernel size.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        internal static int DivRoundUp(int x, int y) => (x + y - 1) / y;
    }
}
