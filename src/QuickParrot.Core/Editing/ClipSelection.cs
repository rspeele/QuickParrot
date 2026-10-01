namespace QuickParrot.Core.Editing;

/// <summary>A range of frames [Start, End) within the audio being edited.</summary>
public readonly record struct ClipSelection(int Start, int End)
{
    public int Length => End - Start;

    public bool Contains(double frame) => frame >= Start && frame < End;

    /// <summary>
    /// Orders two points into a selection inside [0, totalFrames), growing it to at least
    /// <paramref name="minLength"/> frames: away from <paramref name="anchor"/> where there's room, otherwise back over it.
    /// </summary>
    public static ClipSelection FromPoints(int anchor, int other, int totalFrames, int minLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalFrames, 0);
        minLength = Math.Clamp(minLength, 0, totalFrames);
        anchor = Math.Clamp(anchor, 0, totalFrames);
        other = Math.Clamp(other, 0, totalFrames);

        int start = Math.Min(anchor, other), end = Math.Max(anchor, other);
        if (end - start >= minLength)
            return new ClipSelection(start, end);

        if (other >= anchor)
        {
            end = Math.Min(start + minLength, totalFrames);
            start = end - minLength;
        }
        else
        {
            start = Math.Max(end - minLength, 0);
            end = start + minLength;
        }

        return new ClipSelection(start, end);
    }

    public static ClipSelection All(int totalFrames) => new(0, totalFrames);
}
