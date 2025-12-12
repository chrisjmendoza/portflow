using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Core.Pipeline.Steps;

public sealed class HelloStep : IPipelineStep
{
    public string Name => "HelloStep";

    public Task ExecuteAsync(PipelineContext ctx, CancellationToken ct)
    {
        ctx.Log.Add($"Hello from PortFlow. USB root = {ctx.UsbRootPath ?? "(none)"}");
        return Task.CompletedTask;
    }
}
