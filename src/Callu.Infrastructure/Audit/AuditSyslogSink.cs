using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Callu.Domain.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Callu.Infrastructure.Audit;

/// <summary>Forwards audit entries to a syslog collector as RFC 5424 frames with a CEF message.</summary>
public sealed class AuditSyslogSink : BackgroundService, IAuditSink
{
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(2);
    private const int SendAttempts = 2;

    private readonly AuditSyslogOptions _options;
    private readonly ILogger<AuditSyslogSink> _logger;
    private readonly Channel<AuditLog>? _queue;
    private readonly string _hostName;
    private readonly string _productVersion;

    private int _dropped;
    private Stream? _stream;
    private TcpClient? _client;

    public AuditSyslogSink(IOptions<AuditSyslogOptions> options, ILogger<AuditSyslogSink> logger)
    {
        _options = options.Value;
        _logger = logger;
        _hostName = Environment.MachineName;
        _productVersion = typeof(AuditSyslogSink).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        if (!_options.Enabled) return;

        _queue = Channel.CreateBounded<AuditLog>(
            new BoundedChannelOptions(Math.Max(1, _options.QueueCapacity))
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    /// <summary>Entries dropped because the collector was unreachable and the queue filled.</summary>
    public int DroppedCount => Volatile.Read(ref _dropped);

    public void Emit(AuditLog entry) => _queue?.Writer.TryWrite(entry);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_queue is null) return;

        _logger.LogInformation(
            "Audit syslog forwarding to {Host}:{Port} ({Transport})",
            _options.Host, _options.Port, _options.UseTls ? "TLS" : "plaintext TCP");

        if (_options.UseTls && _options.AcceptAnyCertificate)
            _logger.LogWarning(
                "Audit syslog is not validating the collector's certificate; the trail is readable "
                + "and forgeable by anything that can intercept the connection");

        var backoff = MinBackoff;

        await foreach (var entry in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 1; attempt <= SendAttempts; attempt++)
            {
                try
                {
                    await SendAsync(entry, stoppingToken);
                    backoff = MinBackoff;
                    ReportDrops();
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Disconnect();

                    if (attempt == SendAttempts)
                    {
                        _logger.LogError(ex,
                            "Audit syslog could not deliver entry {AuditId}; it stays in the audit table",
                            entry.Id);

                        await Task.Delay(backoff, stoppingToken);
                        backoff = backoff < MaxBackoff ? backoff * 2 : MaxBackoff;
                    }
                }
            }
        }
    }

    private async Task SendAsync(AuditLog entry, CancellationToken ct)
    {
        var stream = _stream ?? await ConnectAsync(ct);
        var frame = AuditCefFormatter.Frame(entry, _options, _hostName, _productVersion);
        var payload = Encoding.UTF8.GetBytes(frame);

        // Octet counting (RFC 6587): a collector reading a stream cannot otherwise tell where a
        // frame ends, since the message itself may contain anything. Prefix and frame go out in one
        // write so a reader cannot see a length with no message behind it yet.
        var prefix = Encoding.ASCII.GetBytes($"{payload.Length} ");
        var buffer = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(buffer, 0);
        payload.CopyTo(buffer, prefix.Length);

        await stream.WriteAsync(buffer, ct);
        await stream.FlushAsync(ct);
    }

    private async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ConnectTimeoutSeconds)));

        var client = new TcpClient();
        await client.ConnectAsync(_options.Host!, _options.Port, timeout.Token);

        Stream stream = client.GetStream();

        if (_options.UseTls)
        {
            var tls = new SslStream(stream, leaveInnerStreamOpen: false, ValidateCertificate);
            await tls.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = _options.Host! }, timeout.Token);
            stream = tls;
        }

        _client = client;
        _stream = stream;
        return stream;
    }

    private bool ValidateCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
        System.Security.Cryptography.X509Certificates.X509Chain? chain, SslPolicyErrors errors)
        => errors == SslPolicyErrors.None || _options.AcceptAnyCertificate;

    private void ReportDrops()
    {
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped == 0) return;

        _logger.LogWarning(
            "Audit syslog dropped {Count} entry(ies) while the collector was unreachable; they are "
            + "still in the audit table and the stream file", dropped);
    }

    private void Disconnect()
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    public override void Dispose()
    {
        _queue?.Writer.TryComplete();
        Disconnect();
        base.Dispose();
    }
}
