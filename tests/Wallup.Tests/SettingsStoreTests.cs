using Wallup.Models;
using Wallup.Storage;

namespace Wallup.Tests;

public sealed class SettingsStoreTests : TempFolder
{
    [Fact]
    public void A_missing_file_gives_the_defaults()
    {
        var settings = new SettingsStore(FileIn("settings.json")).Load();

        Assert.Equal(OpenGesture.Both, settings.OpenWith);
        Assert.Equal(1.0, settings.Opacity);
    }

    [Fact]
    public void Settings_survive_a_save_and_a_load()
    {
        var store = new SettingsStore(FileIn("settings.json"));
        store.Save(new AppSettings { OpenWith = OpenGesture.CtrlAlt, Opacity = 0.5, FontSize = 18, ChipWidth = 260 });

        var loaded = store.Load();

        Assert.Equal(OpenGesture.CtrlAlt, loaded.OpenWith);
        Assert.Equal(0.5, loaded.Opacity);
        Assert.Equal(18, loaded.FontSize);
        Assert.Equal(260, loaded.ChipWidth);
    }

    [Fact]
    public void The_gesture_is_written_by_name()
    {
        var path = FileIn("settings.json");
        new SettingsStore(path).Save(new AppSettings { OpenWith = OpenGesture.RightClick });

        Assert.Contains("\"RightClick\"", File.ReadAllText(path));
    }

    [Fact]
    public void A_hand_edited_value_out_of_range_is_clamped()
    {
        // An opacity of zero would make every chip invisible, with no way to find them.
        var path = FileIn("settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "Opacity": 0, "FontSize": 500, "ChipWidth": 1 }""");

        var loaded = new SettingsStore(path).Load();

        Assert.Equal(0.25, loaded.Opacity);
        Assert.Equal(32, loaded.FontSize);
        Assert.Equal(140, loaded.ChipWidth);
    }
}
