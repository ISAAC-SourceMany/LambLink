using System.Text;

namespace ChzzkOfTheLamb.Companion.Diagnostics;

/// <summary>
/// Append-only UTF-8 writer with bounded local retention. The active file is kept at
/// <c>basePath</c> and older segments are shifted to .1, .2, ... .
/// </summary>
internal sealed class RollingFileTextWriter : TextWriter
{
    private readonly object _gate = new();
    private readonly string _basePath;
    private readonly long _maxBytes;
    private readonly int _archiveCount;
    private readonly UTF8Encoding _encoding = new(encoderShouldEmitUTF8Identifier: false);
    private StreamWriter _writer;
    private long _currentBytes;
    private bool _disposed;

    public RollingFileTextWriter(string basePath, long maxBytes, int archiveCount)
    {
        if (maxBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (archiveCount < 1) throw new ArgumentOutOfRangeException(nameof(archiveCount));

        _basePath = basePath;
        _maxBytes = maxBytes;
        _archiveCount = archiveCount;
        Directory.CreateDirectory(Path.GetDirectoryName(basePath)
                                  ?? throw new ArgumentException("Log path has no directory.", nameof(basePath)));
        _writer = OpenWriter();
        _currentBytes = new FileInfo(_basePath).Length;
    }

    public override Encoding Encoding => _encoding;

    public override void Write(char value) => Write(value.ToString());

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_gate)
        {
            ThrowIfDisposed();
            RotateIfRequired(_encoding.GetByteCount(value));
            _writer.Write(value);
            _writer.Flush();
            _currentBytes += _encoding.GetByteCount(value);
        }
    }

    public override void WriteLine(string? value)
    {
        var text = (value ?? string.Empty) + Environment.NewLine;
        Write(text);
    }

    public override void Flush()
    {
        lock (_gate)
        {
            if (!_disposed) _writer.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _writer.Dispose();
                }
            }
        }
        base.Dispose(disposing);
    }

    private StreamWriter OpenWriter() => new(
        new FileStream(_basePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
        _encoding) { AutoFlush = true };

    private void RotateIfRequired(int incomingBytes)
    {
        if (_currentBytes == 0 || _currentBytes + incomingBytes <= _maxBytes) return;

        _writer.Dispose();
        try
        {
            var oldest = ArchivePath(_archiveCount);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var index = _archiveCount - 1; index >= 1; index--)
            {
                var source = ArchivePath(index);
                if (File.Exists(source)) File.Move(source, ArchivePath(index + 1), overwrite: true);
            }
            if (File.Exists(_basePath)) File.Move(_basePath, ArchivePath(1), overwrite: true);
        }
        catch (IOException)
        {
            // A log viewer or antivirus can briefly deny rename/delete. Logging must remain
            // available, so keep appending and try rotation again on a later write.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort behavior as the transient file-lock case above.
        }
        finally
        {
            _writer = OpenWriter();
            _currentBytes = File.Exists(_basePath) ? new FileInfo(_basePath).Length : 0;
        }
    }

    private string ArchivePath(int index) => _basePath + "." + index;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RollingFileTextWriter));
    }
}
