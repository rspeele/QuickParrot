using System.Collections.Immutable;
using QuickParrot.Core.Keyboard;
using QuickParrot.Core.Library;

namespace QuickParrot.Core.Navigation;

internal sealed class FragmentComposer
{
    private readonly FolderEntry[] _clips;
    private readonly IReadOnlyDictionary<string, LibrarySearchFolderContext?> _contexts;
    private readonly List<FolderEntry> _phrase = [];
    private FolderEntry[] _matches = [];
    private string _query = "";
    private string? _speaker;
    private bool _emptyBackspace;
    private double _holdProgress;

    public FragmentComposer(IReadOnlyList<FolderEntry> clips)
    {
        _clips = clips.Where(clip => !clip.IsFolder && FragmentLibrary.GetSpeaker(clip.RelativePath) is not null).ToArray();
        _contexts = FragmentLibrary.CreateFolderContexts(_clips);
        UpdateSearch();
    }

    public bool Closed { get; private set; }
    public OverlayViewState ViewState { get; private set; } = null!;

    public IReadOnlyList<NavigationAction> Handle(ChordEvent evt)
    {
        switch (evt)
        {
            case SearchTextEntered text:
                _emptyBackspace = false;
                _query += string.Concat(text.Text.Where(c => !char.IsControl(c) && !char.IsDigit(c)));
                UpdateSearch();
                break;
            case SearchBackspacePressed:
                Backspace(false);
                break;
            case FragmentBackspacePressed backspace:
                Backspace(backspace.IsRepeat);
                break;
            case FragmentSelectionPressed select:
                _emptyBackspace = false;
                var index = select.Number - 1;
                if (index >= 0 && index < _matches.Length)
                {
                    var clip = _matches[index];
                    _phrase.Add(clip);
                    _speaker ??= FragmentLibrary.GetSpeaker(clip.RelativePath);
                    _query = "";
                    _holdProgress = 0;
                    UpdateSearch();
                }
                break;
            case FragmentHoldProgressChanged progress:
                _holdProgress = double.IsFinite(progress.Progress) ? Math.Clamp(progress.Progress, 0, 1) : 0;
                Publish();
                break;
            case FragmentSubmitPressed when _phrase.Count > 0:
                var paths = _phrase.Select(clip => clip.RelativePath).ToArray();
                Closed = true;
                _phrase.Clear();
                return [new PlayPhrase(paths)];
            case ChordCancelled:
                Closed = true;
                _phrase.Clear();
                break;
        }
        return [];
    }

    private void Backspace(bool isRepeat)
    {
        if (_query.Length == 0 && isRepeat)
            return;
        if (_query.Length > 0)
        {
            var elements = System.Globalization.StringInfo.ParseCombiningCharacters(_query);
            _query = _query[..elements[^1]];
            _emptyBackspace = false;
        }
        else if (_emptyBackspace)
        {
            if (_phrase.Count > 0)
                _phrase.RemoveAt(_phrase.Count - 1);
            if (_phrase.Count == 0)
                _speaker = null;
            _emptyBackspace = false;
        }
        else
            _emptyBackspace = true;
        UpdateSearch();
    }

    private void UpdateSearch()
    {
        var clips = _speaker is null ? _clips : _clips.Where(clip =>
            string.Equals(FragmentLibrary.GetSpeaker(clip.RelativePath), _speaker, StringComparison.OrdinalIgnoreCase)).ToArray();
        _matches = LibrarySearch.Match(clips, _query);
        Publish();
    }

    private void Publish() => ViewState = new OverlayViewState("", OverlayLayoutKind.Wheel,
        _matches.Select((clip, index) => new NumberedEntry(index + 1, clip.Name, false)
        {
            FolderContext = _contexts[clip.RelativePath],
        }).ToImmutableArray(), [], null, false)
    {
        SearchQuery = _query,
        IsFragmentSearch = true,
        FragmentSpeaker = _speaker,
        FragmentNames = _phrase.Select(clip => Path.GetFileNameWithoutExtension(clip.Name)).ToImmutableArray(),
        FragmentHoldProgress = _holdProgress,
    };
}
