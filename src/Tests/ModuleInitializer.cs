public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.InitializePlugins();

        VerifierSettings.AddExtraSettings(_ => _.Converters.Add(new UnboundedEntitiesConverter()));

        // LocalDB start time on a hosted build agent is erratic, and the default connect timeout is
        // 15 seconds
        LocalDbSettings.ConnectionBuilder(_ => _.ConnectTimeout = 300);
    }
}
