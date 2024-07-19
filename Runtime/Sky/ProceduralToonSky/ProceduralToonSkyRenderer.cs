
using static UnityEngine.Rendering.Universal.ShaderInput;

namespace UnityEngine.Rendering.Universal
{
    public class ProceduralToonSkyRenderer : SkyRenderer
    {
        MaterialPropertyBlock m_PropertyBlock;

        public ProceduralToonSkyRenderer()
        {
            SupportDynamicSunLight = false;
        }

        public override void Build()
        {
            m_PropertyBlock = new MaterialPropertyBlock();
        }

        public override void Cleanup()
        {
        }

        public override void RenderSky(CommandBuffer cmd, SkyBasePassData basePassData, SkySettings skySettings, bool renderForCubemap)
        {
            var proceduralToonSky = skySettings as ProceduralToonSky;

            Material skyMaterial = proceduralToonSky.material.value;
            skyMaterial = skyMaterial == null ? ProceduralToonSky.defaultMaterial : skyMaterial;

            // Get mainLight
            var lightData = basePassData.lightData;
            int shadowLightIndex = lightData.mainLightIndex;
            Light mainLight = shadowLightIndex == -1 ? null : lightData.visibleLights[shadowLightIndex].light;
            
            float timeOfDay = TimeOfDaySystem.GetTimeOfDayFromLight(mainLight);

            // Set material
            skyMaterial.SetFloat(ShaderConstants._TimeOfDay, timeOfDay);
            skyMaterial.SetInt(ShaderConstants._RenderSunDisk, skySettings.includeSunInBaking.value ? 1 : 0);

            // This matrix needs to be updated at the draw call frequency.
            m_PropertyBlock.SetMatrix(ShaderConstants._PixelCoordToViewDirWS, basePassData.pixelCoordToViewDirMatrix);

            CoreUtils.DrawFullScreen(cmd, skyMaterial, m_PropertyBlock, renderForCubemap ? 0 : 1);
        }

        static class ShaderConstants
        {
            public static readonly int _TimeOfDay = Shader.PropertyToID("_TimeOfDay");
            public static readonly int _RenderSunDisk = Shader.PropertyToID("_RenderSunDisk");
            public static readonly int _PixelCoordToViewDirWS = Shader.PropertyToID("_PixelCoordToViewDirWS");
        }
    }
}