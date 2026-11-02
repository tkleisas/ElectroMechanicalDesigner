namespace Emd.Adapters;

public static class RenodeBridgeScript
{
    public static string Build(string firmwarePath, string platformPath, string uart = "uart0", int port = 3456) => $"""
        mach create
        machine LoadPlatformDescription @{Path.GetFullPath(platformPath)}
        sysbus LoadELF @{Path.GetFullPath(firmwarePath)}
        emulation CreateServerSocketTerminal {port} "emd_uart"
        connector Connect sysbus.{uart} emd_uart
        start
        """;
}
