using Microsoft.AspNetCore.SignalR;
using NewTek.NDI;
using System.Net.Sockets;

namespace Tractus.NdiToHlsFfmpeg.Controllers;

public class FinderWrapper
{
    public Finder Finder { get; }

    private readonly IHubContext<ControlSignalRHub> hub;

    public FinderWrapper(IHubContext<ControlSignalRHub> hub)
    {
        this.Finder = new Finder(showLocalSources: true);
        this.Finder.NewNdiSourceDiscovered += this.OnNewNdiSource;
        this.hub = hub;
    }

    private void OnNewNdiSource(Source source)
    {
        this.hub.Clients.All.SendAsync("NewNdiSourceFound");
    }
}
