using UnityEngine;
using WeNeedMoreNoels.DataStruct;

namespace WeNeedMoreNoels
{
    public class NoelPreview : MonoBehaviour
    {
        SpriteRenderer renderer;

        public ColorNoelColor color;

        public NoelType noelType;

        void Awake()
        {
            renderer = GetComponent<SpriteRenderer>();
        }

        void Update()
        {
            int frameCount = 12; // 12帧动画
            int curFrame = (int)(Time.time * 6f) % frameCount; // 6fps
            Texture t;
            switch (noelType)
            {
                case NoelType.Normal:
                    t = MTRExtension.NoelPreviews[NoelType.Normal][curFrame].Tx;
                    break;
                case NoelType.Inverse:
                    t = MTRExtension.NoelPreviews[NoelType.Inverse][curFrame].Tx;
                    break;
                case NoelType.ColorNoel:
                    t = MTRExtension.ColorPreviews[color][curFrame].Tx;
                    break;
                default:
                    return;
            }
            Texture2D t2d = TextureToTexture2D(t);
            Sprite s = Sprite.Create(t2d, new Rect(0, 0, t2d.width, t2d.height), Vector2.zero);
            renderer.sprite = s;
        }

        Texture2D TextureToTexture2D(Texture texture)
        {
            Texture2D texture2D = new(texture.width, texture.height, TextureFormat.RGBA32, false);
            RenderTexture currentRT = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 32);
            Graphics.Blit(texture, renderTexture);
            RenderTexture.active = renderTexture;
            texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = currentRT;
            RenderTexture.ReleaseTemporary(renderTexture);
            return texture2D;
        }
    }
}
