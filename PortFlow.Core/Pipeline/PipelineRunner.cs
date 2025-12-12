using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Core.Pipeline;

public sealed class PipelineRunner
{
    private readonly IReadOnlyList<IPipelineStep> _steps;

    public PipelineRunner(IEnumerable<IPipelineStep> steps)
        => _steps = steps.ToList();

    public async Task RunAsync(PipelineContext ctx, CancellationToken ct)
    {
        foreach (var step in _steps)
        {
            ct.ThrowIfCancellationRequested();
            ctx.Log.Add($"START {step.Name}");
            await step.ExecuteAsync(ctx, ct).ConfigureAwait(false);
            ctx.Log.Add($"DONE  {step.Name}");
        }
    }
}
