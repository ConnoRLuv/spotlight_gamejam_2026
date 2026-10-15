using System;
using UnityEngine;

namespace RoguelikeMap.Unity
{
    // 代码生成的兜底图形（圆底、高亮圆环）。
    //
    // 存在的意义：没有任何美术资源时，地图也要看起来像一张地图，而不是一排灰色方块。
    // 这些贴图（Sprite）是运行时按需算出来的，不会写进工程，所以不会污染美术目录。
    public static class MapSpriteFactory
    {
        const int Resolution = 128;

        static Sprite _circle;
        static Sprite _ring;

        // 柔边实心圆，用作没有图标时的节点底图。
        public static Sprite CircleSprite
        {
            get
            {
                // 静态缓存可能会跨播放会话留下来（域重载被关掉时），那时候旧图已经失效了，重建一张
                if (!IsUsable(_circle)) _circle = null;
                if (_circle == null) _circle = BuildCircle();
                return _circle;
            }
        }

        // 空心圆环，用作"这一步可以走"的高亮。
        public static Sprite RingSprite
        {
            get
            {
                if (!IsUsable(_ring)) _ring = null;
                if (_ring == null) _ring = BuildRing();
                return _ring;
            }
        }

        static Sprite BuildCircle()
        {
            var pixels = new Color32[Resolution * Resolution];
            FillCircle(pixels, Resolution);
            return Create(pixels, "MapCircle");
        }

        static Sprite BuildRing()
        {
            var pixels = new Color32[Resolution * Resolution];
            FillRing(pixels, Resolution);
            return Create(pixels, "MapRing");
        }

        // 柔边实心圆的像素。编辑器工具会拿它落成一张 PNG 图片，所以这里跟贴图（Sprite）版本共用同一份算法。
        public static void FillCircle(Color32[] pixels, int size)
        {
            float half = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2((x - half) / half, (y - half) / half).magnitude;

                    // 中间实、边缘渐隐，缩放后不会有锯齿
                    float alpha = 1f - Mathf.SmoothStep(0.88f, 1f, d);
                    float shade = Mathf.Lerp(1f, 0.72f, Mathf.Clamp01(d));
                    alpha *= Mathf.Lerp(1f, 0.92f, Mathf.Clamp01(d));

                    pixels[y * size + x] = new Color32(
                        (byte)(shade * 255f), (byte)(shade * 255f), (byte)(shade * 255f), (byte)(alpha * 255f));
                }
            }
        }

        // 空心圆环的像素。
        public static void FillRing(Color32[] pixels, int size)
        {
            float half = (size - 1) * 0.5f;
            const float inner = 0.78f;
            const float mid = 0.90f;
            const float outer = 1.02f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2((x - half) / half, (y - half) / half).magnitude;
                    float alpha = Mathf.SmoothStep(inner, mid, d) * (1f - Mathf.SmoothStep(mid, outer, d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
        }

        static Sprite Create(Color32[] pixels, string name)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, Resolution, Resolution),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // 这张图还能不能安全地挂到图片组件上。
        //
        // 美术被删掉、或者资源重新导入过之后，序列化里留下的引用会变成一个"已经不在了"的空壳。
        // 这种空壳不等于 null，直接赋给图片组件会在生成网格的时候抛异常（一屏节点能刷出上百条），
        // 所以挂图之前先用这里挡一道，发现问题就退回到代码生成的兜底图形。
        public static bool IsUsable(Sprite sprite)
        {
            if (sprite == null) return true;   // 没配图是正常情况，交给上层走兜底

            try { return sprite.texture != null; }
            catch (Exception) { return false; }
        }

        // 贴图版的同一道检查，给原始图片组件（RawImage）用。
        public static bool IsUsable(Texture texture)
        {
            if (texture == null) return true;

            try { return texture.width > 0; }
            catch (Exception) { return false; }
        }
    }
}
