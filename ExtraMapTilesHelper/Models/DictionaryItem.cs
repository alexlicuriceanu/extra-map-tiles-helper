using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ExtraMapTilesHelper.Models;

// Adding INotifyPropertyChanged lets the UI know when we click to expand/collapse
public class DictionaryItem : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public ObservableCollection<TextureItem> Textures { get; } = new();

    // Default to true so it auto-expands the moment it's imported!
    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}