namespace QuickParrot.Core.Editing;

public static class SlidingMinimum
{
    /// <summary>result[i] = min(values[i .. i + window - 1]), truncated at the end; O(n) via a monotonic deque.</summary>
    public static double[] Forward(ReadOnlySpan<double> values, int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        var result = new double[values.Length];
        var deque = new int[values.Length];
        int head = 0, tail = 0;
        for (var i = values.Length - 1; i >= 0; i--)
        {
            while (tail > head && values[deque[tail - 1]] >= values[i])
                tail--;

            deque[tail++] = i;
            if (deque[head] >= i + window)
                head++;

            result[i] = values[deque[head]];
        }

        return result;
    }
}
