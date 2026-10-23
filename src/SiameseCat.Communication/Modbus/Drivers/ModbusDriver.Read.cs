using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public partial class ModbusDriver
{
    /// <inheritdoc/>
    protected override async Task<DeviceValue> DoReadAsync(DeviceAddress address, CancellationToken cancellationToken)
    {
        switch (address.Region)
        {
            case ModbusAddressParser.CoilRegion:
                EnsureBool(address);
                var coils = await _client.ReadCoilsAsync(_unitId, ToUShort(address.Offset), 1, cancellationToken)
                    .ConfigureAwait(false);
                return DeviceValue.FromBool(coils[0]);

            case ModbusAddressParser.DiscreteInputRegion:
                EnsureBool(address);
                var inputs = await _client.ReadDiscreteInputsAsync(_unitId, ToUShort(address.Offset), 1, cancellationToken)
                    .ConfigureAwait(false);
                return DeviceValue.FromBool(inputs[0]);

            case ModbusAddressParser.HoldingRegisterRegion:
            case ModbusAddressParser.InputRegisterRegion:
                var count = GetRegisterCount(address);
                var registers = address.Region == ModbusAddressParser.HoldingRegisterRegion
                    ? await _client.ReadHoldingRegistersAsync(_unitId, ToUShort(address.Offset), ToUShort(count), cancellationToken).ConfigureAwait(false)
                    : await _client.ReadInputRegistersAsync(_unitId, ToUShort(address.Offset), ToUShort(count), cancellationToken).ConfigureAwait(false);
                return FromRegisters(address, registers);

            default:
                throw new NotSupportedException($"Unknown Modbus region '{address.Region}'.");
        }
    }


    /// <inheritdoc/>
    protected override async Task<DeviceValue[]> DoReadBatchAsync(
        IReadOnlyList<DeviceAddress> addresses,
        CancellationToken cancellationToken)
    {
        var results = new DeviceValue[addresses.Count];

        foreach (var group in GroupByRegion(addresses))
        {
            if (group.Key is ModbusAddressParser.CoilRegion or ModbusAddressParser.DiscreteInputRegion)
            {
                await ReadBitsBatchAsync(group.Key, group.Value, results, cancellationToken).ConfigureAwait(false);
            }
            else if (group.Key is ModbusAddressParser.HoldingRegisterRegion or ModbusAddressParser.InputRegisterRegion)
            {
                await ReadRegistersBatchAsync(group.Key, group.Value, results, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new NotSupportedException($"Unknown Modbus region '{group.Key}'.");
            }
        }

        return results;
    }



    private async Task ReadBitsBatchAsync(
        string region,
        List<(int Index, DeviceAddress Address)> items,
        DeviceValue[] results,
        CancellationToken cancellationToken)
    {
        items.Sort((left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var runStart = 0;
        while (runStart < items.Count)
        {
            var start = items[runStart].Address.Offset;
            var runEnd = runStart + 1;
            var expected = start + 1;

            while (runEnd < items.Count
                   && items[runEnd].Address.Offset == expected
                   && items[runEnd].Address.Offset - start < MaxCoilsPerRequest)
            {
                expected++;
                runEnd++;
            }

            var count = runEnd - runStart;
            var values = region == ModbusAddressParser.CoilRegion
                ? await _client.ReadCoilsAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false)
                : await _client.ReadDiscreteInputsAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false);

            for (var i = runStart; i < runEnd; i++)
            {
                results[items[i].Index] = DeviceValue.FromBool(values[items[i].Address.Offset - start]);
            }

            runStart = runEnd;
        }
    }

    private async Task ReadRegistersBatchAsync(
        string region,
        List<(int Index, DeviceAddress Address)> items,
        DeviceValue[] results,
        CancellationToken cancellationToken)
    {
        items.Sort((left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var runStart = 0;
        while (runStart < items.Count)
        {
            var start = items[runStart].Address.Offset;
            var runEnd = runStart + 1;
            var expected = start + GetRegisterCount(items[runStart].Address);

            while (runEnd < items.Count
                   && items[runEnd].Address.DataType == items[runStart].Address.DataType
                   && items[runEnd].Address.Offset == expected)
            {
                var span = GetRegisterCount(items[runEnd].Address);
                if (expected - start + span > MaxRegistersPerRequest)
                {
                    break;
                }

                expected += span;
                runEnd++;
            }

            var count = expected - start;
            var registers = region == ModbusAddressParser.HoldingRegisterRegion
                ? await _client.ReadHoldingRegistersAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false)
                : await _client.ReadInputRegistersAsync(_unitId, ToUShort(start), ToUShort(count), cancellationToken).ConfigureAwait(false);

            for (var i = runStart; i < runEnd; i++)
            {
                var address = items[i].Address;
                var offset = address.Offset - start;
                var span = GetRegisterCount(address);
                results[items[i].Index] = FromRegisters(address, registers.AsSpan(offset, span));
            }

            runStart = runEnd;
        }
    }

    private static Dictionary<string, List<(int Index, DeviceAddress Address)>> GroupByRegion(
        IReadOnlyList<DeviceAddress> addresses)
    {
        var groups = new Dictionary<string, List<(int, DeviceAddress)>>(StringComparer.Ordinal);
        for (var i = 0; i < addresses.Count; i++)
        {
            if (!groups.TryGetValue(addresses[i].Region, out var list))
            {
                list = new List<(int, DeviceAddress)>();
                groups[addresses[i].Region] = list;
            }

            list.Add((i, addresses[i]));
        }

        return groups;
    }

}
