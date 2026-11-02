using Emd.Adapters;

namespace Emd.Adapters.Tests;

public class RenodeBridgeScriptTests
{
    [Fact]
    public void Script_connects_uart_to_a_socket_terminal()
    {
        var script = RenodeBridgeScript.Build("axis.elf", "axis.repl", "uart1", 4567);

        Assert.Contains("CreateServerSocketTerminal 4567", script);
        Assert.Contains("connector Connect sysbus.uart1 emd_uart", script);
        Assert.Contains("sysbus LoadELF", script);
        Assert.Contains("start", script);
    }
}
