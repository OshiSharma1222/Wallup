namespace Wallup.Tests;

/// <summary>A folder of its own for each test, so the stores never touch the real %APPDATA%.</summary>
public abstract class TempFolder : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wallup-tests-" + Guid.NewGuid().ToString("N"));

    protected string FileIn(string name) => Path.Combine(_root, name);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
