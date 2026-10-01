using Wallup.Models;

namespace Wallup.Tests;

public sealed class TaskItemTests
{
    [Fact]
    public void Ticking_stamps_the_finish_time_before_anyone_hears_about_it()
    {
        // Saving is driven by PropertyChanged, so a late stamp wrote a finished task with
        // no finish time.
        var task = new TaskItem { Text = "x" };
        DateTimeOffset? seen = null;
        task.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TaskItem.IsDone))
            {
                seen = task.CompletedAt;
            }
        };

        task.IsDone = true;

        Assert.NotNull(seen);
    }

    [Fact]
    public void Unticking_clears_the_finish_time()
    {
        var task = new TaskItem { Text = "x", IsDone = true };

        task.IsDone = false;

        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void A_new_alarm_is_armed_again()
    {
        var task = new TaskItem { Text = "x", AlarmAt = DateTimeOffset.Now, HasFired = true };
        var changed = new List<string?>();
        task.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        task.AlarmAt = DateTimeOffset.Now.AddHours(1);

        Assert.False(task.HasFired);
        Assert.Contains(nameof(TaskItem.HasAlarm), changed);
    }

    [Fact]
    public void Setting_the_same_text_says_nothing()
    {
        var task = new TaskItem { Text = "same" };
        var raised = false;
        task.PropertyChanged += (_, _) => raised = true;

        task.Text = "same";

        Assert.False(raised);
    }
}
