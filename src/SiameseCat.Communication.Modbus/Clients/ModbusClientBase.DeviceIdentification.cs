using SiameseCat.Communication;
using SiameseCat.Communication.Exceptions;

namespace SiameseCat.Communication.Modbus;

public abstract partial class ModbusClientBase
{
    private const byte FunctionReadDeviceIdentification = 0x2B;
    private const byte MeiTypeDeviceIdentification = 0x0E;
    private const int MaxDeviceIdentificationPages = 20;

    /// <summary>
    /// Reads the basic device identification objects (function code 0x2B, MEI type 0x0E) from
    /// <see cref="DefaultUnitId"/>: vendor name, product code and revision.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The objects the server returned.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task<ModbusDeviceIdentification> ReadDeviceIdentificationAsync(
        CancellationToken cancellationToken = default) =>
        ReadDeviceIdentificationAsync(DefaultUnitId, ModbusDeviceIdCode.Basic, 0, cancellationToken);

    /// <summary>
    /// Reads device identification objects (function code 0x2B, MEI type 0x0E) from a specific unit,
    /// following continuation pages until the server reports that no objects are left.
    /// </summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="code">Access type to request.</param>
    /// <param name="objectId">First object to read; use 0 for basic and regular access.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The objects the server returned, merged across continuation pages.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public Task<ModbusDeviceIdentification> ReadDeviceIdentificationAsync(
        byte unitId,
        ModbusDeviceIdCode code,
        byte objectId = 0,
        CancellationToken cancellationToken = default) =>
        ReadDeviceIdentificationAsync(unitId, code, objectId, followMore: true, cancellationToken);

    /// <summary>
    /// Reads device identification objects (function code 0x2B, MEI type 0x0E) from a specific unit.
    /// </summary>
    /// <param name="unitId">Unit (slave) identifier.</param>
    /// <param name="code">Access type to request.</param>
    /// <param name="objectId">First object to read; use 0 for basic and regular access.</param>
    /// <param name="followMore">
    /// When <see langword="true"/> the client keeps requesting while the server reports more objects,
    /// so the result contains every object of the requested category.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The objects the server returned.</returns>
    /// <exception cref="ModbusException">The server returned an exception response.</exception>
    public async Task<ModbusDeviceIdentification> ReadDeviceIdentificationAsync(
        byte unitId,
        ModbusDeviceIdCode code,
        byte objectId,
        bool followMore,
        CancellationToken cancellationToken = default)
    {
        if (code is < ModbusDeviceIdCode.Basic or > ModbusDeviceIdCode.Individual)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code), code, "The access type must be a value defined by ModbusDeviceIdCode.");
        }

        var objects = new Dictionary<byte, byte[]>();
        byte conformity = 0;
        var more = false;
        var next = objectId;
        var pages = 0;

        do
        {
            byte[] pdu = { FunctionReadDeviceIdentification, MeiTypeDeviceIdentification, (byte)code, next };
            byte[] response = await ExecutePduAsync(unitId, pdu, isWrite: false, cancellationToken)
                .ConfigureAwait(false);

            conformity = ParseDeviceIdentificationPage(response, code, objects, out more, out next);
            pages++;
        }
        while (followMore
            && code != ModbusDeviceIdCode.Individual
            && more
            && next != 0
            && pages < MaxDeviceIdentificationPages);

        return new ModbusDeviceIdentification(conformity, more, next, objects);
    }

    private static byte ParseDeviceIdentificationPage(
        byte[] response,
        ModbusDeviceIdCode code,
        Dictionary<byte, byte[]> objects,
        out bool moreFollows,
        out byte nextObjectId)
    {
        if (response.Length < 7
            || response[0] != FunctionReadDeviceIdentification
            || response[1] != MeiTypeDeviceIdentification
            || response[2] != (byte)code)
        {
            throw new ProtocolException("The Modbus device identification response has an unexpected header.");
        }

        moreFollows = response[4] != 0;
        nextObjectId = response[5];

        var count = response[6];
        var position = 7;
        for (var i = 0; i < count; i++)
        {
            if (position + 2 > response.Length)
            {
                throw new ProtocolException("The Modbus device identification response is truncated.");
            }

            var objectId = response[position];
            var length = response[position + 1];
            position += 2;
            if (position + length > response.Length)
            {
                throw new ProtocolException("The Modbus device identification response is truncated.");
            }

            var value = new byte[length];
            Array.Copy(response, position, value, 0, length);
            objects[objectId] = value;
            position += length;
        }

        if (position != response.Length)
        {
            throw new ProtocolException("The Modbus device identification response has an unexpected length.");
        }

        return response[3];
    }
}
