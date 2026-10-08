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

        /// <summary>游戏资源目录（StreamingAssets）下放本模组素材的文件夹名；pxls 路径字符串里也用到它。</summary>
        const string GameResDir = "PolarisNoelsResources";

        public static void Load()
        {
            Plugin.Logger.LogInfo("start loading PolarisNoels resources..");

            // 动画（pxls）走游戏自己的加载器，它只认 StreamingAssets，所以只把 pxls 摆过去；Core 只复制新增或有变化的文件。
            // 预览图和标题图是普通 PNG，直接由 Core 的 Res 从模组目录加载，不用拷。
            string pluginPath = Path.GetDirectoryName(typeof(MTRExtension).Assembly.Location);
            res = ResAPI.For(MyPluginInfo.PLUGIN_GUID, pluginPath);
            res.MountToGame("pxls", GameResDir + "/pxls");

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

        public static PrPoseContainer LoadExtenalPxl(string[][] pxlPath, string name)
        {
            LoadTicketManager.PrepareLoadManager();
            LoadTicketManager instance = LoadTicketManager.Instance;
            int num = pxlPath.Length;
            for (int i = 0; i < num; i++)
            {
                int num2 = pxlPath[i].Length;
                for (int j = 0; j < num2; j++)
                {
                    string text = GameResDir + "/pxls/" + pxlPath[i][j] + ".pxls";
                    MTIOneImage mtioneImage;
                    PxlCharacter pxlCharacter = MTRX.loadMtiPxc(out mtioneImage, pxlPath[i][j], text, "_", true, true, true);
                    instance.AddTicketInner(pxlCharacter, mtioneImage, 1);
                }
            }
            CaneManager.reloadScript(false);
            return new PrPoseContainer(name, GameResDir + "/pxls/", "_", delegate (PxlFrame F, float rCLENB)
            {
                float num3;
                float num4;
                return M2PxlAnimator.getRodPosS(rCLENB, F, out num3, out num4, "rod", "ROD", 0.5f, 0f, ALIGN.LEFT, ALIGNY.MIDDLE, 2, "rodeff");
            });
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
