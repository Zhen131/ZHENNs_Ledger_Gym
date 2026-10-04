using Gym.Runtime.Play;
using UnityEngine;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战画面上的一段提示（两种语言，跟着语言切换换字）：没选模型时在图表正中说去点哪个菜单；走到段尾时在
    /// 图表上方左边说按 R 重来。同一时间最多一段。字用 TextMesh 画，命令行截图截得到。
    /// </summary>
    public class WatchNotice : MonoBehaviour
    {
        const float NoModelTextSize = 0.26f;
        const float EndTextSize = 0.24f;

        [SerializeField] PlayLanguageSwitch language;

        ShapeLayer panel;
        TextMesh noModelText;
        TextMesh endText;

        public PlayLanguageSwitch Language
        {
            get => language;
            set => language = value;
        }

        /// <summary>正在显示哪一段；什么都不显示时为 null。</summary>
        public WatchNoticeKind? Shown { get; private set; }

        /// <summary>正在显示的那段字；什么都不显示时为 null。</summary>
        public TextMesh ShownText => Shown == WatchNoticeKind.NoModel ? noModelText : Shown == WatchNoticeKind.EndOfSegment ? endText : null;

        PlayLanguage CurrentLanguage => language != null ? language.Current : PlayLanguage.Chinese;

        void OnEnable()
        {
            if (language != null) language.Changed += OnLanguageChanged;
        }

        void OnDisable()
        {
            if (language != null) language.Changed -= OnLanguageChanged;
        }

        void OnLanguageChanged(PlayLanguage _) => Draw();

        public void Show(WatchNoticeKind kind)
        {
            BuildIfNeeded();
            Shown = kind;
            Draw();
        }

        public void Hide()
        {
            Shown = null;
            Draw();
        }

        void Draw()
        {
            if (panel == null) return;
            bool noModel = Shown == WatchNoticeKind.NoModel;
            bool end = Shown == WatchNoticeKind.EndOfSegment;
            panel.Begin();
            if (noModel)
            {
                panel.Shapes.AddRect(PlayLayout.WatchNoModelNotice, PlayPalette.PanelBackground);
                panel.Shapes.AddFrame(PlayLayout.WatchNoModelNotice, 0.03f, PlayPalette.AvatarFrame);
            }
            panel.End();
            noModelText.gameObject.SetActive(noModel);
            endText.gameObject.SetActive(end);
            noModelText.text = PlayText.Get(PlayTextKey.WatchNoModel, CurrentLanguage);
            endText.text = PlayText.Get(PlayTextKey.WatchEnd, CurrentLanguage);
        }

        void BuildIfNeeded()
        {
            if (panel != null) return;
            panel = new ShapeLayer(transform, "Watch notice panel", 0f, 12);
            Rect noModel = PlayLayout.WatchNoModelNotice;
            noModelText = WorldText.Create(transform, "Watch notice: no model", noModel.center, NoModelTextSize,
                TextAnchor.MiddleCenter, PlayPalette.Value);
            noModelText.lineSpacing = 1.3f;
            Rect end = PlayLayout.WatchEndNotice;
            endText = WorldText.Create(transform, "Watch notice: end of segment", new Vector2(end.xMin + PlayLayout.Padding, end.center.y),
                EndTextSize, TextAnchor.MiddleLeft, PlayPalette.AvatarFrame);
        }
    }
}
