namespace SiameseCat.Communication.Transport.Tcp;

/// <summary>Settings for the built-in <see cref="TcpFrameCodec"/> presets.</summary>
public sealed class TcpFrameOptions
{
    /// <summary>Gets or sets how frames are delimited. Defaults to <see cref="TcpFrameMode.Raw"/>.</summary>
    public TcpFrameMode Mode { get; set; } = TcpFrameMode.Raw;

    /// <summary>
    /// Gets or sets the size of the length field for <see cref="TcpFrameMode.LengthPrefix"/>.
    /// Valid values are 1, 2 and 4; defaults to 2.
    /// </summary>
    public int PrefixLength { get; set; } = 2;

    /// <summary>
    /// Gets or sets whether the length field counts its own bytes. Defaults to <see langword="false"/>,
    /// which means the field carries the payload length only.
    /// </summary>
    public bool LengthIncludesPrefix { get; set; }

    /// <summary>Gets or sets the byte order of the length field. Defaults to big-endian network order.</summary>
    public bool BigEndian { get; set; } = true;

    /// <summary>Gets or sets the trailing delimiter for <see cref="TcpFrameMode.Delimiter"/>. Defaults to CR LF.</summary>
    public byte[] Delimiter { get; set; } = { 0x0D, 0x0A };

    /// <summary>
    /// Gets or sets whether a decoded frame keeps the trailing delimiter. Defaults to
    /// <see langword="false"/>, so the payload does not contain separator bytes.
    /// </summary>
    public bool IncludeDelimiter { get; set; }

    /// <summary>Gets or sets the frame size for <see cref="TcpFrameMode.FixedLength"/>. Defaults to 1.</summary>
    public int FixedLength { get; set; } = 1;

    /// <summary>Gets or sets the largest payload accepted from or sent to the peer. Defaults to 64 KiB.</summary>
    public int MaxFrameLength { get; set; } = 64 * 1024;

    /// <summary>Validates the option values and throws when a value is out of range.</summary>
    public void Validate()
    {
        if (MaxFrameLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFrameLength), MaxFrameLength, "MaxFrameLength must be greater than zero.");
        }

        switch (Mode)
        {
            case TcpFrameMode.Raw:
                break;

            case TcpFrameMode.LengthPrefix:
                if (PrefixLength is not (1 or 2 or 4))
                {
                    throw new ArgumentOutOfRangeException(nameof(PrefixLength), PrefixLength, "PrefixLength must be 1, 2 or 4.");
                }

                break;

            case TcpFrameMode.Delimiter:
                if (Delimiter is null || Delimiter.Length == 0)
                {
                    throw new ArgumentException("A delimiter must be specified.", nameof(Delimiter));
                }

                if (MaxFrameLength > int.MaxValue - Delimiter.Length)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(MaxFrameLength),
                        MaxFrameLength,
                        "MaxFrameLength is too large for the configured delimiter.");
                }

                break;

            case TcpFrameMode.FixedLength:
                if (FixedLength < 1 || FixedLength > MaxFrameLength)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(FixedLength),
                        FixedLength,
                        "FixedLength must be between 1 and MaxFrameLength.");
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(Mode), Mode, "Unknown framing mode.");
        }
    }
}
