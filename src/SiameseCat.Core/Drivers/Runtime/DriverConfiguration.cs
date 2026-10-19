using System.Globalization;

namespace SiameseCat.Core;

/// <summary>Connection settings shared by all drivers, independent of any protocol package.</summary>
public sealed class DriverConfiguration
{
    private static readonly Random JitterRandom = new();

    /// <summary>Gets or sets the transport kind. Defaults to <see cref="TransportKind.Tcp"/>.</summary>
    public TransportKind Transport { get; set; } = TransportKind.Tcp;

    /// <summary>Gets or sets the host name or IP address used by TCP/UDP transports.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Gets or sets the TCP/UDP port. Drivers apply their default port when this is 0.</summary>
    public int Port { get; set; }

    /// <summary>Gets or sets the serial port settings, used when <see cref="Transport"/> is <see cref="TransportKind.Serial"/>.</summary>
    public SerialSettings Serial { get; set; } = new();

    /// <summary>
    /// Gets or sets a factory that supplies the transport. When set, the driver no longer creates its
    /// own transport, which makes it possible to reuse an existing link or to share one physical port
    /// between several drivers. Use <see cref="TransportKind.Custom"/> when the link is not TCP, UDP or serial.
    /// </summary>
    public ITransportFactory? TransportFactory { get; set; }

    /// <summary>
    /// Gets or sets a lock shared by every driver that uses the same physical link. Drivers serialize
    /// their own requests by default; assign the same instance to all stations of a multi-drop bus so
    /// request/response frames do not interleave.
    /// </summary>
    public RequestLock? RequestLock { get; set; }

    /// <summary>
    /// Gets or sets whether every successful read is stamped with <see cref="PlcValue.Timestamp"/>
    /// (<see cref="DateTimeOffset.UtcNow"/>). Defaults to <see langword="false"/>.
    /// </summary>
    public bool StampReadTimestamps { get; set; }

    /// <summary>
    /// Gets or sets whether every transport frame is written to <see cref="IPlcLogger"/>
    /// at <see cref="PlcLogLevel.Trace"/>. Useful for field diagnostics; keep it off on hot paths.
    /// </summary>
    public bool LogFrames { get; set; }

    /// <summary>Gets or sets the per-request timeout. Defaults to 3 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Gets or sets the connection timeout. Defaults to 5 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the number of additional attempts for transient failures. Defaults to 0.</summary>
    public int RetryCount { get; set; }

    /// <summary>Gets or sets the delay between retries. Defaults to 200 milliseconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets or sets the upper bound of the exponential retry backoff. Defaults to 30 seconds.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets whether retry delays are randomized (50–100 % of the backoff). Defaults to <see langword="true"/>.</summary>
    public bool RetryJitter { get; set; } = true;

    /// <summary>Gets or sets whether write operations may be retried. Defaults to <see langword="false"/>.</summary>
    public bool RetryWrites { get; set; }

    /// <summary>
    /// Gets or sets the protocol-native address used by <see cref="IHealthCheckable.PingAsync"/> and the
    /// keep-alive loop, for example <c>40001</c>, <c>M0.0</c> or <c>D0</c>. Probe reads have no side effects.
    /// </summary>
    public string? ProbeAddress { get; set; }

    /// <summary>Gets or sets the keep-alive interval. Defaults to <see cref="TimeSpan.Zero"/>, which disables it.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets how many consecutive keep-alive failures trigger a reconnect. Defaults to 2.</summary>
    public int KeepAliveFailureThreshold { get; set; } = 2;

    /// <summary>
    /// Gets or sets whether polling subscriptions randomize each interval by ±20 %. Defaults to
    /// <see langword="true"/> so several drivers do not poll a PLC in lockstep.
    /// </summary>
    public bool SubscriptionJitter { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum delay applied after consecutive subscription read failures.
    /// The delay doubles per failure up to this bound. Defaults to 30 seconds; zero disables backoff.
    /// </summary>
    public TimeSpan SubscriptionMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets protocol-specific options, for example <c>unitId</c>, <c>rack</c> or <c>slot</c>.</summary>
    public IDictionary<string, string> Options { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets an option value.</summary>
    /// <param name="key">Option key.</param>
    /// <returns>The value, or <see langword="null"/> when the option is not set.</returns>
    public string? GetOption(string key) => Options.TryGetValue(key, out var value) ? value : null;

    /// <summary>Gets whether an option is present.</summary>
    /// <param name="key">Option key.</param>
    public bool HasOption(string key) => Options.ContainsKey(key);

    /// <summary>Gets an option parsed as an integer.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="defaultValue">Value returned when the option is missing or empty.</param>
    /// <exception cref="ArgumentException">The option is present but not an integer.</exception>
    public int GetOption(string key, int defaultValue) =>
        GetOption(key, defaultValue, int.MinValue, int.MaxValue);

    /// <summary>Gets an option parsed as an integer and validates its range.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="defaultValue">Value returned when the option is missing or empty.</param>
    /// <param name="min">Inclusive lower bound.</param>
    /// <param name="max">Inclusive upper bound.</param>
    /// <exception cref="ArgumentException">The option is present but not an integer.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside <paramref name="min"/>..<paramref name="max"/>.</exception>
    public int GetOption(string key, int defaultValue, int min, int max)
    {
        if (!Options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"Option '{key}' must be an integer but was '{value}'.", nameof(key));
        }

        if (parsed < min || parsed > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(key),
                parsed,
                $"Option '{key}' must be between {min} and {max} but was {parsed}.");
        }

        return parsed;
    }

    /// <summary>Gets an option parsed as a byte, without silent truncation.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="defaultValue">Value returned when the option is missing or empty.</param>
    public byte GetByteOption(string key, byte defaultValue) =>
        (byte)GetOption(key, defaultValue, byte.MinValue, byte.MaxValue);

    /// <summary>Gets an option parsed as an unsigned 16-bit integer, without silent truncation.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="defaultValue">Value returned when the option is missing or empty.</param>
    public ushort GetUInt16Option(string key, ushort defaultValue) =>
        (ushort)GetOption(key, defaultValue, ushort.MinValue, ushort.MaxValue);

    /// <summary>Gets an option parsed as a boolean.</summary>
    /// <param name="key">Option key.</param>
    /// <param name="defaultValue">Value returned when the option is missing or empty.</param>
    /// <exception cref="ArgumentException">The option is present but not a boolean.</exception>
    public bool GetBooleanOption(string key, bool defaultValue)
    {
        if (!Options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new ArgumentException($"Option '{key}' must be 'true' or 'false' but was '{value}'.", nameof(key));
        }

        return parsed;
    }

    /// <summary>
    /// Computes the delay before a retry using exponential backoff (base × 2^(attempt-1), capped at
    /// <see cref="MaxRetryDelay"/>) with optional equal jitter.
    /// </summary>
    /// <param name="attempt">1-based retry number.</param>
    public TimeSpan GetRetryDelay(int attempt)
    {
        if (attempt <= 0 || RetryDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maxMs = MaxRetryDelay > TimeSpan.Zero ? MaxRetryDelay.TotalMilliseconds : 30_000;
        var exponential = RetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var delayMs = Math.Min(exponential, maxMs);

        if (RetryJitter)
        {
            delayMs *= 0.5 + (NextJitter() * 0.5);
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }

    private static double NextJitter()
    {
        lock (JitterRandom)
        {
            return JitterRandom.NextDouble();
        }
    }

    /// <summary>Validates the configuration.</summary>
    public void Validate()
    {
        if (Transport == TransportKind.None)
        {
            throw new ArgumentException("A transport kind must be specified.", nameof(Transport));
        }

        if (Transport == TransportKind.Custom && TransportFactory is null)
        {
            throw new ArgumentException(
                "TransportFactory must be set when the transport kind is Custom.",
                nameof(TransportFactory));
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Timeout must be greater than zero.");
        }

        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), ConnectTimeout, "ConnectTimeout must be greater than zero.");
        }

        if (RetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount), RetryCount, "RetryCount cannot be negative.");
        }

        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelay), RetryDelay, "RetryDelay cannot be negative.");
        }

        if (MaxRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay), MaxRetryDelay, "MaxRetryDelay cannot be negative.");
        }

        if (KeepAliveInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(KeepAliveInterval),
                KeepAliveInterval,
                "KeepAliveInterval cannot be negative.");
        }

        if (KeepAliveFailureThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(KeepAliveFailureThreshold),
                KeepAliveFailureThreshold,
                "KeepAliveFailureThreshold must be at least 1.");
        }

        if (KeepAliveInterval > TimeSpan.Zero && string.IsNullOrWhiteSpace(ProbeAddress))
        {
            throw new ArgumentException(
                "A ProbeAddress is required when KeepAliveInterval is enabled.",
                nameof(ProbeAddress));
        }

        if (SubscriptionMaxBackoff < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SubscriptionMaxBackoff),
                SubscriptionMaxBackoff,
                "SubscriptionMaxBackoff cannot be negative.");
        }

        if (TransportFactory is not null)
        {
            return;
        }

        if (Transport is TransportKind.Tcp or TransportKind.Udp)
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                throw new ArgumentException("Host must be specified for TCP/UDP transports.", nameof(Host));
            }

            if (Port is < 0 or > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between 0 and 65535.");
            }
        }

        if (Transport == TransportKind.Serial)
        {
            Serial.Validate();
        }
    }
}

/// <summary>Serial port settings expressed without a dependency on <c>System.IO.Ports</c>.</summary>
public sealed class SerialSettings
{
    /// <summary>Gets or sets the port name, for example <c>COM3</c> or <c>/dev/ttyUSB0</c>.</summary>
    public string PortName { get; set; } = "COM1";

    /// <summary>Gets or sets the baud rate. Defaults to 9600.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>Gets or sets the data bits. Defaults to 8.</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>Gets or sets the parity: <c>none</c>, <c>odd</c>, <c>even</c>, <c>mark</c> or <c>space</c>.</summary>
    public string Parity { get; set; } = "none";

    /// <summary>Gets or sets the stop bits: <c>one</c>, <c>onePointFive</c> or <c>two</c>.</summary>
    public string StopBits { get; set; } = "one";

    /// <summary>Gets or sets the handshake: <c>none</c>, <c>requestToSend</c>, <c>requestToSendXOnXOff</c> or <c>xOnXOff</c>.</summary>
    public string Handshake { get; set; } = "none";

    /// <summary>Gets or sets the blocking read timeout. Defaults to 1 second.</summary>
    public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Validates the settings.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName))
        {
            throw new ArgumentException("Serial port name must be specified.", nameof(PortName));
        }

        if (BaudRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(BaudRate), BaudRate, "BaudRate must be greater than zero.");
        }

        if (DataBits is < 5 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(DataBits), DataBits, "DataBits must be between 5 and 8.");
        }

        if (ReadTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadTimeout), ReadTimeout, "ReadTimeout must be greater than zero.");
        }
    }
}
