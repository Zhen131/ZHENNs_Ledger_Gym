using System;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 画面文字的语言：左上角一个能用鼠标点的按钮，按 L 效果相同。切换后发出
    /// <see cref="Changed"/>，各个面板马上换字。默认语言在 Inspector 里改。
    /// </summary>
    public class PlayLanguageSwitch : MonoBehaviour
    {
        const float LabelSize = 0.2f;

        [Tooltip("Language of the Play scene's text when the scene starts.")]
        [SerializeField] PlayLanguage defaultLanguage = PlayLanguage.Chinese;

        PlayLanguage current;
        bool started;
        ShapeLayer background;
        TextMesh label;

        public event Action<PlayLanguage> Changed;

        public PlayLanguage DefaultLanguage
        {
            get => defaultLanguage;
            set => defaultLanguage = value;
        }

        public PlayLanguage Current
        {
            get
            {
                StartIfNeeded();
                return current;
            }
        }

        public Rect ButtonArea => PlayLayout.LanguageButton;

        /// <summary>按钮上的字；第一次 <see cref="Redraw"/> 之前为 null。</summary>
        public TextMesh Label => label;

        void Awake() => Redraw();

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.L)) Toggle();
            if (Input.GetMouseButtonDown(0)) Click(Input.mousePosition);
        }

        /// <summary>中文和英文互换。按 L 和点按钮都走这里。</summary>
        public void Toggle() => Set(Current == PlayLanguage.Chinese ? PlayLanguage.English : PlayLanguage.Chinese);

        public void Set(PlayLanguage language)
        {
            StartIfNeeded();
            current = language;
            Redraw();
            Changed?.Invoke(current);
        }

        /// <summary>鼠标在屏幕坐标 <paramref name="screenPoint"/> 点了一下：点在按钮上就切换，返回点中没有。</summary>
        public bool Click(Vector2 screenPoint)
        {
            Camera view = Camera.main;
            if (view == null) return false;
            Vector3 world = view.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, -view.transform.position.z));
            if (!ButtonArea.Contains(new Vector2(world.x, world.y))) return false;
            Toggle();
            return true;
        }

        /// <summary>按当前语言画按钮；第一次调用时建出底色和字。</summary>
        public void Redraw()
        {
            StartIfNeeded();
            if (label == null)
            {
                background = new ShapeLayer(transform, "Language button background", 0f, 10);
                label = WorldText.Create(transform, "Language button label",
                    new Vector2(ButtonArea.xMin + PlayLayout.Padding, ButtonArea.center.y), LabelSize, TextAnchor.MiddleLeft, PlayPalette.Value);
                background.Begin();
                background.Shapes.AddRect(ButtonArea, PlayPalette.ButtonBackground);
                background.Shapes.AddFrame(ButtonArea, 0.02f, PlayPalette.PanelBorder);
                background.End();
            }
            label.text = PlayText.Get(PlayTextKey.LanguageButton, current);
        }

        void StartIfNeeded()
        {
            if (started) return;
            started = true;
            current = defaultLanguage;
        }
    }
}
