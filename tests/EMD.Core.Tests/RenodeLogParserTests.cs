using Emd.Core.Manufacturing;

namespace Emd.Core.Tests;

public class RenodeLogParserTests
{
    [Fact]
    public void Extracts_uart_messages_and_strips_ansi()
    {
        var log = "[07:10:39.7873] [INFO] uart0: [host: 2.14s] " +
                  "\u001b[1;32muart:~$ \u001b[m\n" +
                  "[07:10:39.8000] [INFO] machine-0: Machine paused.\n";

        var events = RenodeLogParser.ExtractUart(log);

        var message = Assert.Single(events);
        Assert.Contains("uart:~$", message);
        Assert.DoesNotContain("[1;32m", message);
        Assert.Single(RenodeLogParser.ToJson(events));
    }
}
