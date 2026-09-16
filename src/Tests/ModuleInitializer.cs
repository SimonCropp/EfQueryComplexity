public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        VerifierSettings.InitializePlugins();

        // LocalDB start time on a hosted build agent is erratic, and the default connect timeout is
        // 15 seconds
        LocalDbSettings.ConnectionBuilder(_ => _.ConnectTimeout = 300);
    }
}
