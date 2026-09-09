using LambLink.Companion.Overlay;

// Isolated real-server fixture: no CHZZK login, game, or production data access.
var dataDirectory = Path.GetFullPath(args[0]);
var path = OverlayBootstrap.EnsureFile(dataDirectory);
var modified = File.GetLastWriteTimeUtc(path);
if (OverlayBootstrap.EnsureFile(dataDirectory) != path || File.GetLastWriteTimeUtc(path) != modified)
    throw new Exception("An unchanged bootstrap must retain its path and timestamp.");
File.WriteAllText(path, "old bootstrap");
OverlayBootstrap.EnsureFile(dataDirectory);
if (!File.ReadAllText(path).Contains("lamblink-overlay-rendered"))
    throw new Exception("Bootstrap update failed.");
Console.WriteLine("BOOTSTRAP " + path);
RaffleOverlayServer? server = null;
try
{
    while (await Console.In.ReadLineAsync() is { } command)
    {
        switch (command)
        {
            case "START":
                server = new RaffleOverlayServer();
                server.DonationDisplayChanged += (id, state) => Console.WriteLine($"DISPLAY {id} {state}");
                server.Start();
                server.Open(120, "!신도");
                break;
            case "STOP":
                if (server is not null) await server.DisposeAsync();
                server = null;
                break;
            case "STATUS":
                if (server?.IsClientPolling != true || !server.IsClientDocumentCurrent)
                    throw new Exception("Real overlay is not polling a current document.");
                break;
            case "DONATION":
                server!.ShowCancelled(1);
                server.ShowDonation("복구 테스트", 1000, "테스트 효과", 3, "11111111111111111111111111111111");
                break;
            case "OFFLINE_DONATION":
                server!.ShowCancelled(1);
                server.ShowDonation("미연결 테스트", 3000, "표시 대기 테스트", 2, "22222222222222222222222222222222");
                break;
            case "EXIT": return;
            default: throw new Exception("Unknown fixture command: " + command);
        }
        Console.WriteLine("ACK " + command);
    }
}
finally { if (server is not null) await server.DisposeAsync(); }
