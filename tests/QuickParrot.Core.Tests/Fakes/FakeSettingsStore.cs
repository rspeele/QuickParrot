using QuickParrot.Core.Settings;

namespace QuickParrot.Core.Tests.Fakes;

public sealed class FakeSettingsStore : ISettingsStore
{
    public List<AppSettings> Saved { get; } = [];

    public SettingsLoadResult Load() => new(Saved.LastOrDefault() ?? new AppSettings());

    public void Save(AppSettings settings) => Saved.Add(settings);
}
