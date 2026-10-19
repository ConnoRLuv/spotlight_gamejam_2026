using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SpotlightGameJam.UI
{
    /// <summary>
    /// 挂在 StartupRoot 上。每个图层单独配置入场、鼠标视差和自动漂浮。
    /// DOTween 只修改入场偏移，LateUpdate 统一合成最终位置，避免位置互相覆盖。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    [AddComponentMenu("UI/Startup Page Tween")]
    public sealed class StartupPageTween : MonoBehaviour
    {
        public enum LogoPlaybackStart
        {
            [InspectorName("与 Logo 入场同时")] WithLogoEntrance,
            [InspectorName("Logo 入场完成后")] AfterLogoEntrance,
            [InspectorName("整个页面入场完成后")] AfterPageEntrance,
            [InspectorName("手动调用播放")] Manual
        }

        [Serializable]
        public sealed class Layer
        {
            [InspectorName("图层名称")] public string label;
            [InspectorName("动画对象")] public RectTransform target;

            [Header("入场")]
            [InspectorName("延迟（秒）"), Min(0f)] public float delay;
            [InspectorName("渐显时间（秒）"), Min(0f)] public float fadeDuration = 0.6f;
            [InspectorName("位移时间（秒）"), Min(0f)] public float moveDuration = 0.8f;
            [InspectorName("起始上下偏移（UI 单位）")]
            [Tooltip("相对编辑器中摆好的位置：负数从下方进入，正数从上方进入，0 不位移。")]
            public float startOffsetY = -40f;
            [InspectorName("起始透明度"), Range(0f, 1f)] public float startAlpha;
            [InspectorName("显示透明度"), Range(0f, 1f)] public float shownAlpha = 1f;
            [InspectorName("渐显缓动")] public Ease fadeEase = Ease.OutQuad;
            [InspectorName("位移缓动")] public Ease moveEase = Ease.OutCubic;

            [Header("待机：鼠标视差")]
            [InspectorName("鼠标最大偏移（X / Y）")]
            [Tooltip("鼠标位于屏幕边缘时的偏移量，单位为本图层父级的 UI 单位。负数反向；全为 0 则关闭此图层的鼠标视差。")]
            public Vector2 mouseOffset = new Vector2(10f, 5f);

            [Header("待机：自动漂浮（鼠标不动也播放）")]
            [InspectorName("漂浮幅度（X / Y）")]
            [Tooltip("全为 0 则关闭漂浮。可用不同的幅度、周期和相位，让前后景产生不同步的运动。")]
            public Vector2 floatAmplitude = Vector2.zero;
            [InspectorName("漂浮周期（秒）"), Min(0.1f)] public float floatPeriod = 6f;
            [InspectorName("漂浮相位（度）"), Range(0f, 360f)] public float floatPhase;

            [NonSerialized] internal CanvasGroup group;
            [NonSerialized] internal Vector2 restPosition;
            [NonSerialized] internal float restAlpha;
            [NonSerialized] internal float entranceY;
            [NonSerialized] internal bool valid;
        }

        [Header("图层：只填叶子节点，不重复填它们的父物体")]
        [SerializeField] private Layer[] layers = new Layer[0];

        [Header("播放")]
        [InspectorName("每次启用时重播")]
        [SerializeField] private bool playOnEnable = true;
        [InspectorName("忽略游戏时间缩放")]
        [SerializeField] private bool ignoreTimeScale = true;
        [InspectorName("入场期间禁止页面交互")]
        [SerializeField] private bool lockInteractionDuringIntro = true;

        [Header("待机全局设置")]
        [InspectorName("启用待机运动")]
        [SerializeField] private bool idleMotion = true;
        [InspectorName("启用鼠标视差")]
        [SerializeField] private bool mouseParallax = true;
        [InspectorName("启用自动漂浮")]
        [SerializeField] private bool autoFloat = true;
        [InspectorName("鼠标跟随平滑时间（秒）"), Min(0f)]
        [SerializeField] private float mouseSmoothTime = 0.2f;
        [InspectorName("入场后待机渐入时间（秒）"), Min(0f)]
        [Tooltip("所有图层入场及自动播放的 Logo 首轮结束后，统一渐入鼠标视差和漂浮。0 表示立即开启。")]
        [SerializeField] private float idleBlendDuration = 0.4f;

        [Header("Logo 序列帧")]
        [InspectorName("启用 Logo 序列帧")]
        [SerializeField] private bool enableLogoSequence = true;
        [InspectorName("Logo 图片组件")]
        [Tooltip("留空时自动查找 StartupRoot/Logo 上的 Image，不改变其尺寸。")]
        [SerializeField] private Image logoImage;
        [InspectorName("序列帧（按编号播放）")]
        [SerializeField] private Sprite[] logoFrames = new Sprite[0];
        [InspectorName("播放帧率"), Min(0.1f)]
        [Tooltip("例如 24 表示每秒播放 24 张。修改后重播生效。")]
        [SerializeField] private float logoFrameRate = 24f;
        [InspectorName("播放时机")]
        [SerializeField] private LogoPlaybackStart logoPlaybackStart = LogoPlaybackStart.WithLogoEntrance;
        [InspectorName("额外播放延迟（秒）"), Min(0f)]
        [SerializeField] private float logoPlaybackDelay;
        [InspectorName("循环播放")]
        [Tooltip("关闭时播放一次并停在最后一帧；开启时从第一帧循环。统一待机只等待首轮结束。")]
        [SerializeField] private bool loopLogoSequence;
#if UNITY_EDITOR
        [InspectorName("编辑器中自动填充空的帧列表")]
        [Tooltip("编译后自动按文件末尾编号填入帧列表。不会覆盖已有列表，不修改素材或导入设置。请在 Unity 中保存场景以保留引用并用于打包。")]
        [SerializeField] private bool autoFillLogoFrames = true;
        [InspectorName("序列帧素材目录")]
        [SerializeField] private string logoFramesFolder = "Assets/Art/Startup/logo序列帧";
#endif

        [Header("事件（可选）")]
        [SerializeField] private UnityEvent onIntroStarted = new UnityEvent();
        [SerializeField] private UnityEvent onIntroFinished = new UnityEvent();
        [SerializeField] private UnityEvent onLogoSequenceFinished = new UnityEvent();

        private CanvasGroup pageGroup;
        private Sequence intro;
        private bool initialized;
        private bool interactionLocked;
        private bool previousInteractable;
        private bool previousBlocksRaycasts;
        private Vector2 smoothedMouse;
        private double idleTime;
        private float idleBlend;
        private Tween logoPlayback;
        private float logoFrameCursor;
        private Image cachedLogoImage;
        private Sprite originalLogoSprite;
        private Sprite originalLogoOverride;

        private void Reset()
        {
            ConfigureFromHierarchy();
#if UNITY_EDITOR
            FillLogoFramesInEditor(false);
#endif
        }
        private void Awake() { Initialize(); }

        private void OnEnable()
        {
            Initialize();
            if (playOnEnable) Replay();
            else ShowImmediately();
        }

        private void Initialize()
        {
            if (initialized) return;
            pageGroup = GetComponent<CanvasGroup>();
            var seen = new HashSet<RectTransform>();
            if (layers == null) layers = new Layer[0];
            foreach (Layer layer in layers)
            {
                if (layer == null) continue;
                layer.valid = false;
                if (!layer.target) continue;
                if (layer.target == transform || !layer.target.IsChildOf(transform) ||
                    !seen.Add(layer.target))
                {
                    Debug.LogWarning("StartupPageTween：忽略根节点、根节点外的对象或重复对象：" + layer.label, this);
                    continue;
                }
                // 父子对象同时入表会叠加位移/透明度，阻止这种误配置。
                bool overlaps = false;
                foreach (Layer other in layers)
                {
                    if (other == null || other == layer || !other.target) continue;
                    if (other.target != layer.target && layer.target.IsChildOf(other.target))
                        overlaps = true;
                }
                if (overlaps)
                {
                    Debug.LogWarning("StartupPageTween：父子节点同时入表，请只保留叶子节点；已忽略：" + layer.label, this);
                    continue;
                }
                layer.group = layer.target.GetComponent<CanvasGroup>();
                if (!layer.group) layer.group = layer.target.gameObject.AddComponent<CanvasGroup>();
                layer.restPosition = layer.target.anchoredPosition;
                layer.restAlpha = layer.group.alpha;
                layer.valid = true;
            }
            ResolveLogoImage();
            initialized = true;
        }

        /// <summary>运行中重播；也可从组件右上角菜单调用。不会激活已关闭的页面。</summary>
        [ContextMenu("运行中：重播入场")]
        public void Replay()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            Initialize();
            StopIntro();
            StopLogoSequence();
            UnlockInteraction();
            smoothedMouse = Vector2.zero;
            idleTime = 0d;
            idleBlend = 0f;
            float totalTime = 0f;
            foreach (Layer layer in layers)
            {
                if (!IsValid(layer)) continue;
                layer.entranceY = layer.startOffsetY;
                layer.group.alpha = layer.startAlpha;
                layer.target.anchoredPosition = layer.restPosition + Vector2.up * layer.entranceY;
                totalTime = Mathf.Max(totalTime, Mathf.Max(0f, layer.delay) +
                    Mathf.Max(0f, Mathf.Max(layer.fadeDuration, layer.moveDuration)));
            }
            float idleStartTime = GetIdleStartTime(totalTime);
            LockInteraction();
            // 事件在序列启动后触发，事件监听器可以安全地关闭页面或再次重播。
            if (idleStartTime <= 0f && idleBlendDuration <= 0f)
            {
                ShowImmediately();
                ScheduleLogoSequence(totalTime);
                onIntroStarted.Invoke();
                if (isActiveAndEnabled) onIntroFinished.Invoke();
                return;
            }

            Sequence sequence = DOTween.Sequence().Pause();
            sequence.SetUpdate(ignoreTimeScale);
            foreach (Layer layer in layers)
            {
                if (!IsValid(layer)) continue;
                Layer item = layer;
                float delay = Mathf.Max(0f, item.delay);
                float fadeTime = Mathf.Max(0f, item.fadeDuration);
                float moveTime = Mathf.Max(0f, item.moveDuration);
                sequence.Insert(delay, DOTween.To(() => item.group.alpha,
                    value => { if (item.group) item.group.alpha = value; },
                    item.shownAlpha, fadeTime).SetEase(item.fadeEase));
                sequence.Insert(delay, DOTween.To(() => item.entranceY,
                    value => item.entranceY = value, 0f, moveTime).SetEase(item.moveEase));
            }
            // 各层只播放入场；等待整页入场和自动 Logo 首轮都结束后统一启动待机。
            sequence.Insert(idleStartTime, DOTween.To(() => idleBlend, value => idleBlend = value,
                1f, Mathf.Max(0f, idleBlendDuration)).SetEase(Ease.OutSine));
            intro = sequence;
            sequence.OnComplete(() =>
            {
                intro = null;
                ApplyPositions();
                UnlockInteraction();
                onIntroFinished.Invoke();
            });
            sequence.Play();
            ScheduleLogoSequence(totalTime);
            onIntroStarted.Invoke();
        }

        [ContextMenu("运行中：跳过入场并显示")]
        public void ShowImmediately()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            Initialize();
            StopIntro();
            StopLogoSequence();
            ShowLogoFrame(true);
            idleBlend = 1f;
            idleTime = 0d;
            smoothedMouse = Vector2.zero;
            foreach (Layer layer in layers)
            {
                if (!IsValid(layer)) continue;
                layer.entranceY = 0f;
                layer.group.alpha = layer.shownAlpha;
            }
            ApplyPositions();
            UnlockInteraction();
        }

        public void SetIdleMotion(bool value) { idleMotion = value; }

        private void ResolveLogoImage()
        {
            if (!logoImage)
            {
                Transform logo = transform.Find("Logo");
                if (logo) logoImage = logo.GetComponent<Image>();
            }
            if (!logoImage || cachedLogoImage == logoImage) return;
            cachedLogoImage = logoImage;
            originalLogoSprite = logoImage.sprite;
            originalLogoOverride = logoImage.overrideSprite != logoImage.sprite ? logoImage.overrideSprite : null;
        }

        private void ScheduleLogoSequence(float pageEntranceDuration)
        {
            if (!enableLogoSequence) return;
            ResolveLogoImage();
            // 等待播放时显示第一帧，防止先显示静态 Logo 再跳回序列帧起点。
            ShowLogoFrame(false);
            if (logoPlaybackStart == LogoPlaybackStart.Manual) return;

            StartLogoSequence(GetLogoStartTime(pageEntranceDuration));
        }

        private float GetLogoStartTime(float pageEntranceDuration)
        {
            float startTime = 0f;
            if (logoPlaybackStart == LogoPlaybackStart.AfterPageEntrance)
                startTime = pageEntranceDuration;
            else if (logoImage)
            {
                foreach (Layer layer in layers)
                {
                    if (!IsValid(layer) || !logoImage.transform.IsChildOf(layer.target)) continue;
                    startTime = Mathf.Max(0f, layer.delay);
                    if (logoPlaybackStart == LogoPlaybackStart.AfterLogoEntrance)
                        startTime += Mathf.Max(0f, Mathf.Max(layer.fadeDuration, layer.moveDuration));
                    break;
                }
            }
            return startTime + Mathf.Max(0f, logoPlaybackDelay);
        }

        private float GetIdleStartTime(float pageEntranceDuration)
        {
            // 手动播放不属于本次自动入场；缺少图片/帧时也无需等待 Logo。
            if (!enableLogoSequence || logoPlaybackStart == LogoPlaybackStart.Manual ||
                !logoImage || logoFrames == null) return pageEntranceDuration;

            int frameCount = 0;
            foreach (Sprite frame in logoFrames)
                if (frame) frameCount++;
            if (frameCount == 0) return pageEntranceDuration;

            float firstLoopEnd = GetLogoStartTime(pageEntranceDuration) +
                frameCount / Mathf.Max(0.1f, logoFrameRate);
            return Mathf.Max(pageEntranceDuration, firstLoopEnd);
        }

        /// <summary>独立重播 Logo 序列帧，不重置页面入场和待机运动。</summary>
        [ContextMenu("运行中：重播 Logo 序列帧")]
        public void PlayLogoSequence()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            Initialize();
            StartLogoSequence(Mathf.Max(0f, logoPlaybackDelay));
        }

        private void StartLogoSequence(float delay)
        {
            StopLogoSequence();
            if (!enableLogoSequence) return;
            ResolveLogoImage();
            if (!logoImage || logoFrames == null) return;
            // 使用快照，避免播放过程中在面板修改列表引发越界或空引用。
            Sprite[] frames = Array.FindAll(logoFrames, frame => frame != null);
            if (frames.Length == 0)
            {
                Debug.LogWarning("StartupPageTween：Logo 序列帧列表为空，请使用组件菜单按编号填充。", this);
                return;
            }
            logoImage.overrideSprite = null;
            logoImage.sprite = frames[0];
            logoFrameCursor = 0f;
            float duration = frames.Length / Mathf.Max(0.1f, logoFrameRate);
            logoPlayback = DOTween.To(() => logoFrameCursor, value =>
            {
                logoFrameCursor = value;
                if (logoImage)
                    logoImage.sprite = frames[Mathf.Clamp(Mathf.FloorToInt(value), 0, frames.Length - 1)];
            }, frames.Length, duration)
                .SetEase(Ease.Linear)
                .SetDelay(Mathf.Max(0f, delay))
                .SetUpdate(ignoreTimeScale)
                .SetLoops(loopLogoSequence ? -1 : 1, LoopType.Restart)
                .OnComplete(() =>
                {
                    logoPlayback = null;
                    if (logoImage) logoImage.sprite = frames[frames.Length - 1];
                    onLogoSequenceFinished.Invoke();
                });
        }

        /// <summary>停止播放，保留当前帧。关闭页面时会另外恢复原始 Logo。</summary>
        public void StopLogoSequence()
        {
            logoPlayback?.Kill();
            logoPlayback = null;
        }

        private void ShowLogoFrame(bool last)
        {
            if (!enableLogoSequence || !logoImage || logoFrames == null) return;
            for (int step = 0; step < logoFrames.Length; step++)
            {
                int index = last ? logoFrames.Length - 1 - step : step;
                if (!logoFrames[index]) continue;
                logoImage.overrideSprite = null;
                logoImage.sprite = logoFrames[index];
                return;
            }
        }

        private void LateUpdate()
        {
            float dt = ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            if (idleBlend > 0f)
            {
                idleTime += dt;
                Vector2 desired = idleMotion && mouseParallax ? ReadMouse() : Vector2.zero;
                float weight = mouseSmoothTime <= 0f ? 1f : 1f - Mathf.Exp(-dt / mouseSmoothTime);
                smoothedMouse = Vector2.Lerp(smoothedMouse, desired, weight);
            }
            else
            {
                // 入场阶段不累计鼠标跟随和漂浮时间，避免待机启动时突然跳位。
                smoothedMouse = Vector2.zero;
                idleTime = 0d;
            }
            ApplyPositions();
        }

        private void ApplyPositions()
        {
            foreach (Layer layer in layers)
            {
                if (!IsValid(layer)) continue;
                Vector2 idleOffset = Vector2.zero;
                if (idleMotion && idleBlend > 0f)
                {
                    idleOffset = Vector2.Scale(smoothedMouse, layer.mouseOffset);
                    if (autoFloat)
                    {
                        double phase = idleTime / Mathf.Max(0.1f, layer.floatPeriod) * Math.PI * 2d +
                            layer.floatPhase * Mathf.Deg2Rad;
                        idleOffset += new Vector2((float)Math.Sin(phase),
                            (float)Math.Sin(phase + Math.PI * 0.5d)) * layer.floatAmplitude;
                    }
                }
                layer.target.anchoredPosition = layer.restPosition +
                    Vector2.up * layer.entranceY + idleOffset * idleBlend;
            }
        }

        private static Vector2 ReadMouse()
        {
            if (!Application.isFocused || Screen.width <= 0 || Screen.height <= 0) return Vector2.zero;
#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            Vector2 position;
#endif
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return Vector2.zero;
            position = Mouse.current.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.mousePresent) return Vector2.zero;
            position = Input.mousePosition;
#else
            return Vector2.zero;
#endif
#if ENABLE_INPUT_SYSTEM || ENABLE_LEGACY_INPUT_MANAGER
            // 鼠标离开游戏画面时平滑回到中心。
            if (position.x < 0f || position.y < 0f ||
                position.x > Screen.width || position.y > Screen.height) return Vector2.zero;
            return new Vector2(position.x / Screen.width * 2f - 1f,
                position.y / Screen.height * 2f - 1f);
#endif
        }

        private static bool IsValid(Layer layer)
        {
            return layer != null && layer.valid && layer.target && layer.group;
        }

        private void LockInteraction()
        {
            if (!lockInteractionDuringIntro || !pageGroup) return;
            previousInteractable = pageGroup.interactable;
            previousBlocksRaycasts = pageGroup.blocksRaycasts;
            pageGroup.interactable = false;
            // 保持阻挡射线，避免入场期间点击穿透到后面的页面。
            pageGroup.blocksRaycasts = true;
            interactionLocked = true;
        }

        private void UnlockInteraction()
        {
            if (!interactionLocked) return;
            if (pageGroup)
            {
                pageGroup.interactable = previousInteractable;
                pageGroup.blocksRaycasts = previousBlocksRaycasts;
            }
            interactionLocked = false;
        }

        private void StopIntro()
        {
            // 只清理本组件的序列，不使用 target.DOKill，以免影响按钮等其他动画。
            intro?.Kill();
            intro = null;
        }

        private void OnDisable()
        {
            StopIntro();
            StopLogoSequence();
            idleBlend = 0f;
            smoothedMouse = Vector2.zero;
            idleTime = 0d;
            if (cachedLogoImage)
            {
                cachedLogoImage.sprite = originalLogoSprite;
                cachedLogoImage.overrideSprite = originalLogoOverride;
            }
            UnlockInteraction();
            if (!initialized) return;
            foreach (Layer layer in layers)
            {
                if (!IsValid(layer)) continue;
                layer.target.anchoredPosition = layer.restPosition;
                layer.group.alpha = layer.restAlpha;
                layer.entranceY = 0f;
            }
        }

        private void OnDestroy()
        {
            StopIntro();
            StopLogoSequence();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall -= AutoFillLogoInEditor;
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // OnValidate 可能由加载线程调用，将 AssetDatabase 访问推迟到编辑器主线程。
            UnityEditor.EditorApplication.delayCall -= AutoFillLogoInEditor;
            UnityEditor.EditorApplication.delayCall += AutoFillLogoInEditor;
        }

        private void AutoFillLogoInEditor()
        {
            if (!this || Application.isPlaying || !autoFillLogoFrames) return;
            FillLogoFramesInEditor(false);
        }

        [ContextMenu("编辑时：按编号重新填充 Logo 序列帧（覆盖帧列表）")]
        private void ReloadLogoFramesInEditor()
        {
            if (!Application.isPlaying) FillLogoFramesInEditor(true);
        }

        private void FillLogoFramesInEditor(bool replace)
        {
            bool needsFrames = replace || logoFrames == null || logoFrames.Length == 0;
            Image foundImage = logoImage;
            if (!foundImage)
            {
                Transform logo = transform.Find("Logo");
                if (logo) foundImage = logo.GetComponent<Image>();
            }

            var foundFrames = new List<Sprite>();
            if (needsFrames && UnityEditor.AssetDatabase.IsValidFolder(logoFramesFolder))
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { logoFramesFolder });
                foreach (string guid in guids)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    foreach (UnityEngine.Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        Sprite frame = asset as Sprite;
                        if (frame) foundFrames.Add(frame);
                    }
                }
                foundFrames.Sort((left, right) =>
                {
                    int result = CompareNumberedFrameNames(left.name, right.name);
                    return result != 0 ? result : StringComparer.Ordinal.Compare(
                        UnityEditor.AssetDatabase.GetAssetPath(left), UnityEditor.AssetDatabase.GetAssetPath(right));
                });
            }
            if (replace && foundFrames.Count == 0)
                Debug.LogWarning("StartupPageTween：素材目录中未找到 Sprite，保留现有帧列表。", this);
            if (foundImage == logoImage && foundFrames.Count == 0) return;
            UnityEditor.Undo.RecordObject(this, "Fill Logo Sequence Frames");
            logoImage = foundImage;
            if (foundFrames.Count > 0) logoFrames = foundFrames.ToArray();
            // 只修改内存中的组件引用，不调用 SaveScene/SaveAssets 或更改导入设置。
            // 在 Unity 保存场景后，Sprite 引用才会写入场景并被打包。
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static int CompareNumberedFrameNames(string left, string right)
        {
            ulong leftNumber = GetTrailingFrameNumber(left);
            ulong rightNumber = GetTrailingFrameNumber(right);
            int result = leftNumber.CompareTo(rightNumber);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
        }

        private static ulong GetTrailingFrameNumber(string value)
        {
            int start = value.Length;
            while (start > 0 && value[start - 1] >= '0' && value[start - 1] <= '9') start--;
            ulong result;
            return start < value.Length && ulong.TryParse(value.Substring(start), out result)
                ? result : ulong.MaxValue;
        }

        [ContextMenu("编辑时：按 StartupRoot 层级填充默认图层（覆盖列表）")]
        private void ConfigureFromHierarchy()
        {
            if (Application.isPlaying) return;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Configure Startup Tween Layers");
#endif
            string[] paths = { "Background/Background_1", "Background/Background_2",
                "Background/Background_3", "Logo", "Character/Character_1",
                "Character/Character_2", "StartButton" };
            layers = new Layer[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                bool background = i < 3;
                layers[i] = new Layer
                {
                    label = paths[i],
                    target = transform.Find(paths[i]) as RectTransform,
                    delay = background ? i * 0.1f : (i == 6 ? 0.8f : 0.3f + (i - 3) * 0.12f),
                    startOffsetY = background ? 0f : -40f,
                    mouseOffset = i == 6 ? Vector2.zero : new Vector2(4f + i * 3f, 2f + i),
                    floatAmplitude = i == 6 || i == 3 ? Vector2.zero : new Vector2(2f, 3f),
                    floatPhase = i * 45f,
                    floatPeriod = 6f + i * 0.5f
                };
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
