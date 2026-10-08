using m2d;
using nel;
using PixelLiner;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Polaris;
using Polaris.Res;
using Polaris.Res.Import;
using Polaris.Res.Pxls;
using System.Linq;
using PolarisNoels.DataStruct;
using XX;

namespace PolarisNoels
{
    public static class MTRExtension
    {
        public static string[][] Anoel_inverse_pxls
        {
            get =>
            [
                ["noel_inverse", "noel_inverse_r18", "noel_inverse_magic"]
            ];
        }

        public static string PreviewPrefix
        {
            get => "Preview_Noel_";
        }

        static Dictionary<ColorNoelColor, PrPoseContainer> colorDics = [];

        public static Dictionary<NoelType, MImage[]> NoelPreviews = [];

        public static Dictionary<ColorNoelColor, MImage[]> ColorPreviews = [];

        public static string GetColorNoelName(ColorNoelColor color) => "noel_" + color.ToString().ToLower();
        public static string GetColorNoelMagicName(ColorNoelColor color) => "noel_magic_" + color.ToString().ToLower();

        public static string[][] GetColorNoelPxlsFull(ColorNoelColor color) => [[GetColorNoelName(color), GetColorNoelMagicName(color)]];

        public static PrPoseContainer GetPrPoseContainer(ColorNoelColor color) => colorDics[color];

        public static PrPoseContainer PConNoelIAnim;

        public const string LOCALIZATION_FILE_NAME = "_polarisnoels_localization";

        /// <summary>本模组的 Core 资源句柄；预览图和标题图用它按 PNG 加载。</summary>
        static ModResources res;

        /// <summary>图片租约在进程生命周期内一直持有，不释放（这些图整个游戏期间都要用）。</summary>
        static readonly List<IDisposable> imageLeases = [];

        static readonly TextureImportSettings ImageSettings = new() { FilterMode = FilterMode.Bilinear };

        /// <summary>标题"多人"确认页用的大图；首次取用时才加载，之后复用。</summary>
        public static MImage MultiplayerImage => multiplayerImage ??= LoadModImage("resources/multiplayer.png");

        static MImage multiplayerImage;

        /// <summary>从模组目录加载一张 PNG 成游戏的 <see cref="MImage"/>；找不到或解码失败时记日志并返回 null。</summary>
        public static MImage LoadModImage(string path)
        {
            try
            {
                IResourceLease<MImage> lease = res.Image(path, ImageSettings);
                imageLeases.Add(lease);
                return lease.Value;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError($"Failed to load image {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>构造 <c>PrPoseContainer</c> 时要的 pxl_dir；Core 加载的 pxls 不走它，仅为满足构造参数。</summary>
        const string GameResDir = "PolarisNoelsResources";

        public static void Load()
        {
            Plugin.Logger.LogInfo("start loading PolarisNoels resources..");

            // 素材（pxls、预览图、标题图）都由 Core 的 Res 直接从模组目录加载，不再往游戏的 StreamingAssets 里拷。
            string pluginPath = Path.GetDirectoryName(typeof(MTRExtension).Assembly.Location);
            res = ResAPI.For(MyPluginInfo.PLUGIN_GUID, pluginPath);

            // 文案文件交给 Core 直接读，不拷进游戏目录。
            PolarisAPI.Localization.AddTextFiles(pluginPath, LOCALIZATION_FILE_NAME);

            Plugin.Logger.LogInfo("PolarisNoels resources load complete!");
        }

        public static void LoadAllPxls()
        {
            PConNoelIAnim = LoadExtenalPxl(Anoel_inverse_pxls, "noel_inverse");
            for (int i = 0; i < 2; i++)
            {
                NoelType type = (NoelType)i;
                MImage[] previews = new MImage[12];
                for (int j = 0; j < 12; j++)
                {
                    previews[j] = LoadImage(type, j);
                }
                NoelPreviews.Add(type, previews);
            }
            for (int i = 0; i < 8; i++)
            {
                ColorNoelColor color = (ColorNoelColor)i;
                colorDics.Add(color, LoadExtenalPxl(GetColorNoelPxlsFull(color), GetColorNoelName(color)));
                MImage[] previews = new MImage[12];
                for (int j = 0; j < 12; j++)
                {
                    previews[j] = LoadImage(color, j);
                }
                ColorPreviews.Add(color, previews);
            }
        }

        /// <summary>用 Core 的 Res 加载的 pxls：容器 → 它名下各角色的加载句柄。游戏原有的"重新载入贴图"流程认不得这些角色，靠它识别并绕开。</summary>
        static readonly Dictionary<PrPoseContainer, List<PxlsCharacterHandle>> coreLoaded = [];

        /// <summary>角色租约在进程生命周期内一直持有，不释放。</summary>
        static readonly List<IDisposable> pxlLeases = [];

        /// <summary>该容器是不是由 Core 的 Res 加载的 pxls 组成的。</summary>
        public static bool IsCoreLoaded(PrPoseContainer container) => container != null && coreLoaded.ContainsKey(container);

        /// <summary>容器名下的角色是否都已加载完、贴图已换好。</summary>
        public static bool AllReady(PrPoseContainer container)
        {
            if (!coreLoaded.TryGetValue(container, out List<PxlsCharacterHandle> handles))
            {
                return false;
            }

            foreach (PxlsCharacterHandle handle in handles)
            {
                if (!handle.IsReady)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 通过 Core 的 Res 从模组目录加载 pxls（原始 <c>.pxls</c> 加同名图集 PNG），并登记成游戏里按原名查找的角色，
        /// 所以 <c>PrPoseContainer.iniPxlResourcesASync</c> 照旧用名字取角色。加载是异步的，那边会等到加载完成。
        /// </summary>
        public static PrPoseContainer LoadExtenalPxl(string[][] pxlPath, string name)
        {
            var handles = new List<PxlsCharacterHandle>();
            foreach (string[] group in pxlPath)
            {
                foreach (string pxlName in group)
                {
                    // 原版素材是 Bilinear 过滤；Title 取原名，PrPoseContainer 按它查角色。
                    IResourceLease<PxlsCharacterHandle> lease = res.Pxls("pxls/" + pxlName + ".pxls", new PxlsImportSettings
                    {
                        Title = pxlName,
                        Texture = ImageSettings,
                    });
                    pxlLeases.Add(lease);
                    handles.Add(lease.Value);
                }
            }

            CaneManager.reloadScript(false);
            var container = new PrPoseContainer(name, GameResDir + "/pxls/", "_", delegate (PxlFrame F, float rCLENB)
            {
                float num3;
                float num4;
                return M2PxlAnimator.getRodPosS(rCLENB, F, out num3, out num4, "rod", "ROD", 0.5f, 0f, ALIGN.LEFT, ALIGNY.MIDDLE, 2, "rodeff");
            });
            coreLoaded[container] = handles;
            return container;
        }

        public static MImage LoadImage(NoelType type, int index)
        {
            if (index < 0 | index > 11)
            {
                return null;
            }
            string name = PreviewPrefix + type.ToString() + index.ToString().PadLeft(2, '0');
            name = name.ToLower();
            if (type == NoelType.Normal)
            {
                name = "preview_noel" + index.ToString().PadLeft(2, '0');
            }
            return LoadModImage("pics/" + name + ".png");
        }

        public static MImage LoadImage(ColorNoelColor color, int index)
        {
            if (index < 0 | index > 11)
            {
                return null;
            }
            string name = PreviewPrefix + color.ToString() + index.ToString().PadLeft(2, '0');
            name = name.ToLower();
            return LoadModImage("pics/" + name + ".png");
        }
    }
}
