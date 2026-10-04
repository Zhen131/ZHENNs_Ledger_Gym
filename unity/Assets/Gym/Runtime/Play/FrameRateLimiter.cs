using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 只挂在 Play scene 上：启用时关掉 vSync，把 <c>Application.targetFrameRate</c> 设成
    /// <see cref="TargetFrameRate"/>；停用或卸载时恢复成启用前的两个值。
    /// 画面很简单，不限帧的话 editor 会一直全速重画，Mac 会发烫。
    /// 只在运行时设，不改 ProjectSettings；Training、Eval scene 不挂它，它们的速度和这里无关。
    /// </summary>
    public class FrameRateLimiter : MonoBehaviour
    {
        public const int DefaultFrameRate = 30;

        [Tooltip("Frames per second while the Play scene runs; set 60 for smoother motion.")]
        [SerializeField] int targetFrameRate = DefaultFrameRate;

        int previousFrameRate;
        int previousVSyncCount;
        bool applied;

        public int TargetFrameRate
        {
            get => targetFrameRate;
            set
            {
                targetFrameRate = value;
                if (applied) Application.targetFrameRate = value;
            }
        }

        void OnEnable()
        {
            previousFrameRate = Application.targetFrameRate;
            previousVSyncCount = QualitySettings.vSyncCount;
            // vSync 开着时 targetFrameRate 不起作用，所以两样一起设。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
            applied = true;
        }

        void OnDisable()
        {
            if (!applied) return;
            QualitySettings.vSyncCount = previousVSyncCount;
            Application.targetFrameRate = previousFrameRate;
            applied = false;
        }
    }
}
