using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QuickParrot.App.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Raises <see cref="PropertyChanged"/> for each property whose value differs between the two states.</summary>
    protected void OnPropertiesChanged<TState>(TState old, TState now, IEnumerable<(string Name, Func<TState, object?> Value)> properties)
    {
        foreach (var (name, value) in properties)
        {
            if (!Equals(value(old), value(now)))
                OnPropertyChanged(name);
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
