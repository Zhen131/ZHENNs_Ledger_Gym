using System;
using System.IO;
using System.Text.RegularExpressions;
using Gym.Runtime.Play;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.Runtime.Play
{
    /// <summary>贴图文件夹里放不同的东西时，头像框用哪张图。用临时文件夹，不碰真的贴图文件夹。</summary>
    public class AvatarPictureLoaderTests
    {
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-avatar-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        static byte[] Picture(int width, int height, Color color, bool jpeg)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] bytes = jpeg ? texture.EncodeToJPG() : texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return bytes;
        }

        void Put(string fileName, byte[] bytes) => File.WriteAllBytes(Path.Combine(folder, fileName), bytes);

        [Test]
        public void AGoodPng_IsUsed()
        {
            Put("avatar.png", Picture(40, 40, Color.red, false));
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            Assert.IsFalse(picture.IsPlaceholder);
            Assert.AreEqual(Path.Combine(folder, "avatar.png"), picture.SourceFile);
            Assert.AreEqual(40, picture.Width);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AGoodJpg_IsUsed()
        {
            Put("avatar.jpg", Picture(30, 30, Color.blue, true));
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            Assert.IsFalse(picture.IsPlaceholder);
            Assert.AreEqual(Path.Combine(folder, "avatar.jpg"), picture.SourceFile);
            Assert.AreEqual(30, picture.Width);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AJpegWithTheLongExtension_IsUsed()
        {
            Put("avatar.jpeg", Picture(20, 20, Color.green, true));
            Assert.AreEqual(Path.Combine(folder, "avatar.jpeg"), AvatarPictureLoader.Load(folder).SourceFile);
        }

        [Test]
        public void PngAndJpgTogether_ThePngWins()
        {
            Put("avatar.jpg", Picture(30, 30, Color.blue, true));
            Put("avatar.png", Picture(40, 40, Color.red, false));
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            Assert.AreEqual(Path.Combine(folder, "avatar.png"), picture.SourceFile);
            Assert.AreEqual(40, picture.Width);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ABrokenFile_FallsBackToThePlaceholderWithOneWarning()
        {
            Put("avatar.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            AvatarPicture picture = null;
            Assert.DoesNotThrow(() => picture = AvatarPictureLoader.Load(folder));
            LogAssert.Expect(LogType.Warning, new Regex(@"avatar picture '.*avatar\.png' is not a PNG or JPEG image"));
            Assert.IsTrue(picture.IsPlaceholder);
            Assert.AreEqual(AvatarPlaceholder.Size, picture.Width);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ABrokenPngNextToAGoodJpg_SkipsThePngWithAWarningAndUsesTheJpg()
        {
            Put("avatar.png", new byte[0]);
            Put("avatar.jpg", Picture(30, 30, Color.blue, true));
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            LogAssert.Expect(LogType.Warning, new Regex(@"avatar\.png' is not a PNG or JPEG image"));
            Assert.AreEqual(Path.Combine(folder, "avatar.jpg"), picture.SourceFile);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnEmptyFolder_UsesThePlaceholderWithoutWarnings()
        {
            File.WriteAllText(Path.Combine(folder, "README.md"), "not a picture");
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            Assert.IsTrue(picture.IsPlaceholder);
            Assert.IsNull(picture.SourceFile);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AMissingFolder_UsesThePlaceholderWithoutWarnings()
        {
            Directory.Delete(folder, true);
            Assert.IsTrue(AvatarPictureLoader.Load(folder).IsPlaceholder);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ANonSquarePicture_KeepsItsAspectRatioWhenFittedIntoTheFrame()
        {
            Put("avatar.png", Picture(64, 32, Color.yellow, false));
            AvatarPicture picture = AvatarPictureLoader.Load(folder);
            Vector2 shown = picture.FitInto(0.8f);
            Assert.AreEqual(0.8f, shown.x, 1e-5, "the long side fills the box");
            Assert.AreEqual(2f, shown.x / shown.y, 1e-5, "same proportions as the file");

            Put("avatar.png", Picture(30, 90, Color.yellow, false));
            shown = AvatarPictureLoader.Load(folder).FitInto(0.8f);
            Assert.AreEqual(0.8f, shown.y, 1e-5);
            Assert.AreEqual(1f / 3f, shown.x / shown.y, 1e-5);
        }

        [Test]
        public void ThePlaceholder_IsASquareFigureOnADarkBackground()
        {
            AvatarPicture picture = AvatarPicture.Placeholder();
            Assert.IsTrue(picture.IsPlaceholder);
            Assert.AreEqual(AvatarPlaceholder.Size, picture.Width);
            Assert.AreEqual(AvatarPlaceholder.Size, picture.Height);
            Color32 corner = picture.Texture.GetPixel(0, AvatarPlaceholder.Size - 1);
            Color32 head = picture.Texture.GetPixel(AvatarPlaceholder.Size / 2, 40);
            Assert.AreEqual((Color32)PlayPalette.AvatarPlaceholderBackground, corner, "top-left corner is background");
            Assert.AreEqual((Color32)PlayPalette.AvatarPlaceholderFigure, head, "the head is the figure colour");
        }
    }
}
