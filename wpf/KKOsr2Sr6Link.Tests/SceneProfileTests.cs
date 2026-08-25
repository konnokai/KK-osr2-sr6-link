using System.IO;
using System.Linq;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class SceneProfileTests
{
    [Fact]
    public void ProfileKey_RejectsProtocolAndPathCharacters()
    {
        Assert.True(AxisInfo.IsValidProfileKey("shared-v1"));
        Assert.False(AxisInfo.IsValidProfileKey(""));
        Assert.False(AxisInfo.IsValidProfileKey(".."));
        Assert.False(AxisInfo.IsValidProfileKey("a|b"));
        Assert.False(AxisInfo.IsValidProfileKey("a:b"));
        Assert.False(AxisInfo.IsValidProfileKey("a/b"));
    }

    [Fact]
    public void Sr6Ref_RoundTripsOneProfileKey()
    {
        var path = Path.Combine(Path.GetTempPath(), "kk_" + Path.GetRandomFileName() + ".sr6ref");
        try
        {
            SceneFiles.SaveSr6Ref(path, "shared-v1");
            Assert.True(SceneFiles.TryLoadSr6Ref(path, out var key, out var exists, out _));
            Assert.True(exists);
            Assert.Equal("shared-v1", key);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CompleteActionSet_RequiresAllAxesCfgAndEqualNonZeroLengths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kkprofile_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var stem = Path.Combine(dir, "shared");
            SceneFiles.SaveActionSet(stem, Axes(3), Parts());
            Assert.True(SceneFiles.TryLoadActionSet(stem, out var loaded, out _));
            Assert.Equal(3, loaded.Axes[0].Values.Count);

            File.Delete(AxisInfo.Sr6ScriptPath(stem, Axis.R2));
            Assert.False(SceneFiles.TryLoadActionSet(stem, out _, out _));

            SceneFiles.SaveActionSet(stem, Axes(3), Parts());
            SceneFiles.SaveSr6Script(AxisInfo.Sr6ScriptPath(stem, Axis.R2),
                new AxisScript { Values = { 1, 2 }, MaxValue = 999, MinValue = 0 });
            Assert.False(SceneFiles.TryLoadActionSet(stem, out _, out _));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TwoScenesShareUpdates_AndForkStaysIndependent()
    {
        var root = Path.Combine(Path.GetTempPath(), "kkscenes_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var shared = AxisInfo.ProfileStem(root, "shared");
            var fork = AxisInfo.ProfileStem(root, "fork");
            SceneFiles.SaveActionSet(shared, Axes(2, 10), Parts());

            var sceneA = Path.Combine(root, "scene-a.txt");
            var sceneB = Path.Combine(root, "scene-b.txt");
            SceneFiles.SaveSr6Ref(AxisInfo.Sr6RefPath(sceneA), "shared");
            SceneFiles.SaveSr6Ref(AxisInfo.Sr6RefPath(sceneB), "shared");

            Assert.True(SceneFiles.TryLoadActionSet(shared, out var before, out _));
            Assert.Equal(before.Axes[0].Values, SceneFiles.TryLoadSr6Ref(AxisInfo.Sr6RefPath(sceneB), out var key, out _, out _)
                ? SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(AxisInfo.ProfileStem(root, key), Axis.L0))!.Values
                : null);

            SceneFiles.SaveActionSet(shared, Axes(2, 20), Parts());
            Assert.Equal(new[] { 20, 20 }, SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(shared, Axis.L0))!.Values);

            SceneFiles.SaveActionSet(fork, Axes(2, 30), Parts());
            Assert.Equal(new[] { 20, 20 }, SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(shared, Axis.L0))!.Values);
            Assert.Equal(new[] { 30, 30 }, SceneFiles.LoadSr6Script(AxisInfo.Sr6ScriptPath(fork, Axis.L0))!.Values);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProfileCanLoadWithoutRawTxt()
    {
        var root = Path.Combine(Path.GetTempPath(), "kkrawless_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var stem = AxisInfo.ProfileStem(root, "rawless");
            SceneFiles.SaveActionSet(stem, Axes(1, 500), Parts());
            Assert.False(File.Exists(Path.Combine(root, "missing.txt")));
            Assert.True(SceneFiles.TryLoadActionSet(stem, out var set, out _));
            Assert.Equal(500, set.Axes[(int)Axis.L0].Values.Single());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ProfileAssetsUseProfileKeyAndCopyOptionalFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kkassets_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            Assert.EndsWith(Path.Combine("_profiles", "demo.txt"), AxisInfo.ProfileRawPath(root, "demo"));
            Assert.EndsWith(Path.Combine("_profiles", "demo.png"), AxisInfo.ProfilePreviewPath(root, "demo"));

            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, "raw");
            var destination = AxisInfo.ProfileRawPath(root, "demo");
            SceneFiles.CopyFileIfExists(source, destination);
            Assert.Equal("raw", File.ReadAllText(destination));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacySceneData_IsDetectedFromRawTxtOrActionSidecars()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kklegacy_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var stem = Path.Combine(dir, "scene.txt");
            Assert.False(SceneFiles.HasLegacySceneData(stem));

            File.WriteAllText(stem, "raw");
            Assert.True(SceneFiles.HasLegacySceneData(stem));

            File.Delete(stem);
            SceneFiles.SaveActionSet(stem, Axes(1), Parts());
            Assert.True(SceneFiles.HasLegacySceneData(stem));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CompleteProfiles_AreSortedByNewestRelatedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "kkprofilesort_" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            SceneFiles.SaveActionSet(AxisInfo.ProfileStem(root, "older"), Axes(1), Parts());
            SceneFiles.SaveActionSet(AxisInfo.ProfileStem(root, "newer"), Axes(1), Parts());
            SetProfileTimes(root, "older", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            SetProfileTimes(root, "newer", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(new[] { "newer", "older" }, SceneFiles.ListCompleteProfiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private static AxisScript[] Axes(int length, int value = 1)
        => AxisInfo.All.Select(_ => new AxisScript { Values = Enumerable.Repeat(value, length).ToList() }).ToArray();

    private static ScenePart[] Parts()
        => new[] { new ScenePart { Part = 0, LovemakingMode = "normal", Charas = "chaF_001-chaM_001" } };

    private static void SetProfileTimes(string root, string key, DateTime timestamp)
    {
        string stem = AxisInfo.ProfileStem(root, key);
        foreach (var path in AxisInfo.All.Select(axis => AxisInfo.Sr6ScriptPath(stem, axis)).Append(AxisInfo.Sr6CfgPath(stem)))
            File.SetLastWriteTimeUtc(path, timestamp);
    }
}
