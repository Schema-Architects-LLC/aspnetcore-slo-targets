namespace SchemaArchitects.AspNetCore.Slo.Tests;

/// <summary>
/// Test classes that subscribe to the static SloTargetMiddleware.OnTargetRecorded hook must share
/// this collection so xUnit runs them sequentially instead of in parallel.
/// </summary>
[CollectionDefinition(Name)]
public class SloTargetHookCollection
{
    public const string Name = "SloTargetHook";
}
