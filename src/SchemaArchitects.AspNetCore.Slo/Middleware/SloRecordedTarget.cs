namespace SchemaArchitects.AspNetCore.Slo;

/// <summary>
/// The values passed to <see cref="SloTargetMiddleware.RecordSloTarget"/> for one request.
/// Used by the internal test hook.
/// </summary>
internal readonly record struct SloRecordedTarget(string ApmId, string Method, string Route, double TargetMs);
