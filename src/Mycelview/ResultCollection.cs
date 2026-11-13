using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Mycelview;
public sealed class ResultCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> values)
    {
        if (this.SequenceEqual(values)) return;
        Items.Clear(); foreach (var value in values) Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
