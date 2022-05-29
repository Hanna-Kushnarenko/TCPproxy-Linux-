using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Config;

public class LinkConfig
{
    public List<SimpleLink> SimpleLinks { get; set; }

    public int BindTimeout { get; set; }

    public int KeepAliveTimeout { get; set; }

    public ServerControlLink? ServerControlLink { get; set; }

    public List<RemoteGateLink> RemoteGateLinks { get; set; }

    public LogLevel LogLevel { get; set; }

    public LinkConfig()
    {
        this.BindTimeout = 60000;
        this.KeepAliveTimeout = 15000;
        this.SimpleLinks = new List<SimpleLink>();
        this.RemoteGateLinks = new List<RemoteGateLink>();
        this.ServerControlLink = null;
        this.LogLevel = LogLevel.Info;
    }
}