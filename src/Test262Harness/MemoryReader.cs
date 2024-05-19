#pragma warning disable CA1513

namespace System.IO;

internal sealed class MemoryReader : TextReader
{
    private readonly ReadOnlyMemory<char> _s;
    private int _pos;
    private int _length;
    private bool _disposed;

    public MemoryReader(ReadOnlyMemory<char> s)
    {
        _s = s;
        _length = s.Length;
    }

    public override void Close()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        _pos = 0;
        _length = 0;
        base.Dispose(disposing);
    }

    public override int Peek()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(null);
        }
        if (_pos == _length)
        {
            return -1;
        }

        return _s.Span[_pos];
    }

    // Reads the next character from the underlying string. The returned value
    // is -1 if no further characters are available.
    //
    public override int Read()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(null);
        }
        if (_pos == _length)
        {
            return -1;
        }

        return _s.Span[_pos++];
    }

    public override int Read(char[] buffer, int index, int count)
    {
        int n = _length - _pos;
        if (n > 0)
        {
            if (n > count)
            {
                n = count;
            }

            _s.Span.Slice(_pos, n).CopyTo(buffer.AsSpan(index));
            _pos += n;
        }
        return n;
    }

#if NET6_0_OR_GREATER
    public override int Read(Span<char> buffer)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(null);
        }

        int n = _length - _pos;
        if (n > 0)
        {
            if (n > buffer.Length)
            {
                n = buffer.Length;
            }

            _s.Span.Slice(_pos, n).CopyTo(buffer);
            _pos += n;
        }

        return n;
    }

    public override int ReadBlock(Span<char> buffer) => Read(buffer);
#endif

    public override string ReadToEnd()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(null);
        }

        ReadOnlySpan<char> s;
        if (_pos == 0)
        {
            s = _s.Span;
        }
        else
        {
            s = _s.Span.Slice(_pos, _length - _pos);
        }

        _pos = _length;
        return s.ToString();
    }

    public override string? ReadLine()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(null);
        }

        var span = _s.Span;
        int i = _pos;
        while (i < _length)
        {
            char ch = span[i];
            if (ch == '\r' || ch == '\n')
            {
                var result = span.Slice(_pos, i - _pos);
                _pos = i + 1;
                if (ch == '\r' && _pos < _length && span[_pos] == '\n')
                {
                    _pos++;
                }

                return result.ToString();
            }

            i++;
        }

        if (i > _pos)
        {
            var result = span.Slice(_pos, i - _pos);
            _pos = i;
            return result.ToString();
        }

        return null;
    }

    public override Task<string?> ReadLineAsync()
    {
        return Task.FromResult(ReadLine());
    }

    public override Task<string> ReadToEndAsync()
    {
        return Task.FromResult(ReadToEnd());
    }

    public override Task<int> ReadBlockAsync(char[] buffer, int index, int count)
    {
        return Task.FromResult(ReadBlock(buffer, index, count));
    }

#if NET6_0_OR_GREATER
    public override ValueTask<int> ReadBlockAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<int>(cancellationToken) : new ValueTask<int>(ReadBlock(buffer.Span));
#endif

    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        return Task.FromResult(Read(buffer, index, count));
    }

#if NET6_0_OR_GREATER
    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<int>(cancellationToken) : new ValueTask<int>(Read(buffer.Span));
#endif
}
