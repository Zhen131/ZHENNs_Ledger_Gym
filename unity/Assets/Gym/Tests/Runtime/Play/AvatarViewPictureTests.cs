using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gym.Runtime.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.Runtime.Play
{
    /// <summary>
    /// 头像框里画出来的图：用临时文件夹，看框里实际画出来的宽高比。第一次画字时 PlayFont 会打一行普通日志
    /// （说用了哪个系统字体），所以这里只数警告，不用 LogAssert.NoUnexpectedReceived。
    /// </summary>
    public class AvatarViewPictureTests
    {
        string folder;
        GameObject holder;
        readonly List<string> warnings = new List<string>();

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-avatar-view-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            holder = new GameObject("Avatar under test");
            warnings.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Object.DestroyImmediate(holder);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(message);
        }

        void PutPng(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            File.WriteAllBytes(Path.Combine(folder, "avatar.png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        [Test]
        public void ANonSquarePicture_IsDrawnWithTheSameAspectRatioAsTheFile()
        {
            PutPng(120, 40);
            var avatar = holder.AddComponent<AvatarView>();
            avatar.UsePictureFrom(folder);
            Assert.IsFalse(avatar.Picture.IsPlaceholder);
            Assert.AreEqual(3f, avatar.PictureSize.x / avatar.PictureSize.y, 1e-4);
            Assert.AreEqual(avatar.Picture.FitInto(avatar.PictureSize.x).x, avatar.PictureSize.x, 1e-5, "the long side fills the box");
            CollectionAssert.IsEmpty(warnings);
        }

        [Test]
        public void NoPicture_DrawsTheSquarePlaceholder()
        {
            var avatar = holder.AddComponent<AvatarView>();
            avatar.UsePictureFrom(folder);
            Assert.IsTrue(avatar.Picture.IsPlaceholder);
            Assert.AreEqual(1f, avatar.PictureSize.x / avatar.PictureSize.y, 1e-5);
            CollectionAssert.IsEmpty(warnings);
        }

        [Test]
        public void ABrokenPicture_DrawsThePlaceholderWithAWarningAndNoException()
        {
            File.WriteAllText(Path.Combine(folder, "avatar.jpg"), "this is not a jpeg");
            var avatar = holder.AddComponent<AvatarView>();
            Assert.DoesNotThrow(() => avatar.UsePictureFrom(folder));
            LogAssert.Expect(LogType.Warning, new Regex(@"avatar\.jpg' is not a PNG or JPEG image"));
            Assert.IsTrue(avatar.Picture.IsPlaceholder);
            Assert.AreEqual(1, warnings.Count, string.Join("\n", warnings));
        }

        [Test]
        public void ChangingTheFolder_SwapsThePictureStraightAway()
        {
            var avatar = holder.AddComponent<AvatarView>();
            avatar.UsePictureFrom(folder);
            Assert.IsTrue(avatar.Picture.IsPlaceholder);
            PutPng(50, 100);
            avatar.UsePictureFrom(folder);
            Assert.IsFalse(avatar.Picture.IsPlaceholder);
            Assert.AreEqual(0.5f, avatar.PictureSize.x / avatar.PictureSize.y, 1e-4);
        }
    }
}
