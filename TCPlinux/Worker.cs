namespace TCPlinux;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
    }
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        TcpProxyManager.Instance.Run(false);
        await base.StartAsync(cancellationToken);
    }
    
    public override async  Task StopAsync(CancellationToken cancellationToken)
    {
        TcpProxyManager.Instance.Stop();
        await base.StopAsync(cancellationToken);
    }
    public override void Dispose()
    {
        base.Dispose();
    }

}