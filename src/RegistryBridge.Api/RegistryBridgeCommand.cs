namespace RegistryBridge.Api;

public static class RegistryBridgeCommand
{
    public static void EnsureServe(string[] arguments)
    {
        if (arguments.Length == 0
            || arguments[0].StartsWith("--", StringComparison.Ordinal)
            || string.Equals(arguments[0], "serve", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ArgumentException("RegistryBridge only supports the 'serve' command.");
    }
}
