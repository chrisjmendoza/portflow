using System;
using System.Collections.Generic;

namespace PortFlow.Core.Pipeline;

public sealed class PipelineContext
{
    public string? UsbRootPath { get; init; }
    public string RunId { get; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    // lightweight log buffer for now (we'll replace with structured logging)
    public List<string> Log { get; } = new();
}
