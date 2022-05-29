namespace Config;

public class ServiceAddress
{
    public string? Address { get; set; }
    public int PortNumber { get; set; }
    public override string ToString() => this.Address + ":" + (object)this.PortNumber;
}