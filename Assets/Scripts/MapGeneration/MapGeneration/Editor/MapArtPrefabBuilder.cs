using System;
using System.Collections.Generic;
using System.IO;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RoguelikeMap.EditorTools
{
    // 把 Assets/地图/地图 里的图片变成节点与路线预制体，并把它们挂到 MapGenerationConfig 资产上。
    //
    // 菜单：菜单/地图/生成节点与路线预制体
    // 命令行：Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod RoguelikeMap.EditorTools.MapArtPrefabBuilder.BuildAll
    //
    // 这个工具是幂等的：重复跑只会覆盖它生成的那几个预制体，不会碰你手改过的其它东西。
    // 换一张图片再跑一次即可，端点（轴心点与 _targetEnd）会按新的透明边重新算。
    public static class MapArtPrefabBuilder
    {
        const string ArtFolder = "Assets/地图/地图";
        const string PrefabFolder = ArtFolder + "/Prefabs";
        const string NodePrefabFolder = PrefabFolder + "/Nodes";
        const string RoutePrefabFolder = PrefabFolder + "/Routes";
        const string ConfigAssetPath = "Assets/MapGenerationConfig.asset";
        const string ScenePath = "Assets/Scenes/SampleScene.unity";

        const float NodeSize = 130f;
        const float RouteThickness = 14f;

        // 路线的美术是按"竖长的格子"画的（斜线的弦大约是 1 : 2.3），
        // 行距取列距的 2.3 倍左右时，斜着的那四张图不用旋转就能正好对上两个节点。
        const float ColumnSpacing = 150f;
        const float RowSpacing = 350f;

        // 出厂的旧值。场景里还是这些值（也就是没人手调过）时才覆盖，免得抹掉手调的手感。
        static readonly Vector2[] OldNodeSizes = { new Vector2(96f, 72f), new Vector2(110f, 110f) };
        static readonly float[] OldColumnSpacings = { 100f, 140f };
        static readonly float[] OldRowSpacings = { 90f, 120f };
        static readonly float[] OldEdgeThicknesses = { 5f, 14f };

        // 一种房间类型用哪张图。FileName 为 null 表示这类没有美术，靠代码生成的圆底。
        sealed class NodeArtEntry
        {
            public string TypeKey;
            public string FileName;
        }

        sealed class RouteArtEntry
        {
            public MapRouteDirection Direction;
            public string FileName;

            // 起点端是不是在图片的下边（"上"字辈的图都是从下往上画的）
            public bool SourceIsBottom;

            // 竖直的那两张两端都在图片正中的上下边，没有可推的端点，直接用默认值
            public bool DetectEndpoints = true;
        }

        static readonly NodeArtEntry[] NodeArt =
        {
            new NodeArtEntry { TypeKey = NodeTypeKeys.Start, FileName = "起点.png" },
            new NodeArtEntry { TypeKey = NodeTypeKeys.Monster, FileName = "怪物（普通）.png" },
            new NodeArtEntry { TypeKey = NodeTypeKeys.Elite, FileName = "怪物（精英）.png" },
            new NodeArtEntry { TypeKey = NodeTypeKeys.Event, FileName = "事件.png" },
            new NodeArtEntry { TypeKey = NodeTypeKeys.Shop, FileName = "商店.png" },
            new NodeArtEntry { TypeKey = NodeTypeKeys.Boss, FileName = "怪物（BOSS）.png" }
        };

        static readonly RouteArtEntry[] RouteArt =
        {
            new RouteArtEntry { Direction = MapRouteDirection.Up, FileName = "分支（上）.png", SourceIsBottom = true, DetectEndpoints = false },
            new RouteArtEntry { Direction = MapRouteDirection.UpRight, FileName = "分支（上右）.png", SourceIsBottom = true },
            new RouteArtEntry { Direction = MapRouteDirection.UpLeft, FileName = "分支（上左）.png", SourceIsBottom = true },
            new RouteArtEntry { Direction = MapRouteDirection.Down, FileName = "分支（下）.png", SourceIsBottom = false, DetectEndpoints = false },
            new RouteArtEntry { Direction = MapRouteDirection.DownRight, FileName = "分支（下右）.png", SourceIsBottom = false },
            new RouteArtEntry { Direction = MapRouteDirection.DownLeft, FileName = "分支（下左）.png", SourceIsBottom = false }
        };

        // ------------------------------------------------------------------
        // 入口
        // ------------------------------------------------------------------

        [MenuItem("Tools/地图/生成节点与路线预制体", false, 10)]
        public static void BuildAllMenu()
        {
            BuildAll();
        }

        [MenuItem("Tools/地图/检查节点与路线预制体", false, 11)]
        public static void VerifyMenu()
        {
            int failures = Verify(LogDetails: true);
            EditorUtility.DisplayDialog("地图美术检查",
                failures == 0 ? "全部通过，节点与路线的端点都对得上。" : "有 " + failures + " 项有问题，看 Console。",
                "好");
        }

        // 批处理入口：生成 + 自检。
        public static void BuildAll()
        {
            BuildInternal(false);
        }

        // 打开工程时自动跑的那一次：不碰用户当前打开的场景，也不弹窗。
        public static void BuildFromAutoWire()
        {
            BuildInternal(true);
        }

        // 美术还没挂上时才需要跑工具。自动挂载就是靠这个判断决定要不要动手。
        public static bool NeedsArtWiring()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>(ConfigAssetPath);
            if (asset == null) return true;

            if (asset.BackgroundSprite == null || asset.BackdropSprite == null) return true;

            for (int i = 0; i < NodeArt.Length; i++)
            {
                NodeTypeVisual visual = asset.GetVisual(NodeArt[i].TypeKey);
                if (visual == null || visual.CustomPrefab == null) return true;
                if (NodeArt[i].FileName != null && visual.Icon == null) return true;
            }

            for (int i = 0; i < RouteArt.Length; i++)
            {
                MapRouteVisual route = asset.GetRoute(RouteArt[i].Direction);
                if (route == null || route.Prefab == null || route.Sprite == null) return true;
            }

            return false;
        }

        static void BuildInternal(bool autoWire)
        {
            try
            {
                MapGenerationConfigAsset asset = LoadOrCreateConfigAsset();
                if (asset == null)
                {
                    Debug.LogError("[MapArtPrefabBuilder] 找不到也建不出 " + ConfigAssetPath);
                    Exit(1);
                    return;
                }

                EnsureFolder(NodePrefabFolder);
                EnsureFolder(RoutePrefabFolder);

                // 先保证图片是按"单张图"导入的，不然下面 LoadSprite 会拿到空
                EnsureArtImportSettings();

                NodeTypeCatalog catalog = NodeTypeCatalog.CreateDefault();

                for (int i = 0; i < NodeArt.Length; i++)
                {
                    NodeArtEntry entry = NodeArt[i];
                    Sprite icon = entry.FileName == null ? null : LoadSprite(entry.FileName);
                    if (entry.FileName != null && icon == null)
                        Debug.LogWarning("[MapArtPrefabBuilder] 读不到图片，这一类会退化成纯色圆底：" + entry.FileName);

                    GameObject prefab = BuildNodePrefab(entry.TypeKey, icon);

                    NodeTypeVisual visual = GetOrCreateVisual(asset, entry.TypeKey);
                    visual.DisplayName = catalog.GetDisplayName(entry.TypeKey);   // 顺手把"对话"改成"事件"
                    visual.Icon = icon;
                    visual.CustomPrefab = prefab;
                    LogSpriteIdentity("节点 " + entry.TypeKey, icon);
                    EditorUtility.SetDirty(asset);
                }

                asset.EnsureRouteSlots();

                for (int i = 0; i < RouteArt.Length; i++)
                {
                    RouteArtEntry entry = RouteArt[i];
                    Sprite sprite = LoadSprite(entry.FileName);
                    if (sprite == null)
                    {
                        Debug.LogWarning("[MapArtPrefabBuilder] 读不到路线图片：" + entry.FileName);
                        continue;
                    }

                    // 默认按"竖直连线"摆，端点推不出来时就用它
                    Vector2 sourceEnd = new Vector2(0.5f, entry.SourceIsBottom ? 0f : 1f);
                    Vector2 targetEnd = new Vector2(0.5f, entry.SourceIsBottom ? 1f : 0f);

                    if (entry.DetectEndpoints &&
                        !TryDetectEndpoints(entry.FileName, entry.SourceIsBottom, out sourceEnd, out targetEnd))
                    {
                        sourceEnd = new Vector2(0.5f, entry.SourceIsBottom ? 0f : 1f);
                        targetEnd = new Vector2(0.5f, entry.SourceIsBottom ? 1f : 0f);
                        Debug.LogWarning("[MapArtPrefabBuilder] " + entry.FileName +
                                         " 的端点没能按透明边推出来，先按竖直连线的默认值放。" +
                                         "想更贴合美术的话，在配置资产的这条路线里手填起点/终点端。");
                    }

                    GameObject prefab = BuildRoutePrefab(entry.Direction, sprite, sourceEnd, targetEnd);

                    MapRouteVisual route = asset.GetRoute(entry.Direction);
                    route.Sprite = sprite;
                    route.Prefab = prefab;
                    route.SourceEnd = sourceEnd;
                    route.TargetEnd = targetEnd;
                    LogSpriteIdentity("路线 " + entry.Direction, sprite);
                    EditorUtility.SetDirty(asset);
                }

                ApplySceneStyle(autoWire);

                // 背景插画（固定不动）与底纹（跟着地图滚）也一起挂上去
                Sprite background = LoadSprite("背景插画.JPG");
                Sprite backdrop = LoadSprite("底纹.png");
                asset.BackgroundSprite = background;
                asset.BackdropSprite = backdrop;
                if (backdrop != null) EnsureRepeatWrap("底纹.png");
                LogSpriteIdentity("背景插画", background);
                LogSpriteIdentity("底纹", backdrop);
                if (background == null)
                    Debug.LogWarning("[MapArtPrefabBuilder] 没找到 背景插画.JPG，背景会留空。");

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[MapArtPrefabBuilder] 预制体与配置资产已更新完毕。" +
                          "节点：" + NodeArt.Length + " 个，路线：" + RouteArt.Length + " 个。");

                int failures = Verify(LogDetails: true);
                if (!autoWire) Exit(failures == 0 ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[MapArtPrefabBuilder] 生成失败：" + e);
                Exit(1);
            }
        }

        // ------------------------------------------------------------------
        // 节点预制体
        // ------------------------------------------------------------------

        // 节点预制体 = 一张底图 + 一个（默认关着的）房间名。
        // 高亮圈不放进预制体，由 MapNodeView 运行时用代码生成的圆环补上，少一个要维护的占位资源。
        static GameObject BuildNodePrefab(string typeKey, Sprite icon)
        {
            var root = new GameObject("Node_" + typeKey,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(NodeSize, NodeSize);

            var badge = root.GetComponent<Image>();
            badge.sprite = icon;
            badge.color = Color.white;
            badge.preserveAspect = true;
            badge.raycastTarget = true;

            // 房间名文字。默认关着，MapGraphView 需要的时候会打开
            Text label = CreateChildLabel(root.transform);

            var view = root.AddComponent<MapNodeView>();
            SetObjectField(view, "_badge", badge);
            SetObjectField(view, "_label", label);

            return SavePrefab(root, NodePrefabFolder + "/Node_" + typeKey + ".prefab");
        }

        static Text CreateChildLabel(Transform parent)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            var text = go.GetComponent<Text>();
            text.font = MapGraphView.ResolveBuiltinFont();
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.12f, 0.12f, 0.12f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            go.SetActive(false);
            return text;
        }

        // ------------------------------------------------------------------
        // 路线预制体
        // ------------------------------------------------------------------

        static GameObject BuildRoutePrefab(
            MapRouteDirection direction, Sprite sprite, Vector2 sourceEnd, Vector2 targetEnd)
        {
            var root = new GameObject("Route_" + direction,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = sprite.rect.size;

            // 关键约定：轴心点放在线段的起点端，摆放时轴心点直接对齐父节点
            rect.pivot = new Vector2(sourceEnd.x, sourceEnd.y);

            var image = root.GetComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = false;   // 线段的长宽由 RectTransform 决定
            image.raycastTarget = false;

            var view = root.AddComponent<MapEdgeView>();
            SetObjectField(view, "_image", image);
            SetVector2Field(view, "_targetEnd", targetEnd);

            return SavePrefab(root, RoutePrefabFolder + "/Route_" + direction + ".prefab");
        }

        // 从图片的透明边推出线段两个端点的位置。
        //
        // 做法：沿四条边找 alpha 不为空的一段段"接触区"，把挨在一起的接触区（同一条线段的端头）
        // 归成一组，每组的重心就是一个端点。手绘线段的端头通常正好顶到图片边缘，所以这样很稳。
        static bool TryDetectEndpoints(string fileName, bool sourceIsBottom, out Vector2 sourceEnd, out Vector2 targetEnd)
        {
            sourceEnd = new Vector2(0.5f, sourceIsBottom ? 0f : 1f);
            targetEnd = new Vector2(0.5f, sourceIsBottom ? 1f : 0f);

            string path = ArtFolder + "/" + fileName;
            string full = Path.GetFullPath(path);
            if (!File.Exists(full)) return false;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(full))) return false;

                int w = texture.width;
                int h = texture.height;
                Color32[] pixels = texture.GetPixels32();
                const int cutoff = 24;

                // 四条边上的接触点（归一化坐标，y 轴向上）
                var contacts = new List<Vector2>(8);

                CollectBorderContacts(pixels, w, h, cutoff, contacts);
                if (contacts.Count < 2) return false;

                List<Vector2> clusters = CornerClusters(contacts, 0.35f);
                if (clusters.Count != 2) return false;

                Vector2 a = clusters[0];
                Vector2 b = clusters[1];

                // 两端挨太近说明认错了，宁可用默认值
                if ((a - b).magnitude < 0.25f) return false;

                bool aIsBottom = a.y < b.y;
                Vector2 bottom = aIsBottom ? a : b;
                Vector2 top = aIsBottom ? b : a;

                sourceEnd = sourceIsBottom ? bottom : top;
                targetEnd = sourceIsBottom ? top : bottom;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MapArtPrefabBuilder] 读像素失败（" + fileName + "）：" + e.Message);
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static void CollectBorderContacts(Color32[] pixels, int w, int h, int cutoff, List<Vector2> contacts)
        {
            // 下边与上边：横向扫，一段连续的非透明像素取中心
            CollectRuns(w, i => pixels[i].a > cutoff, (start, end) =>
                contacts.Add(new Vector2(((start + end) * 0.5f) / (w - 1), 0f)));

            CollectRuns(w, i => pixels[(h - 1) * w + i].a > cutoff, (start, end) =>
                contacts.Add(new Vector2(((start + end) * 0.5f) / (w - 1), 1f)));

            // 左边与右边：纵向扫
            CollectRuns(h, i => pixels[i * w].a > cutoff, (start, end) =>
                contacts.Add(new Vector2(0f, ((start + end) * 0.5f) / (h - 1))));

            CollectRuns(h, i => pixels[i * w + (w - 1)].a > cutoff, (start, end) =>
                contacts.Add(new Vector2(1f, ((start + end) * 0.5f) / (h - 1))));
        }

        static void CollectRuns(int length, Func<int, bool> opaque, Action<int, int> onRun)
        {
            int start = -1;
            for (int i = 0; i < length; i++)
            {
                if (opaque(i))
                {
                    if (start < 0) start = i;
                }
                else if (start >= 0)
                {
                    onRun(start, i - 1);
                    start = -1;
                }
            }
            if (start >= 0) onRun(start, length - 1);
        }

        // 按"离哪个角最近"把接触点归堆：一条线段的端头一定是顶在某个角上的，
        // 所以同一个角上的接触点（例如上边的右半段 + 右边的上半段）属于同一个端点。
        // 落在边的正中间、哪个角都不挨着的接触点直接丢掉 —— 那是竖直线段两侧的描边，不是端点。
        static List<Vector2> CornerClusters(List<Vector2> points, float threshold)
        {
            var sums = new Vector2[4];
            var counts = new int[4];
            float far = 1f - threshold;

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                int corner = -1;

                if (p.y <= threshold)
                {
                    if (p.x <= threshold) corner = 0;          // 左下
                    else if (p.x >= far) corner = 1;           // 右下
                }
                else if (p.y >= far)
                {
                    if (p.x <= threshold) corner = 2;          // 左上
                    else if (p.x >= far) corner = 3;           // 右上
                }

                if (corner < 0) continue;
                sums[corner] += p;
                counts[corner]++;
            }

            var clusters = new List<Vector2>(2);
            for (int c = 0; c < 4; c++)
                if (counts[c] > 0) clusters.Add(sums[c] / counts[c]);

            return clusters;
        }

        // ------------------------------------------------------------------
        // 场景与资产
        // ------------------------------------------------------------------

        static void ApplySceneStyle()
        {
            ApplySceneStyle(false);
        }

        // autoWire 为真时只处理"已经打开着"的那个场景，不会把用户正在编辑的场景换掉。
        static void ApplySceneStyle(bool onlyIfOpen)
        {
            if (!File.Exists(Path.GetFullPath(ScenePath)))
            {
                Debug.LogWarning("[MapArtPrefabBuilder] 没找到 " + ScenePath + "，跳过场景里的间距设置。");
                return;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (onlyIfOpen && scene.path != ScenePath) return;   // 自动挂载时不动别的场景

            bool opened = scene.path != ScenePath;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            MapGraphView view = null;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length && view == null; i++)
                view = roots[i].GetComponentInChildren<MapGraphView>(true);

            if (view == null)
            {
                Debug.LogWarning("[MapArtPrefabBuilder] 场景里没有 MapGraphView，跳过外观设置。");
                if (opened) EditorSceneManager.SaveScene(scene);
                return;
            }

            var so = new SerializedObject(view);

            // 只在还是出厂默认值时才改，手调过的间距一律保留
            SetIfDefault(so.FindProperty("_style.ColumnSpacing"), OldColumnSpacings, ColumnSpacing);
            SetIfDefault(so.FindProperty("_style.RowSpacing"), OldRowSpacings, RowSpacing);
            SetIfDefault(so.FindProperty("_style.EdgeThickness"), OldEdgeThicknesses, RouteThickness);
            SetIfDefault(so.FindProperty("_nodeSize"), OldNodeSizes, new Vector2(NodeSize, NodeSize));

            // 长地图要能滚：关掉"缩到一屏"，底纹和节点才会跟着滚轮一起动
            SerializedProperty fit = so.FindProperty("_autoFitToViewport");
            if (fit != null && fit.boolValue)
            {
                fit.boolValue = false;
                Debug.Log("[MapArtPrefabBuilder] 已关掉自动缩放，改用滚动布局（滚轮上下看地图，底纹跟着一起滚）。");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void SetIfDefault(SerializedProperty property, float[] oldValues, float newValue)
        {
            if (property == null) return;
            for (int i = 0; i < oldValues.Length; i++)
            {
                if (!Mathf.Approximately(property.floatValue, oldValues[i])) continue;
                property.floatValue = newValue;
                return;
            }
        }

        static void SetIfDefault(SerializedProperty property, Vector2[] oldValues, Vector2 newValue)
        {
            if (property == null) return;
            for (int i = 0; i < oldValues.Length; i++)
            {
                if (Vector2.Distance(property.vector2Value, oldValues[i]) >= 0.01f) continue;
                property.vector2Value = newValue;
                return;
            }
        }

        // 所有用到的美术文件。校正导入设置、以及判断"有没有图"都靠这张表。
        static readonly string[] AllArtFiles =
        {
            "怪物（普通）.png", "怪物（精英）.png", "怪物（BOSS）.png", "商店.png", "事件.png",
            "分支（上）.png", "分支（上右）.png", "分支（上左）.png",
            "分支（下）.png", "分支（下右）.png", "分支（下左）.png",
            "起点.png", "背景插画.JPG", "底纹.png"
        };

        // 图片必须按"单张图（Sprite）"导入，工具才拿得到图去挂。
        // 从别的编辑器版本带过来的图片，导入设置可能没被正确应用（这时脚本里读到的图会是空引用，
        // 挂上去之后一进播放就刷错误），所以这里统一校正一遍；只有确实改了东西才重新导入。
        static void EnsureArtImportSettings()
        {
            int fixedCount = 0;

            for (int i = 0; i < AllArtFiles.Length; i++)
            {
                string fileName = AllArtFiles[i];
                string path = ArtFolder + "/" + fileName;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[MapArtPrefabBuilder] 找不到美术文件：" + path);
                    continue;
                }

                bool dirty = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    dirty = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    dirty = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    dirty = true;
                }

                bool isPng = fileName.EndsWith(".png");
                if (isPng && !importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }

                // 底纹要平铺，其余保持默认的钳制模式
                TextureWrapMode want = fileName == "底纹.png" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                if (importer.wrapMode != want)
                {
                    importer.wrapMode = want;
                    dirty = true;
                }

                if (!dirty) continue;

                importer.SaveAndReimport();
                fixedCount++;
                Debug.Log("[MapArtPrefabBuilder] 校正了导入设置：" + fileName);
            }

            if (fixedCount > 0) AssetDatabase.Refresh();
        }

        // 把图真正的 guid 和局部 ID 打出来。图挂歪的时候，这行日志是唯一能直接看出问题的线索。
        static void LogSpriteIdentity(string label, Sprite sprite)
        {
            if (sprite == null)
            {
                Debug.Log("[MapArtPrefabBuilder] " + label + "：没有图");
                return;
            }

            string guid;
            long localId;
            bool ok = AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out guid, out localId);
            Debug.Log("[MapArtPrefabBuilder] " + label + "：" + sprite.name +
                      "  guid=" + guid + "  局部 ID=" + localId + (ok ? "" : "（取不到）"));
        }

        // 底纹要平铺，贴图的循环方式（Wrap Mode）要设成 Repeat，不然接缝会被拉成一条线。
        static void EnsureRepeatWrap(string fileName)
        {
            string path = ArtFolder + "/" + fileName;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || importer.wrapMode == TextureWrapMode.Repeat) return;

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
            Debug.Log("[MapArtPrefabBuilder] 已把 " + fileName + " 的 Wrap Mode 改成 Repeat（平铺需要）。");
        }

        static MapGenerationConfigAsset LoadOrCreateConfigAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>(ConfigAssetPath);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<MapGenerationConfigAsset>();
            AssetDatabase.CreateAsset(asset, ConfigAssetPath);
            return asset;
        }

        static NodeTypeVisual GetOrCreateVisual(MapGenerationConfigAsset asset, string typeKey)
        {
            NodeTypeVisual visual = asset.GetVisual(typeKey);
            if (visual != null) return visual;

            var so = new SerializedObject(asset);
            SerializedProperty list = so.FindProperty("_visuals");
            list.arraySize++;
            SerializedProperty item = list.GetArrayElementAtIndex(list.arraySize - 1);
            item.FindPropertyRelative("TypeKey").stringValue = typeKey;
            item.FindPropertyRelative("DisplayName").stringValue = typeKey;
            item.FindPropertyRelative("Tags").ClearArray();
            item.FindPropertyRelative("Color").colorValue = Color.white;
            item.FindPropertyRelative("Icon").objectReferenceValue = null;
            item.FindPropertyRelative("OverlayIcon").objectReferenceValue = null;
            item.FindPropertyRelative("CustomPrefab").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            return asset.GetVisual(typeKey);
        }

        static Sprite LoadSprite(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder + "/" + fileName);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static GameObject SavePrefab(GameObject instance, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            UnityEngine.Object.DestroyImmediate(instance);
            return prefab;
        }

        static void SetObjectField(Component target, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[MapArtPrefabBuilder] " + target.GetType().Name + " 上没有字段 " + field);
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetVector2Field(Component target, string field, Vector2 value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning("[MapArtPrefabBuilder] " + target.GetType().Name + " 上没有字段 " + field);
                return;
            }
            property.vector2Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // 自检
        // ------------------------------------------------------------------

        // 把每个预制体实例化一次，按各种间距摆一遍，检查线段两端是不是正好落在两个节点上。
        // 这是纯几何检查，不需要开渲染。
        public static int Verify(bool LogDetails)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>(ConfigAssetPath);
            if (asset == null)
            {
                Debug.LogError("[MapArtPrefabBuilder] 没有 " + ConfigAssetPath + "，无法自检。");
                return 1;
            }

            int failures = 0;
            float maxError = 0f;
            var report = new List<string>();
            var root = new GameObject("MapArtVerifyRoot", typeof(RectTransform));

            try
            {
                NodeTypeCatalog catalog = NodeTypeCatalog.CreateDefault();
                List<string> keys = catalog.AllKeys();

                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i] == NodeTypeKeys.Unassigned) continue;

                    NodeTypeVisual visual = asset.GetVisual(keys[i]);
                    if (visual == null || visual.CustomPrefab == null)
                    {
                        report.Add("节点 " + keys[i] + "：没有预制体");
                        failures++;
                        continue;
                    }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(visual.CustomPrefab, root.transform);
                    var view = go.GetComponent<MapNodeView>();
                    var image = go.GetComponent<Image>();
                    if (view == null)
                    {
                        report.Add("节点 " + keys[i] + "：预制体上缺 MapNodeView");
                        failures++;
                    }
                    if (image == null || image.sprite == null)
                    {
                        report.Add("节点 " + keys[i] + "：底图没有 sprite");
                        failures++;
                    }
                    UnityEngine.Object.DestroyImmediate(go);
                }

                // 采样几种间距，覆盖同一列、斜上、斜下
                var samples = new List<Vector4>
                {
                    new Vector4(0f, 0f, 0f, RowSpacing),
                    new Vector4(0f, 0f, ColumnSpacing, RowSpacing),
                    new Vector4(0f, 0f, -ColumnSpacing, RowSpacing),
                    new Vector4(0f, 0f, 0f, -RowSpacing),
                    new Vector4(0f, 0f, ColumnSpacing, -RowSpacing),
                    new Vector4(0f, 0f, -ColumnSpacing, -RowSpacing)
                };

                for (int i = 0; i < RouteArt.Length; i++)
                {
                    RouteArtEntry entry = RouteArt[i];
                    MapRouteVisual route = asset.GetRoute(entry.Direction);
                    if (route == null || route.Prefab == null || route.Sprite == null)
                    {
                        report.Add("路线 " + entry.Direction + "：没有预制体或图片");
                        failures++;
                        continue;
                    }

                    Vector2 chord = Vector2.Scale(route.TargetEnd - route.SourceEnd, route.Sprite.rect.size);
                    string chordNote = "弦 (" + chord.x.ToString("F0") + ", " + chord.y.ToString("F0") + ")，朝向 " +
                                       (Mathf.Atan2(chord.y, chord.x) * Mathf.Rad2Deg).ToString("F1") + "°";

                    if (entry.SourceIsBottom && chord.y <= 0f)
                    {
                        report.Add("路线 " + entry.Direction + "：方向反了，" + chordNote);
                        failures++;
                    }
                    else if (!entry.SourceIsBottom && chord.y >= 0f)
                    {
                        report.Add("路线 " + entry.Direction + "：方向反了，" + chordNote);
                        failures++;
                    }

                    for (int s = 0; s < samples.Count; s++)
                    {
                        Vector4 sample = samples[s];
                        Vector2 from = new Vector2(sample.x, sample.y);
                        Vector2 to = new Vector2(sample.z, sample.w);

                        var go = (GameObject)PrefabUtility.InstantiatePrefab(route.Prefab, root.transform);
                        var rect = (RectTransform)go.transform;
                        var view = go.GetComponent<MapEdgeView>();

                        if (view != null)
                        {
                            for (int layout = 0; layout <= (int)MapRouteLayout.SimpleStretch; layout++)
                            {
                                view.Place(from, to, (MapRouteLayout)layout, RouteThickness);

                                Vector2 sourceWorld = rect.TransformPoint(Vector2.zero);
                                Vector2 targetLocal = Vector2.Scale(view.TargetEnd - rect.pivot, rect.rect.size);
                                Vector2 targetWorld = rect.TransformPoint(targetLocal);

                                float error = Mathf.Max((sourceWorld - from).magnitude, (targetWorld - to).magnitude);
                                bool stretch = layout == (int)MapRouteLayout.SimpleStretch;
                                if (stretch) continue;   // 直线模式本来就不连着两个端点，跳过

                                maxError = Mathf.Max(maxError, error);
                                if (error > 0.5f)
                                {
                                    report.Add("路线 " + entry.Direction + " 在 " + (MapRouteLayout)layout +
                                               " 下端点偏差 " + error.ToString("F2") + " 单位");
                                    failures++;
                                }
                            }
                        }
                        else
                        {
                            report.Add("路线 " + entry.Direction + "：预制体上缺 MapEdgeView");
                            failures++;
                        }

                        UnityEngine.Object.DestroyImmediate(go);
                    }

                    report.Add("路线 " + entry.Direction + "：" + chordNote +
                               "，起点端 " + Describe(route.SourceEnd) + "，终点端 " + Describe(route.TargetEnd));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MapArtPrefabBuilder] 自检异常：" + e);
                failures++;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            if (LogDetails)
            {
                Debug.Log("[MapArtPrefabBuilder] 自检结果：失败 " + failures + " 项，端点最大偏差 " +
                          maxError.ToString("F3") + " 单位\n" + string.Join("\n", report.ToArray()));
            }

            return failures;
        }

        static string Describe(Vector2 normalized)
        {
            return "(" + normalized.x.ToString("F2") + ", " + normalized.y.ToString("F2") + ")";
        }

        // 批处理里用它把退出码带出去；编辑器里点菜单时什么都不做。
        static void Exit(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }

    // 打开工程时自动把美术挂一次。
    //
    // 只做一件事：发现"美术还没挂上"就跑一遍生成工具，挂好之后再也不介入。
    // 这样从别处拿来的工程、或者美术被重新导入过之后，不需要记得点菜单。
    // 想完全手动控制的话，把 AutoWireOnLoad 改成 false 即可。
    [InitializeOnLoad]
    public static class MapArtAutoWire
    {
        // 想改成完全手动挂载，把这里改成 false（编辑器会记住，不用改别的地方）
        public static bool AutoWireOnLoad = true;

        const string DoneKey = "RoguelikeMap.ArtAutoWiredThisSession";

        static MapArtAutoWire()
        {
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (!AutoWireOnLoad) return;
            if (SessionState.GetBool(DoneKey, false)) return;   // 一次会话只试一次，别反复折腾资源
            SessionState.SetBool(DoneKey, true);

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!MapArtPrefabBuilder.NeedsArtWiring()) return;

            Debug.Log("[RoguelikeMap] 检测到地图美术还没挂上，自动生成一次。" +
                      "想手动来就是菜单里的 地图 → 生成节点与路线预制体。");
            MapArtPrefabBuilder.BuildFromAutoWire();
        }
    }
}
