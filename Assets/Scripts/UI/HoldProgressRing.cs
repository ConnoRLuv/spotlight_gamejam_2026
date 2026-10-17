using UnityEngine;
using UnityEngine.UI;

namespace SpotlightGameJam.UI
{
    /// <summary>只读长按圆环：直接绘制圆环底轨和顺时针进度，不依赖额外图片或拦截输入。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HoldProgressRing : MaskableGraphic
    {
        private float progress;
        public float Progress => progress;

        /// <summary>更新零到一的进度，只在值改变时重建网格。</summary>
        public void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(progress, value)) return;
            progress = value; SetVerticesDirty();
        }

        /// <summary>绘制完整暗色底轨，再从十二点方向覆盖青色进度。</summary>
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            AddArc(mesh, 1f, new Color(.07f, .12f, .18f, .8f));
            AddArc(mesh, progress, color);
        }

        /// <summary>以四边形分段生成等宽圆弧，适配当前矩形且保持圆形比例。</summary>
        private void AddArc(VertexHelper mesh, float amount, Color tint)
        {
            if (amount <= 0) return;
            var rect = GetPixelAdjustedRect();
            float outer = Mathf.Min(rect.width, rect.height) * .5f;
            float inner = outer * .78f;
            int segments = Mathf.CeilToInt(64 * amount);
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * .5f - Mathf.PI * 2 * amount * i / segments;
                float b = Mathf.PI * .5f - Mathf.PI * 2 * amount * (i + 1) / segments;
                var first = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var last = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int index = mesh.currentVertCount;
                mesh.AddVert(rect.center + first * inner, tint, Vector2.zero);
                mesh.AddVert(rect.center + first * outer, tint, Vector2.zero);
                mesh.AddVert(rect.center + last * outer, tint, Vector2.zero);
                mesh.AddVert(rect.center + last * inner, tint, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
