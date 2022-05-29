namespace Config;

public class EndPoint
{
    public string? IP { get; set; }
    public int PortNumber { get; set; }
    public override string ToString() => this.IP + ":" + (object)this.PortNumber;
}