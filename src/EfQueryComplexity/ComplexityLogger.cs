/// <summary>
/// Logs through the Entity Framework pipeline, so a message reaches LogTo, an ILoggerFactory and a
/// DiagnosticSource, and so ConfigureWarnings can change or silence it.
/// </summary>
static class ComplexityLogger
{
    // The behavior configured by ConfigureWarnings is read when a definition is created, so one is
    // kept per set of logging options
    static readonly ConditionalWeakTable<ILoggingOptions, EventDefinition<string>> definitions = new();

    public static void Log(IDiagnosticsLogger<DbLoggerCategory.Query> diagnostics, string message)
    {
        var definition = definitions.GetValue(diagnostics.Options, Create);

        if (diagnostics.ShouldLog(definition))
        {
            definition.Log(diagnostics, message);
        }

        if (diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled))
        {
            var eventData = new EventData(
                definition,
                (definitionBase, _) => ((EventDefinition<string>) definitionBase).GenerateMessage(message));
            diagnostics.DispatchEventData(definition, eventData, diagnosticSourceEnabled, simpleLogEnabled);
        }
    }

    static EventDefinition<string> Create(ILoggingOptions options) =>
        new(
            options,
            QueryComplexityEventId.LimitExceeded,
            LogLevel.Warning,
            "QueryComplexityEventId.LimitExceeded",
            level => LoggerMessage.Define<string>(level, QueryComplexityEventId.LimitExceeded, "{message}"));
}
