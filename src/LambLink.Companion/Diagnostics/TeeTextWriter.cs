using System.Text;

namespace LambLink.Companion.Diagnostics;

internal sealed class TeeTextWriter(params TextWriter[] writers) : TextWriter
{
    public override Encoding Encoding => writers.Length == 0 ? Encoding.UTF8 : writers[0].Encoding;

    public override void Write(char value)
    {
        foreach (var writer in writers) writer.Write(value);
    }

    public override void Write(string? value)
    {
        foreach (var writer in writers) writer.Write(value);
    }

    public override void WriteLine(string? value)
    {
        foreach (var writer in writers) writer.WriteLine(value);
    }

    public override void Flush()
    {
        foreach (var writer in writers) writer.Flush();
    }
}
