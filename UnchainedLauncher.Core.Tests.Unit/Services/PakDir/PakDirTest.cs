using JetBrains.Annotations;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Utilities;
using Xunit.Sdk;

namespace UnchainedLauncher.Core.Tests.Unit.Services.PakDir {
    [TestSubject(typeof(Core.Services.PakDir.PakDir))]
    public class PakDirTest {
        public const string BaseTestDir = "TestDirectories";

        public static readonly IPakDir.MakeFileWriter StaticMkWriter = (string path) => {
            Stream fileStream = new MemoryStream();
            StreamWriter streamWriter = new StreamWriter(fileStream);
            var fileContents = $"dummy contents for '{path}'";
            streamWriter.Write(fileContents);
            return new FileWriter(path, fileStream, fileContents.Length);
        };

        /// <summary>
        /// this should not have multiple versions of the same mod. Make a different dataset for that
        /// </summary>
        private static readonly List<ReleaseCoordinates> DummyMods = new List<ReleaseCoordinates> {
            new("Unchained-Mods", "Mod1", "1.0.0"),
            new("Unchained-Mods", "Mod2", "1.0.0"),
            new("Chained-Mods", "Mod1", "1.0.0"),
            new("Chained-Mods", "Mod2", "1.0.0"),
        };

        // for theories
        public static List<object[]> DummyModsParam = DummyMods.Select(x => new object[] { x }).ToList();

        private static void VerifyHasInstalledPak(Core.Services.PakDir.PakDir pd, string dir, ReleaseCoordinates coords) {
            var pakLocation = pd.GetInstalledPakFile(coords);
            Assert.True(pakLocation.IsSome);
            Assert.Contains(pakLocation.ValueUnsafe(), Directory.EnumerateFiles(dir));
        }

        private static void InstallDummy(Core.Services.PakDir.PakDir pd, ReleaseCoordinates coords) {
            var result = pd
                .Install(coords, StaticMkWriter, coords.ModuleName + ".pak", Option<IProgress<double>>.None);
            Task.Run(async () => await result).Result.IfLeft(e => throw e);
        }

        private static void PrepareTestDir(string dir) {
            Directory.CreateDirectory(dir);
            Directory.Delete(dir, true);
        }

        private static void PrepareTestDirWithSig(string dir) {
            PrepareTestDir(dir);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pakchunk0-WindowsNoEditor.sig"), "test sig file");
        }

        [Fact]
        public void DeleteOrphanedSigs() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var orphanedSigName = Path.Join(thisTestPath, "orphaned.sig");
            File.WriteAllText(orphanedSigName, "orphaned sig file");

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            Assert.True(File.Exists(orphanedSigName));
            pd.DeleteOrphanedSigs();
            Assert.False(File.Exists(orphanedSigName));
        }

        private void throwErrors(IEnumerable<Error> errors) {
            throw Error.Many(errors.ToSeq());
        }

        [Fact]
        public void CleanUpPakDir() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            new List<string> {
                "pakchunk0-WindowsNoEditor.pak",
                "file1.pak",
                "file2.pak",
                "file1.sig",
                "file2.sig",
                "file3.sig"
            }.Map(p => Path.Join(thisTestPath, p))
                .ToList().ForEach(p => File.WriteAllText(p, "mysterious contents"));

            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            InstallDummy(pd, m1);
            pd.CleanUpDir().IfLeft(throwErrors);
            Assert.Empty(
                Directory.EnumerateFiles(thisTestPath)
                    .Map(Path.GetFileName)
                    .Filter(p => p != "pakchunk0-WindowsNoEditor.pak")
                    .Filter(p => p != "pakchunk0-WindowsNoEditor.sig")
                );
        }

        [Fact]
        public void UnmanagedHandling() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            var mysteriousFiles = new List<string> {
                "mysterious-file1.pak",
                "mysterious-file2.pak",
                "mysterious-file3.pak"
            }.Map(fn => Path.Combine(thisTestPath, fn)).ToList();

            mysteriousFiles.ForEach(p => File.WriteAllText(p, "myserious pak contents"));
            // this name should collide with an unmanaged file and be handled properly
            var m1 = new ReleaseCoordinates("Unchained-Mods", "mysterious-file1", "1.0.0");
            InstallDummy(pd, m1);
            pd.Sign(m1).IfLeft(e => throw e);
            Assert.NotEqual(mysteriousFiles[0], pd.GetInstalledPakFile(m1));

            // should add sig files for them
            pd.SignUnmanaged().IfLeft(throwErrors);
            mysteriousFiles
                .Map(p => Path.ChangeExtension(p, ".sig"))
                .ToList().ForEach(p => Assert.True(File.Exists(p)));

            // should delete sig files for them
            pd.UnSignUnmanaged().IfLeft(throwErrors);
            mysteriousFiles
                .Map(p => Path.ChangeExtension(p, ".sig"))
                .ToList().ForEach(p => Assert.False(File.Exists(p)));

            // should delete unmanaged pak and sig files
            pd.SignUnmanaged().IfLeft(throwErrors);
            pd.DeleteUnmanaged().IfLeft(throwErrors);
            mysteriousFiles.ForEach(p => Assert.False(File.Exists(p)));
            mysteriousFiles
                .Map(p => Path.ChangeExtension(p, ".sig"))
                .ToList().ForEach(p => Assert.False(File.Exists(p)));

            // none of the above should affect managed files
            Assert.True(pd.IsSigned(m1));
            Assert.True(File.Exists(pd.GetInstalledPakFile(m1).ValueUnsafe()));
            Assert.True(File.Exists(Path.ChangeExtension(pd.GetInstalledPakFile(m1).ValueUnsafe(), ".sig")));
        }

        [Fact]
        public void SignOnly() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDirWithSig(thisTestPath);

            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var m2 = new ReleaseCoordinates("Chained-Mods", "Mod1", "1.0.0");
            var m3 = new ReleaseCoordinates("Chained-Mods", "Mod2", "1.0.0");

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            InstallDummy(pd, m1);
            InstallDummy(pd, m2);
            InstallDummy(pd, m3);

            pd.SignOnly(new List<ReleaseCoordinates> { m1, m3 }).IfLeft(throwErrors);

            var m2InstallPath = pd.GetInstalledPakFile(m2).ValueUnsafe();

            Assert.True(pd.IsSigned(m1));
            Assert.False(pd.IsSigned(m2));
            Assert.False(File.Exists(Path.ChangeExtension(m2InstallPath, ".sig")));
            Assert.True(pd.IsSigned(m3));

        }

        [Fact]
        public void InstallOnly() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDir(thisTestPath);

            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var m2 = new ReleaseCoordinates("Chained-Mods", "Mod1", "1.0.0");
            var m3 = new ReleaseCoordinates("Chained-Mods", "Mod2", "1.0.0");

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            InstallDummy(pd, m1);
            InstallDummy(pd, m2);
            InstallDummy(pd, m3);

            var m2InstallPath = pd.GetInstalledPakFile(m2).ValueUnsafe();

            pd.InstallOnly(new List<(ReleaseCoordinates, IPakDir.MakeFileWriter, string)> {
                (m1, StaticMkWriter, m1.ModuleName+".pak"),
                (m3, StaticMkWriter, m1.ModuleName+".pak")
            }, Option<AccumulatedMemoryProgress>.None).IfLeft(throwErrors);

            Assert.Contains(m1, pd.GetInstalledReleases());
            Assert.DoesNotContain(m2, pd.GetInstalledReleases());
            Assert.False(File.Exists(m2InstallPath));
            Assert.Contains(m3, pd.GetInstalledReleases());
        }


        [Fact]
        public void WillBumpName() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDir(thisTestPath);

            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var m2 = new ReleaseCoordinates("Chained-Mods", "Mod1", "1.0.0");

            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            InstallDummy(pd, m1);
            InstallDummy(pd, m2);

            Assert.Contains(m1, pd.GetInstalledReleases());
            Assert.Contains(m2, pd.GetInstalledReleases());
            Assert.NotEqual(pd.GetInstalledPakFile(m1), pd.GetInstalledPakFile(m2));
        }

        [Theory]
        [MemberData(nameof(DummyModsParam))]
        public void CanSignPak(ReleaseCoordinates coords) {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            string subPath = Path.Combine(thisTestPath, coords.Org, coords.ModuleName);
            PrepareTestDirWithSig(subPath);
            var pd = new Core.Services.PakDir.PakDir(subPath);
            InstallDummy(pd, coords);
            pd.Sign(coords).IfLeft(e => throw e);
            var signedName = pd.GetInstalledPakFile(coords)
                .Map(p => Path.ChangeExtension(p, ".sig"));
            Assert.True(signedName.IsSome);
            Assert.True(File.Exists(signedName.ValueUnsafe()));
            Assert.Contains(coords, pd.GetSignedReleases());
            pd.Unsign(coords).IfLeft(e => throw e);
            Assert.False(File.Exists(signedName.ValueUnsafe()));
        }

        /// <summary>
        /// Makes sure uninstall logic works properly. This means not uninstalling a release if it's not
        /// a matching verison, and uninstalling a release using only a ModIdentifier
        /// </summary>
        /// <param name="coords"></param>
        /// <exception cref="Error"></exception>
        [Theory]
        [MemberData(nameof(DummyModsParam))]
        public void CanUninstall(ReleaseCoordinates coords) {
            var modIdent = new ModIdentifier(coords.Org, coords.ModuleName);
            var otherVersion = new ReleaseCoordinates(coords.Org, coords.ModuleName, "999.0.0");
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            string subPath = Path.Combine(thisTestPath, coords.Org, coords.ModuleName);
            PrepareTestDir(subPath);
            var pd = new Core.Services.PakDir.PakDir(subPath);
            InstallDummy(pd, coords);
            pd.Uninstall(otherVersion).IfLeft(e => throw e); // This should do nothing
            VerifyHasInstalledPak(pd, subPath, coords);
            pd.Uninstall(modIdent).IfLeft(e => throw e);
            Assert.Throws<TrueException>(() => VerifyHasInstalledPak(pd, subPath, coords));
        }

        [Theory]
        [MemberData(nameof(DummyModsParam))]
        public void CanInstallPak(ReleaseCoordinates coords) {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            string subPath = Path.Combine(thisTestPath, coords.Org, coords.ModuleName);
            PrepareTestDir(subPath);
            var pd = new Core.Services.PakDir.PakDir(subPath);
            InstallDummy(pd, coords);
            VerifyHasInstalledPak(pd, subPath, coords);
            pd.Uninstall(coords).IfLeft(e => throw e);
            Assert.Throws<TrueException>(() => VerifyHasInstalledPak(pd, subPath, coords));
        }

        [Fact]
        public void WillOverrideIfPresent() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDir(thisTestPath);
            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            var m1 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "1.0.0");
            var m2 = new ReleaseCoordinates("Unchained-Mods", "Mod1", "2.0.0");
            InstallDummy(pd, m1);
            InstallDummy(pd, m2);
            Assert.DoesNotContain(m1, pd.GetInstalledReleases());
            Assert.Contains(m2, pd.GetInstalledReleases());
        }

        [Fact]
        public void CanSaveAndLoadMetadata() {
            var thisTestPath = Path.Join(BaseTestDir, System.Reflection.MethodBase.GetCurrentMethod()?.Name);
            PrepareTestDir(thisTestPath);
            var pd = new Core.Services.PakDir.PakDir(thisTestPath);
            DummyMods.ForEach(m => InstallDummy(pd, m));
            var pd2 = new Core.Services.PakDir.PakDir(thisTestPath);
            Assert.Equal(pd.GetInstalledReleases(), pd2.GetInstalledReleases());
        }
    }
}