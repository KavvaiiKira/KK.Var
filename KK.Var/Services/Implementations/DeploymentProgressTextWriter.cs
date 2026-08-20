using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services.Implementations;

internal sealed class DeploymentProgressTextWriter(
    TextWriter writer,
    IProgress<DeploymentProgress>? progress) : TextWriter
{
    public override Encoding Encoding => writer.Encoding;

    public override void WriteLine(string? value)
    {
        writer.WriteLine(value);
        if (!string.IsNullOrWhiteSpace(value))
        {
            progress?.Report(new DeploymentProgress(-1, string.Empty, value));
        }
    }

    public override void Flush()
    {
        writer.Flush();
    }

    public override Task FlushAsync()
    {
        return writer.FlushAsync();
    }
}
