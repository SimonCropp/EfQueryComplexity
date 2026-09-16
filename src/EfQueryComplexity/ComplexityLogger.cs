/// <summary>
/// Logs through the Entity Framework pipeline, so a message reaches LogTo, an ILoggerFactory and a
/// DiagnosticSource, and so ConfigureWarnings can change or silence it.
/// </summary>
static class ComplexityLogger
{
    // The behavior configured by ConfigureWarnings is read when a definition is created, so one is
    // kept per set of logging options
    static readonly ConditionalWeakTable<ILoggingOptions, EventDefinition<string>> definitions = new();

    static readonly ConditionalWeakTable<ILoggingOptions, EventDefinition<string>>.CreateValueCallback create = Create;

    /// <summary>
    /// Logs a message, built only once something is listening.
    /// </summary>
    /// <remarks>
    /// Building a message prints the whole query expression, which is not cheap, and the event can
    /// be silenced with ConfigureWarnings or filtered out by level.
    /// </remarks>
    public static void Log(IDiagnosticsLogger<DbLoggerCategory.Query> diagnostics, Func<string> message)
    {
        var definition = definitions.GetValue(diagnostics.Options, create);

        var shouldLog = diagnostics.ShouldLog(definition);
        var needsEventData = diagnostics.NeedsEventData(definition, out var diagnosticSourceEnabled, out var simpleLogEnabled);

        if (!shouldLog &&
            !needsEventData)
        {
            return;
        }

        var text = message();

        if (shouldLog)
        {
            definition.Log(diagnostics, text);
        }

        if (needsEventData)
        {
            var eventData = new EventData(
                definition,
                (definitionBase, _) => ((EventDefinition<string>) definitionBase).GenerateMessage(text));
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
