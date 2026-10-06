using m2d;
using nel;
using PixelLiner;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using WeNeedMoreNoels.DataStruct;
using XX;

namespace WeNeedMoreNoels
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

        public const string LOCALIZATION_FILE_NAME = "_wnmn_localization";

        static string localPicPath;

        public static void Load()
        {
            Plugin.Logger.LogInfo("start loading WNMN resources..");
            string assetPath = Path.GetFullPath(Application.streamingAssetsPath);
            string assetOriginPath = Path.Combine(assetPath, "WNMNResources\\");
            string localPxlPath = Path.Combine(assetOriginPath, "pxls\\");
            string localPicPath = Path.Combine(assetOriginPath, "pics\\");
            string localLocalizationPath = Path.Combine(assetPath, "localization\\");
            string L_zhPath = Path.Combine(localLocalizationPath, "zh-cn\\");
            string L_zhtcPath = Path.Combine(localLocalizationPath, "zh-tc\\");
            string L_enPath = Path.Combine(localLocalizationPath, "en\\");
            string L_jpPath = Path.Combine(localLocalizationPath, "_\\");
            string L_krPath = Path.Combine(localLocalizationPath, "ko-kr\\");
            string L_thPath = Path.Combine(localLocalizationPath, "th\\");
            string pluginFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "plugins");
            string pluginPath = Path.Combine(pluginFolderPath, "WNMN");
            string pluginPxlPath = Path.Combine(pluginPath, "pxls\\");
            string pluginPicPath = Path.Combine(pluginPath, "pics\\");
            string pluginResPath = Path.Combine(pluginPath, "resources\\");
            string zhPath = Path.Combine(pluginPath, "zh-cn\\");
            string zhtcPath = Path.Combine(pluginPath, "zh-tc\\");
            string enPath = Path.Combine(pluginPath, "en\\");
            string jpPath = Path.Combine(pluginPath, "_\\");
            string krPath = Path.Combine(pluginPath, "ko-kr\\");
            string thPath = Path.Combine(pluginPath, "th\\");
            DB.Plugin_local_path = pluginPath;
            DB.Game_streaming_asset = assetOriginPath;
            if (!Directory.Exists(assetOriginPath))
            {
                Directory.CreateDirectory(assetOriginPath);
            }
            if (!Directory.Exists(localPxlPath))
            {
                Directory.CreateDirectory(localPxlPath);
            }
            if (!Directory.Exists(localPicPath))
            {
                Directory.CreateDirectory(localPicPath);
            }
            Plugin.Logger.LogInfo("Directory check success.");
            foreach (FileInfo file in new DirectoryInfo(pluginPxlPath).EnumerateFiles())
            {
                string targetFile = localPxlPath + file.Name;
                File.Copy(file.FullName, targetFile, true);
            }
            foreach (FileInfo file in new DirectoryInfo(pluginPicPath).EnumerateFiles())
            {
                string targetFile = localPicPath + file.Name;
                File.Copy(file.FullName, targetFile, true);
            }
            foreach (FileInfo file in new DirectoryInfo(pluginResPath).EnumerateFiles())
            {
                string targetFile = assetOriginPath + file.Name;
                File.Copy(file.FullName, targetFile, true);
            }
            File.Copy(zhPath + $"zh-cn{LOCALIZATION_FILE_NAME}.txt", L_zhPath + $"zh-cn{LOCALIZATION_FILE_NAME}.txt", true);
            File.Copy(zhtcPath + $"zh-tc{LOCALIZATION_FILE_NAME}.txt", L_zhtcPath + $"zh-tc{LOCALIZATION_FILE_NAME}.txt", true);
            File.Copy(enPath + $"en{LOCALIZATION_FILE_NAME}.txt", L_enPath + $"en{LOCALIZATION_FILE_NAME}.txt", true);
            File.Copy(jpPath + $"_{LOCALIZATION_FILE_NAME}.txt", L_jpPath + $"_{LOCALIZATION_FILE_NAME}.txt", true);
            File.Copy(krPath + $"ko-kr{LOCALIZATION_FILE_NAME}.txt", L_krPath + $"ko-kr{LOCALIZATION_FILE_NAME}.txt", true);
            File.Copy(thPath + $"th{LOCALIZATION_FILE_NAME}.txt", L_thPath + $"th{LOCALIZATION_FILE_NAME}.txt", true);
            Plugin.Logger.LogInfo("WNMN resources load complete!");
            MTRExtension.localPicPath = localPicPath;
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
                    string text = "WNMNResources/pxls/" + pxlPath[i][j] + ".pxls";
                    MTIOneImage mtioneImage;
                    PxlCharacter pxlCharacter = MTRX.loadMtiPxc(out mtioneImage, pxlPath[i][j], text, "_", true, true, true);
                    instance.AddTicketInner(pxlCharacter, mtioneImage, 1);
                }
            }
            CaneManager.reloadScript(false);
            return new PrPoseContainer(name, "WNMNResources/pxls/", "_", delegate (PxlFrame F, float rCLENB)
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
            return MTI.LoadContainerOneImage(localPicPath + name).MI;
        }

        public static MImage LoadImage(ColorNoelColor color, int index)
        {
            if (index < 0 | index > 11)
            {
                return null;
            }
            string name = PreviewPrefix + color.ToString() + index.ToString().PadLeft(2, '0');
            name = name.ToLower();
            return MTI.LoadContainerOneImage(localPicPath + name).MI;
        }
    }
}
