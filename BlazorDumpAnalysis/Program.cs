using Microsoft.Diagnostics.Runtime;
using System.CommandLine;

var processIdOpt = new Option<int?>(new[] { "-p", "--process-id" }, "The ID of the process to analyze.");

var dumpFileOpt = new Option<FileInfo?>(new[] { "-d", "--dump" }, "The memory dump file to analyze.");

var gcRootsOpt = new Option<bool>(new[] { "-r", "--roots" }, "Compute GC Roots for the circuits.")
{
    IsHidden = true,
};

var rootCommand = new RootCommand("Analyze a Blazor Server app memory usage.");

rootCommand.AddOption(processIdOpt);
rootCommand.AddOption(dumpFileOpt);
rootCommand.AddOption(gcRootsOpt);

rootCommand.AddValidator(r =>
{
    var processId = r.GetValueForOption(processIdOpt);
    var dumpFile = r.GetValueForOption(dumpFileOpt);

    if ((!processId.HasValue && dumpFile is null) ||
    (processId.HasValue && dumpFile is not null))
    {
        r.ErrorMessage = @"
Invalid parameter combination.
You must either specify a process ID or a dump file.";
    }
});

rootCommand.Name = "dotnet-blazor-server-memory-analyzer";

rootCommand.SetHandler(AttachAndAnalyze, processIdOpt, dumpFileOpt, gcRootsOpt);

return await rootCommand.InvokeAsync(args);

Task<int> AttachAndAnalyze(int? processId, FileInfo? dumpFile, bool gcRoots)
{
    using var dump = processId.HasValue ?
        DataTarget.CreateSnapshotAndAttach(processId.Value) :
        DataTarget.LoadDump(dumpFile!.FullName);

    var clr = dump.ClrVersions[0];

    var runtime = clr.CreateRuntime();

    var registry = runtime.Heap.EnumerateObjects()
        .FirstOrDefault(o => o.Type?.Name == "Microsoft.AspNetCore.Components.Server.Circuits.CircuitRegistry");

    var connectedCircuitCount = 0;
    if (registry.TryReadObjectField("<ConnectedCircuits>k__BackingField", out var connected) &&
        connected.TryReadObjectField("_tables", out var tables) &&
        tables.TryReadObjectField("_countPerLock", out var counts))
    {
        connectedCircuitCount = counts!.AsArray()!.ReadValues<int>(0, counts.AsArray()!.Length)!.Sum();
    }

    var disconnectedCircuitCount = 0;
    if (registry.TryReadObjectField("<DisconnectedCircuits>k__BackingField", out var disconnected) &&
        disconnected.TryReadObjectField("_coherentState", out var coherentState) &&
        coherentState.TryReadObjectField("_entries", out var entries) &&
        entries.TryReadObjectField("_tables", out var disconnectedTables) &&
        disconnectedTables.TryReadObjectField("_countPerLock", out var disconnectedCounts))
    {
        disconnectedCircuitCount = disconnectedCounts!.AsArray()!.ReadValues<int>(0, disconnectedCounts.AsArray()!.Length)!.Sum();
    }

    var circuits = runtime.Heap.EnumerateObjects()
        .Where(o => o.Type?.Name == "Microsoft.AspNetCore.Components.Server.Circuits.Circuit")
        .Select(c => new DumpCircuit(c))
        .ToArray();

    Console.WriteLine($"# Circuits summary");
    Console.WriteLine("|Total|Connected|Disconnected|");
    Console.WriteLine("|-----|---------|------------|");
    Console.WriteLine($"|{circuits.Length}|{connectedCircuitCount}|{disconnectedCircuitCount}|");
    Console.WriteLine();


    Console.WriteLine("|Circuit Id|Component Count|Circuit Generation|Disposed|");
    Console.WriteLine("|----------|---------------|------------------|--------|");
    foreach (var circ in circuits)
    {
        var gen = runtime.Heap.GetSegmentByAddress(circ.CircuitHost._circuitHost.Address)!
            .GetGeneration(circ.CircuitHost._circuitHost.Address);
        var disposed = circ.CircuitHost._circuitHost.ReadField<bool>("_disposed");
        Console.WriteLine($"|{circ.CircuitHost.CircuitId.Id}|{circ.CircuitHost.Renderer.ComponentStateById.Count}|{gen}|{disposed}|");
    }
    Console.WriteLine();

    var filters = new[]
    {
    "Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer",
    "Microsoft.AspNetCore.Components.RenderFragment",
    "Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime",
    "Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost",
    "Microsoft.AspNetCore.Components.Server.Circuits.RemoteNavigationManager",
    "System.Collections.Generic.NonRandomizedStringEqualityComparer+OrdinalIgnoreCaseComparer",
    "System.Collections.Generic.NonRandomizedStringEqualityComparer+OrdinalComparer",
    "Microsoft.Extensions.Logging.Logger<Microsoft.AspNetCore.Components.Routing.Router>",
    "Microsoft.AspNetCore.Components.Server.Circuits.RemoteNavigationInterception",
    "Microsoft.Extensions.Logging.LoggerFactory",
    "System.Reflection.RuntimeAssembly",
    "System.RuntimeType",
    "Microsoft.AspNetCore.Components.RenderFragment<Microsoft.AspNetCore.Components.RouteData>",
    "System.Threading.ExecutionContext"
};

    foreach (var circ in circuits)
    {
        Console.WriteLine($"### Circuit: {circ.CircuitHost.CircuitId.Id}");
        Console.WriteLine("|Id|Name|Frames|Buffer|Size|");
        Console.WriteLine("|--|----|------|------|----|");
        foreach (var (id, state) in circ.CircuitHost.Renderer.ComponentStateById)
        {
            var name = state.ComponentType.Name;
            var count = state.CurrentRenderTree.Entries.Count;
            var buffer = state.CurrentRenderTree.Entries.Items.Length;
            var size = ObjSize(state.Component, filters);
            Console.WriteLine($"|{id}|{name}|{count}|{buffer}|{size / 1024.0:0.00} KB|");
        }
        Console.WriteLine();
    }

    static ulong ObjSize(ClrObject input, string[]? filters = null)
    {
        filters ??= Array.Empty<string>();
        HashSet<string> skipFieldTypes = new HashSet<string>(filters);
        HashSet<ulong> seen = new HashSet<ulong>() { input };
        Stack<ClrObject> todo = new Stack<ClrObject>(100);
        todo.Push(input);

        int count = 0;
        ulong totalSize = 0;

        while (todo.Count > 0)
        {
            ClrObject curr = todo.Pop();

            count++;
            totalSize += curr.Size;

            foreach (var obj in curr.EnumerateReferences(carefully: false, considerDependantHandles: false))
            {
                if (seen.Add(obj))
                {
                    var name = obj.Type?.Name;
                    if (name != null && !skipFieldTypes.Contains(name))
                    {
                        todo.Push(obj);
                    }
                }
            }
        }

        return totalSize;
    }

    return Task.FromResult(0);
}

public class DumpCircuit
{
    private readonly ClrObject _circuit;
    private DumpCircuitHost? _circuitHost;

    public DumpCircuit(ClrObject circuit)
    {
        _circuit = circuit;
    }

    public DumpCircuitHost CircuitHost
    {
        get { return _circuitHost ??= new DumpCircuitHost(_circuit.ReadObjectField("_circuitHost")); }
    }

}

public class DumpCircuitHost
{
    public ClrObject _circuitHost;
    private DumpCircuitId? _circuitId;
    private DumpRenderer? _renderer;

    public DumpCircuitHost(ClrObject circuitHost)
    {
        _circuitHost = circuitHost;
    }

    public DumpCircuitId CircuitId => _circuitId ??= new DumpCircuitId(_circuitHost.ReadValueTypeField("<CircuitId>k__BackingField"));
    public DumpRenderer Renderer => _renderer ??= new DumpRenderer(_circuitHost.ReadObjectField("<Renderer>k__BackingField"));
}

public class DumpRenderer
{
    public ClrObject _renderer;

    public DumpRenderer(ClrObject clrValueType)
    {
        _renderer = clrValueType;
    }

    private Dictionary<int, DumpComponentState>? _componentStateById;

    public Dictionary<int, DumpComponentState> ComponentStateById =>
        _componentStateById ??= (new DumpDictionary<int, DumpComponentState>(
                    _renderer.ReadObjectField("_componentStateById"),
                    o => o.ReadField<int>("key")!,
                    o => new DumpComponentState(o.ReadObjectField("value")))
                .ToDictionary());
}

public class DumpDictionary<TKey, TValue> where TKey : notnull
{
    private ClrObject _dictionary;
    private readonly Func<ClrValueType, TKey> _keyConverter;
    private readonly Func<ClrValueType, TValue> _valueConverter;

    public DumpDictionary(ClrObject dumpDictionary, Func<ClrValueType, TKey> keyConverter, Func<ClrValueType, TValue> valueConverter)
    {
        _dictionary = dumpDictionary;
        _keyConverter = keyConverter;
        _valueConverter = valueConverter;
    }

    public Dictionary<TKey, TValue> ToDictionary()
    {
        var result = new Dictionary<TKey, TValue>();
        var array = _dictionary.ReadObjectField("_entries").AsArray();
        var count = _dictionary.ReadField<int>("_count");
        for (int i = 0; i < count; i++)
        {
            var entry = array.GetStructValue(i);
            var next = entry.ReadField<int>("next");
            if (next >= -1)
            {
                result[_keyConverter(entry)] = _valueConverter(entry);
            }
        }

        return result;
    }
}

public class DumpComponentState
{
    public ClrObject _componentState;
    private DumpRenderTreeBuilder? _currentRenderTree;

    public DumpComponentState(ClrObject componentState)
    {
        _componentState = componentState;
    }

    public int ComponentId => _componentState.ReadField<int>("<ComponentId>k__BackingField");

    public ClrObject Component => _componentState.ReadObjectField("<Component>k__BackingField")!;

    public ClrType ComponentType => _componentState.ReadObjectField("<Component>k__BackingField").Type!;


    public DumpRenderTreeBuilder CurrentRenderTree => _currentRenderTree ??= new DumpRenderTreeBuilder(_componentState.ReadObjectField("<CurrentRenderTree>k__BackingField"));
}

public class DumpRenderTreeBuilder
{
    private readonly ClrObject _renderTreeBuilder;
    private DumpRenderTreeFrameArrayBuilder? _entries;

    public DumpRenderTreeBuilder(ClrObject renderTreeBuilder)
    {
        _renderTreeBuilder = renderTreeBuilder;
    }

    public DumpRenderTreeFrameArrayBuilder Entries => _entries ??= new DumpRenderTreeFrameArrayBuilder(_renderTreeBuilder.ReadObjectField("_entries"));
}

public class DumpRenderTreeFrameArrayBuilder
{
    private ClrObject _renderTreeFrameArrayBuilder;

    public DumpRenderTreeFrameArrayBuilder(ClrObject renderTreeFrameArrayBuilder)
    {
        _renderTreeFrameArrayBuilder = renderTreeFrameArrayBuilder;
    }

    public int Count => _renderTreeFrameArrayBuilder.ReadField<int>("_itemsInUse");

    public ClrArray Items => _renderTreeFrameArrayBuilder.ReadObjectField("_items").AsArray();
}

public class DumpCircuitId
{
    private ClrValueType _circuitId;

    public DumpCircuitId(ClrValueType circuitId)
    {
        _circuitId = circuitId;
    }

    public string Id => _circuitId.ReadStringField("<Id>k__BackingField")!;

    public string Secret => _circuitId.ReadStringField("<Secret>k__BackingField")!;
}