using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Editor
{
    /// <summary>读取源 PNG 烘焙同侧接点和全图像素行程，不修改美术文件及导入配置。</summary>
    public static class MapRouteConstraintsBaker
    {
        private const byte AlphaCutoff = 1;
        private const byte PortAlphaCutoff = 24;
        private const string CurrentAsset = "Assets/GameData/Map/MapGenerationConfig.asset";

        /// <summary>仅更新当前地图的约束配置；原制式资源和其他地图保持原状。</summary>
        [MenuItem("Spotlight/Map/Bake Asset Constrained Routes")]
        public static void BakeCurrentMap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>(CurrentAsset);
            if (!asset) throw new InvalidOperationException("当前地图配置不存在。");
            var profile = new MapRouteProfile();
            // 房间类型在铺路后确定，先为所有原图中最大的矩形留足空间；不改图标导入尺寸。
            foreach (var visual in asset.Visuals)
            {
                if (!visual.Icon) continue;
                profile.NodeClearanceWidth = Math.Max(profile.NodeClearanceWidth, Mathf.CeilToInt(visual.Icon.rect.width));
                profile.NodeClearanceHeight = Math.Max(profile.NodeClearanceHeight, Mathf.CeilToInt(visual.Icon.rect.height));
            }
            var bindings = new List<MapConstraintSpriteBinding>();
            foreach (var route in asset.Routes.OrderBy(r => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(r.Sprite))))
            {
                if (!route.Sprite) throw new InvalidOperationException("路线资源缺失。");
                string path = AssetDatabase.GetAssetPath(route.Sprite.texture);
                string id = AssetDatabase.AssetPathToGUID(path) + ":" + route.Sprite.name;
                if (profile.FindFootprint(id) != null) continue;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(path))) throw new InvalidOperationException("无法读取原图：" + path);
                    var pixels = texture.GetPixels32();
                    int width = texture.width, height = texture.height;
                    if (route.Sprite.rect.width != width || route.Sprite.rect.height != height)
                        throw new InvalidOperationException("约束路线需要完整原尺寸 Sprite：" + path);
                    var footprint = new MapRouteFootprint { Id = id, Width = width, Height = height, Runs = Encode(pixels, width, height) };
                    profile.Footprints.Add(footprint); bindings.Add(new MapConstraintSpriteBinding { FootprintId = id, Sprite = route.Sprite });
                    var groups = Groups(pixels, width, height);
                    for (int a = 0; a < groups.Count; a++) for (int b = 0; b < groups.Count; b++)
                    {
                        var from = groups[a]; var to = groups[b];
                        if (!Supported(from.Side, to.Side)) continue;
                        var ac = NodeCenter(from, profile); var bc = NodeCenter(to, profile);
                        if (bc.y - ac.y < height * .4f) continue;
                        int dc = Math.Abs(bc.x - ac.x) <= profile.NodeWidth / 2 ? 0 : bc.x > ac.x ? 1 : -1;
                        // 窄竖图的边缘也有横向像素，不能把这些短线头误识别为跨列斜线。
                        if (dc != 0 && Math.Abs(bc.x - ac.x) < profile.NodeWidth) continue;
                        if ((from.Side == MapSocketSide.Up && to.Side == MapSocketSide.Down) != (dc == 0)) continue;
                        profile.Templates.Add(new MapRouteTemplate { Id = id + ":" + a + "-" + b, FootprintId = id, ColumnDelta = dc,
                            SourceSide = from.Side, TargetSide = to.Side, Source = from.Port, Target = to.Port });
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(texture); }
            }
            profile.Templates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (profile.Templates.Select(t => t.SourceSide + ":" + t.TargetSide).Distinct().Count() < 5)
                throw new InvalidOperationException("当前资产未识别出全部五种接法，请检查图片边缘接点。");
            // 原竖线与节点覆盖区决定层距上限，横向给完整斜线与节点留出足够空间。
            profile.RowSpacing = profile.Footprints.Where(f => f.Width < profile.NodeWidth).Min(f => f.Height) + profile.NodeHeight - 2;
            profile.ColumnSpacing = profile.Footprints.Max(f => f.Width) + profile.NodeWidth / 2 - 18;
            asset.ConfigureAssetConstraints(profile, bindings);
            EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset);
            Directory.CreateDirectory("Logs/MapRoutes");
            File.WriteAllText("Logs/MapRoutes/constraint-profile.json", JsonUtility.ToJson(profile));
            Debug.Log("[地图约束烘焙] " + profile.Footprints.Count + " 张原图，" + profile.Templates.Count + " 种接法，指纹=" + profile.ComputeHash());
        }

        /// <summary>记录像素行上全部可见段，装饰圆环和短支线一并参与碰撞。</summary>
        private static int[] Encode(Color32[] pixels, int width, int height)
        {
            var runs = new List<int>();
            for (int y = 0; y < height; y++)
            {
                int x = 0;
                while (x < width)
                {
                    while (x < width && pixels[y * width + x].a < AlphaCutoff) x++;
                    int start = x;
                    while (x < width && pixels[y * width + x].a >= AlphaCutoff) x++;
                    if (start < x) { runs.Add(y); runs.Add(start); runs.Add(x); }
                }
            }
            return runs.ToArray();
        }

        /// <summary>一条图片边界上的双轨接点组，绝不与相邻的另一条边界平均。</summary>
        private sealed class PortGroup { public MapSocketSide Side; public MapRoutePort Port; }

        /// <summary>分别扫描四条图片边界；一个角落可以导出水平或垂直接法供搜索选择。</summary>
        private static List<PortGroup> Groups(Color32[] pixels, int width, int height)
        {
            var result = new List<PortGroup>();
            for (int border = 0; border < 4; border++)
            {
                bool horizontal = border == 0 || border == 2;
                int length = horizontal ? width : height;
                PortGroup group = null; int last = -length;
                for (int coordinate = 0; coordinate < length;)
                {
                    while (coordinate < length && !Opaque(pixels, width, height, border, coordinate, 0)) coordinate++;
                    int start = coordinate;
                    while (coordinate < length && Opaque(pixels, width, height, border, coordinate, 0)) coordinate++;
                    if (start == coordinate) continue;
                    int end = coordinate - 1, middle = (start + end) / 2;
                    int lead = 0, maxLead = horizontal ? height : width;
                    while (lead < maxLead && Opaque(pixels, width, height, border, middle, lead)) lead++;
                    if (group == null || start - last > length * .35f)
                    {
                        group = new PortGroup { Side = (MapSocketSide)((border + 2) % 4), Port = new MapRoutePort { MinX = width, MinY = height, MaxX = -1, MaxY = -1, LeadLength = maxLead } };
                        result.Add(group);
                    }
                    var p = group.Port;
                    BorderPoint(width, height, border, start, out int x0, out int y0);
                    BorderPoint(width, height, border, end, out int x1, out int y1);
                    p.MinX = Math.Min(p.MinX, Math.Min(x0, x1)); p.MaxX = Math.Max(p.MaxX, Math.Max(x0, x1));
                    p.MinY = Math.Min(p.MinY, Math.Min(y0, y1)); p.MaxY = Math.Max(p.MaxY, Math.Max(y0, y1)); p.LeadLength = Math.Min(p.LeadLength, lead);
                    last = end;
                }
            }
            return result;
        }

        /// <summary>读取边界向内某个像素，不要求修改纹理的 Read/Write 导入属性。</summary>
        private static bool Opaque(Color32[] pixels, int width, int height, int border, int coordinate, int depth)
        {
            BorderPoint(width, height, border, coordinate, out int x, out int y);
            if (border == 0) y -= depth; else if (border == 1) x -= depth; else if (border == 2) y += depth; else x += depth;
            // 接点分组取实体线芯，避免抗锯齿噪声形成假接口；占用掩码仍包含所有非零透明度像素。
            return x >= 0 && x < width && y >= 0 && y < height && pixels[y * width + x].a >= PortAlphaCutoff;
        }

        /// <summary>把边界上的一维坐标转换为原图像素坐标，y 向上。</summary>
        private static void BorderPoint(int width, int height, int border, int coordinate, out int x, out int y)
        { x = border == 1 ? width - 1 : border == 3 ? 0 : coordinate; y = border == 0 ? height - 1 : border == 2 ? 0 : coordinate; }

        /// <summary>估计未嵌入时的节点中心，用于判断图片能够直接连接的上下层和列偏移。</summary>
        private static Vector2Int NodeCenter(PortGroup group, MapRouteProfile profile)
        {
            var p = group.Port; var point = new Vector2Int((p.MinX + p.MaxX) / 2, (p.MinY + p.MaxY) / 2);
            if (group.Side == MapSocketSide.Up) point.y -= profile.NodeHeight / 2;
            else if (group.Side == MapSocketSide.Down) point.y += profile.NodeHeight / 2;
            else if (group.Side == MapSocketSide.Right) point.x -= profile.NodeWidth / 2;
            else point.x += profile.NodeWidth / 2;
            return point;
        }

        /// <summary>仅接受用户指定的五种真实接法，不根据图片文件名推断方向。</summary>
        private static bool Supported(MapSocketSide from, MapSocketSide to) =>
            from == MapSocketSide.Up && (to == MapSocketSide.Down || to == MapSocketSide.Left || to == MapSocketSide.Right) ||
            to == MapSocketSide.Down && (from == MapSocketSide.Left || from == MapSocketSide.Right);
    }
}
