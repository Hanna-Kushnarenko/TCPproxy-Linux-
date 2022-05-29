namespace Config;

public class ServerControlLink
{
    public EndPoint EndPoint { get; set; }

    public List<SourcePoint> Sources { get; set; }

    public List<SourcePoint> ClientsAllowed { get; set; }

    public string Key { get; set; }

    public ServerControlLink()
    {
        this.EndPoint = new EndPoint();
        this.Sources = new List<SourcePoint>();
        this.ClientsAllowed = new List<SourcePoint>();
        this.Key = string.Empty;
    }
}