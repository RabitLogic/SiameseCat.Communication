using System.Text;
using SiameseCat.Core;
using SiameseCat.Protocols.Modbus;

namespace SiameseCat.Drivers.Inovance;

/// <summary>
/// Inovance PLC driver: a Modbus driver whose address parser understands Inovance soft elements.
/// </summary>
/// <remarks>
/// Inovance PLCs expose their data through Modbus RTU/TCP, so the PDU, batching and data
/// conversion logic is reused from <see cref="ModbusDriver"/>; only the address mapping differs.
/// </remarks>
public sealed class InovanceDriver : ModbusDriver
{
    /// <summary>Creates a driver over an existing Modbus client, primarily for testing or custom transports.</summary>
    /// <param name="client">Modbus client that performs the framed exchange.</param>
    /// <param name="configuration">Connection and retry settings.</param>
    /// <param name="unitId">Unit identifier used for requests.</param>
    /// <param name="mapping">Element-to-Modbus mapping; defaults to <see cref="InovanceMapping.Default"/>.</param>
    /// <param name="stringEncoding">Encoding used for <see cref="PlcDataType.String"/>.</param>
    /// <param name="swapBytes">Swaps the two bytes inside every register for strings and raw data.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="metrics">Optional metrics collector.</param>
    public InovanceDriver(
        ModbusClientBase client,
        DriverConfiguration configuration,
        byte unitId,
        InovanceMapping? mapping = null,
        Encoding? stringEncoding = null,
        bool swapBytes = false,
        IPlcLogger? logger = null,
        PlcMetrics? metrics = null)
        : base(
            client,
            configuration,
            unitId,
            InovanceDriverFactory.DefaultDescriptor,
            new InovanceAddressParser(mapping ?? InovanceMapping.Default),
            stringEncoding,
            swapBytes,
            logger,
            metrics)
    {
    }
}
