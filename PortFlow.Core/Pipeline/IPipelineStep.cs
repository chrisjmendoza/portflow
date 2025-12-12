using System.Threading;
using System.Threading.Tasks;

namespace PortFlow.Core.Pipeline;

public interface IPipelineStep
{
    string Name { get; }
    Task ExecuteAsync(PipelineContext ctx, CancellationToken ct);
}
