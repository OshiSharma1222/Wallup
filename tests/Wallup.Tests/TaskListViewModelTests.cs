using System.Windows.Threading;
using Wallup.Models;
using Wallup.Storage;
using Wallup.ViewModels;

namespace Wallup.Tests;

public sealed class TaskListViewModelTests : TempFolder
{
    private TaskStore Store => new(FileIn("tasks.json"));

    private TaskListViewModel Open() =>
        new(Store, new SettingsStore(FileIn("settings.json")), new AppSettings());

    [Fact]
    public void Finished_tasks_from_earlier_days_are_dropped_at_start()
    {
        var yesterday = DateTimeOffset.Now.AddDays(-1);
        Store.Save(
        [
            new TaskItem { Text = "done yesterday", IsDone = true, CompletedAt = yesterday },
            new TaskItem { Text = "done today", IsDone = true },
            new TaskItem { Text = "still to do", CreatedAt = yesterday },
        ]);

        var vm = Open();

        Assert.Equal(["done today", "still to do"], vm.Tasks.Select(t => t.Text).Order());
        Assert.Equal(1, vm.ToDo.Count);
        Assert.Equal(1, vm.DoneToday.Count);
    }

    [Fact]
    public void A_new_task_is_trimmed_and_saved_straight_away()
    {
        var vm = Open();

        var task = vm.Add("  Call the bank  ", 10, 20);

        Assert.Equal("Call the bank", task?.Text);
        var saved = Assert.Single(Store.Load());
        Assert.Equal("Call the bank", saved.Text);
        Assert.Equal(10, saved.X);
        Assert.Equal(20, saved.Y);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_task_is_not_added(string text)
    {
        var vm = Open();

        Assert.Null(vm.Add(text, 0, 0));
        Assert.Empty(vm.Tasks);
    }

    [Fact]
    public void Ticking_moves_a_task_from_to_do_to_done()
    {
        var vm = Open();
        var task = vm.Add("x", 0, 0)!;

        task.IsDone = true;

        // Live filtering moves the row on the dispatcher, not inside the setter.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        Assert.Equal(0, vm.ToDo.Count);
        Assert.Equal(1, vm.DoneToday.Count);
        Assert.True(Assert.Single(Store.Load()).IsDone);
    }

    [Fact]
    public void Clearing_finished_keeps_what_is_left_to_do()
    {
        var vm = Open();
        vm.Add("keep", 0, 0);
        vm.Add("clear", 0, 0)!.IsDone = true;

        vm.ClearCompleted();

        Assert.Equal("keep", Assert.Single(vm.Tasks).Text);
        Assert.Equal("keep", Assert.Single(Store.Load()).Text);
    }
}
