using LumeFetch.Core.Localization;
using LumeFetch.Core.Settings;
using LumeFetch.Infrastructure.Settings;

namespace LumeFetch.Core.Tests;

public sealed class PortableSettingsTests
{
    [Fact]
    public void PortableTestsRunWithoutReflectionBasedJson()
    {
        Assert.False(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
        const string desktopPlan = "{\"FormatSelector\":\"137+140\",\"OutputContainer\":\"mp4\",\"ExtractAudio\":false}";
        var plan = LumeFetch.Infrastructure.YtDlp.YtDlpPlanJson.Deserialize(desktopPlan);
        Assert.Equal("137+140", plan.FormatSelector);
        Assert.Equal("mp4", plan.OutputContainer);
        Assert.False(plan.ExtractAudio);
        Assert.Equal(plan, LumeFetch.Infrastructure.YtDlp.YtDlpPlanJson.Deserialize(
            LumeFetch.Infrastructure.YtDlp.YtDlpPlanJson.Serialize(plan)));
    }

    [Fact]
    public async Task HostDefaultsSurviveMissingAndCorruptSettingsWithoutOverwritingUserData()
    {
        var directory = Directory.CreateTempSubdirectory("lumefetch-mobile-settings-").FullName;
        try
        {
            var defaults = new AppSettings
            {
                DownloadDirectory = Path.Combine(directory, "Documents"),
                Language = "tr",
                MaxParallelDownloads = 3,
                EmbedThumbnail = true,
            };
            var path = Path.Combine(directory, "settings.json");
            var store = new JsonSettingsStore(path, defaults);
            Assert.IsAssignableFrom<ISettingsStore>(store);
            Assert.Equal(defaults, store.Load());
            Assert.False(File.Exists(path));
            await File.WriteAllTextAsync(path, "{corrupt");
            Assert.Equal(defaults, store.Load());
            Assert.NotNull(store.LoadWarning);
            Assert.Equal("{corrupt", await File.ReadAllTextAsync(path));
            await store.SaveAsync(defaults with { SmartPaste = false });
            Assert.Equal(defaults with { SmartPaste = false }, store.Load());
            Assert.Null(store.LoadWarning);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task GeneratedJsonKeepsTheExistingDesktopSettingsSchema()
    {
        var directory = Directory.CreateTempSubdirectory("lumefetch-settings-schema-").FullName;
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new AppSettings
            {
                DownloadDirectory = directory,
                Language = "en",
                MaxParallelDownloads = 6,
                SmartPaste = false,
                EmbedMetadata = false,
                EmbedThumbnail = true,
                SpotifyClientId = new string('a', 32),
            };
            var store = new JsonSettingsStore(path);
            await store.SaveAsync(settings);
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"SchemaVersion\": 1", json, StringComparison.Ordinal);
            Assert.Contains("\"DownloadDirectory\":", json, StringComparison.Ordinal);
            Assert.Equal(settings, new JsonSettingsStore(path).Load());
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void LocalizedPlatformLabelsDoNotHardcodeWindows(string language)
    {
        var catalog = new TranslationCatalog(language);
        Assert.Equal("v1.1.0-dev · Android", catalog.Format("VersionPlatform", "Android", "1.1.0-dev"));
        Assert.Equal("v1.0.0 · iOS", catalog.Format("VersionPlatform", "iOS", "1.0.0"));
        Assert.NotEqual("FolderAccessUnavailable", catalog["FolderAccessUnavailable"]);
    }
}
