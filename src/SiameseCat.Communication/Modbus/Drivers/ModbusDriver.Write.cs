using SiameseCat.Communication;

namespace SiameseCat.Communication.Modbus;

public partial class ModbusDriver
{
    /// <inheritdoc/>
    protected override async Task DoWriteAsync(
        DeviceAddress address,
        DeviceValue value,
        CancellationToken cancellationToken)
    {
        switch (address.Region)
        {
            case ModbusAddressParser.CoilRegion:
                EnsureBool(address);
                await _client.WriteSingleCoilAsync(_unitId, ToUShort(address.Offset), value.AsBool(), cancellationToken)
                    .ConfigureAwait(false);
                return;

            case ModbusAddressParser.HoldingRegisterRegion:
                var registers = ToRegisters(address, value);
                if (registers.Length == 1)
                {
                    await _client.WriteSingleRegisterAsync(_unitId, ToUShort(address.Offset), registers[0], cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _client.WriteMultipleRegistersAsync(_unitId, ToUShort(address.Offset), registers, cancellationToken)
                        .ConfigureAwait(false);
                }

                return;

            case ModbusAddressParser.InputRegisterRegion:
            case ModbusAddressParser.DiscreteInputRegion:
                throw new NotSupportedException($"Modbus region '{address.Region}' is read-only.");

            default:
                throw new NotSupportedException($"Unknown Modbus region '{address.Region}'.");
        }
    }

    /// <inheritdoc/>
    protected override async Task DoWriteBatchAsync(
        IReadOnlyList<DeviceWriteRequest> requests,
        CancellationToken cancellationToken)
    {
        foreach (var group in GroupRequestsByRegion(requests))
        {
            switch (group.Key)
            {
                case ModbusAddressParser.HoldingRegisterRegion:
                    await WriteRegisterRunsAsync(group.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case ModbusAddressParser.CoilRegion:
                    await WriteCoilRunsAsync(group.Value, cancellationToken).ConfigureAwait(false);
                    break;

                case ModbusAddressParser.InputRegisterRegion:
                case ModbusAddressParser.DiscreteInputRegion:
                    throw new NotSupportedException($"Modbus region '{group.Key}' is read-only.");

                default:
                    throw new NotSupportedException($"Unknown Modbus region '{group.Key}'.");
            }
        }
    }

    private async Task WriteRegisterRunsAsync(
        List<DeviceWriteRequest> items,
        CancellationToken cancellationToken)
    {
        items.Sort(static (left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var index = 0;
        while (index < items.Count)
        {
            var start = items[index].Address.Offset;
            var registers = new List<ushort>(ToRegisters(items[index].Address, items[index].Value));
            index++;

            while (index < items.Count)
            {
                var next = items[index].Address;
                if (next.Offset != start + registers.Count)
                {
                    break;
                }

                var nextRegisters = ToRegisters(next, items[index].Value);
                if (registers.Count + nextRegisters.Length > MaxRegistersPerWrite)
                {
                    break;
                }

                registers.AddRange(nextRegisters);
                index++;
            }

            if (registers.Count == 1)
            {
                await _client.WriteSingleRegisterAsync(_unitId, ToUShort(start), registers[0], cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _client.WriteMultipleRegistersAsync(_unitId, ToUShort(start), registers.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task WriteCoilRunsAsync(
        List<DeviceWriteRequest> items,
        CancellationToken cancellationToken)
    {
        items.Sort(static (left, right) => left.Address.Offset.CompareTo(right.Address.Offset));

        var index = 0;
        while (index < items.Count)
        {
            var start = items[index].Address.Offset;
            EnsureBool(items[index].Address);
            var values = new List<bool> { items[index].Value.AsBool() };
            index++;

            while (index < items.Count && items[index].Address.Offset == start + values.Count)
            {
                EnsureBool(items[index].Address);
                if (values.Count == MaxCoilsPerWrite)
                {
                    break;
                }

                values.Add(items[index].Value.AsBool());
                index++;
            }

            if (values.Count == 1)
            {
                await _client.WriteSingleCoilAsync(_unitId, ToUShort(start), values[0], cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _client.WriteMultipleCoilsAsync(_unitId, ToUShort(start), values.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static Dictionary<string, List<DeviceWriteRequest>> GroupRequestsByRegion(
        IReadOnlyList<DeviceWriteRequest> requests)
    {
        var groups = new Dictionary<string, List<DeviceWriteRequest>>(StringComparer.Ordinal);
        foreach (var request in requests)
        {
            if (!groups.TryGetValue(request.Address.Region, out var list))
            {
                list = new List<DeviceWriteRequest>();
                groups[request.Address.Region] = list;
            }

            list.Add(request);
        }

        return groups;
    }

}
