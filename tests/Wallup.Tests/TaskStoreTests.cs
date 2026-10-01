using Wallup.Models;
using Wallup.Storage;

namespace Wallup.Tests;

public sealed class TaskStoreTests : TempFolder
{
    [Fact]
    public void A_missing_file_is_an_empty_list()
    {
        Assert.Empty(new TaskStore(FileIn("tasks.json")).Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n ")]
    public void A_blank_file_is_a_fresh_start(string contents)
    {
        // What an interrupted write used to leave behind.
        var path = FileIn("tasks.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);

        Assert.Empty(new TaskStore(path).Load());
    }

    [Fact]
    public void Every_field_survives_a_save_and_a_load()
    {
        var finished = DateTimeOffset.Now.AddMinutes(-5);
        var alarm = DateTimeOffset.Now.AddHours(2);
        var original = new TaskItem
        {
            Text = "Water the plants",
            X = 120.5,
            Y = 340,
            AlarmAt = alarm,
            IsDone = true,
            CompletedAt = finished,
        };

        var store = new TaskStore(FileIn("tasks.json"));
        store.Save([original]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Water the plants", loaded.Text);
        Assert.Equal(120.5, loaded.X);
        Assert.Equal(340, loaded.Y);
        Assert.Equal(alarm, loaded.AlarmAt);
        Assert.True(loaded.IsDone);
        Assert.Equal(finished, loaded.CompletedAt);
        Assert.Equal(original.CreatedAt, loaded.CreatedAt);
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        var path = FileIn("tasks.json");
        new TaskStore(path).Save([new TaskItem { Text = "x" }]);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
