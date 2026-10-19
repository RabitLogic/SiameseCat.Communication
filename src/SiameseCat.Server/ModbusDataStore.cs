using SiameseCat.Core;
namespace SiameseCat.Server;

/// <summary>
/// Thread-safe register and coil storage used by <see cref="ModbusTcpServer"/>.
/// The application can preload values or change them while a client is connected.
/// </summary>
public sealed class ModbusDataStore
{
    private readonly object _sync = new();
    private readonly ushort[] _holdingRegisters;
    private readonly ushort[] _inputRegisters;
    private readonly bool[] _coils;
    private readonly bool[] _discreteInputs;

    /// <summary>Creates a store with the given buffer sizes.</summary>
    /// <param name="holdingRegisters">Number of holding registers (FC03/FC06/FC16/FC23).</param>
    /// <param name="inputRegisters">Number of input registers (FC04).</param>
    /// <param name="coils">Number of coils (FC01/FC05/FC15).</param>
    /// <param name="discreteInputs">Number of discrete inputs (FC02).</param>
    public ModbusDataStore(
        int holdingRegisters = 256,
        int inputRegisters = 256,
        int coils = 256,
        int discreteInputs = 256)
    {
        _holdingRegisters = CreateBuffer(holdingRegisters, nameof(holdingRegisters));
        _inputRegisters = CreateBuffer(inputRegisters, nameof(inputRegisters));
        _coils = CreateBits(coils, nameof(coils));
        _discreteInputs = CreateBits(discreteInputs, nameof(discreteInputs));
    }

    /// <summary>Gets the number of holding registers.</summary>
    public int HoldingRegisterCount => _holdingRegisters.Length;

    /// <summary>Gets the number of input registers.</summary>
    public int InputRegisterCount => _inputRegisters.Length;

    /// <summary>Gets the number of coils.</summary>
    public int CoilCount => _coils.Length;

    /// <summary>Gets the number of discrete inputs.</summary>
    public int DiscreteInputCount => _discreteInputs.Length;

    /// <summary>Gets or sets a holding register. Indexes outside the buffer are ignored.</summary>
    /// <param name="index">Register offset.</param>
    public ushort this[int index]
    {
        get
        {
            lock (_sync)
            {
                return index >= 0 && index < _holdingRegisters.Length ? _holdingRegisters[index] : (ushort)0;
            }
        }

        set
        {
            lock (_sync)
            {
                if (index >= 0 && index < _holdingRegisters.Length)
                {
                    _holdingRegisters[index] = value;
                }
            }
        }
    }

    /// <summary>Writes several holding registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteHoldingRegisters(int start, IReadOnlyList<ushort> values)
    {
        Guard.NotNull(values, nameof(values));

        lock (_sync)
        {
            if (!Fits(start, values.Count, _holdingRegisters.Length))
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                _holdingRegisters[start + i] = values[i];
            }

            return true;
        }
    }

    /// <summary>Reads several holding registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="count">Number of registers.</param>
    /// <returns>The register values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public ushort[]? ReadHoldingRegisters(int start, int count)
    {
        lock (_sync)
        {
            if (!Fits(start, count, _holdingRegisters.Length))
            {
                return null;
            }

            var values = new ushort[count];
            Array.Copy(_holdingRegisters, start, values, 0, count);
            return values;
        }
    }

    /// <summary>Writes several input registers (usually done by the simulated device, not by the client).</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteInputRegisters(int start, IReadOnlyList<ushort> values)
    {
        Guard.NotNull(values, nameof(values));

        lock (_sync)
        {
            if (!Fits(start, values.Count, _inputRegisters.Length))
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                _inputRegisters[start + i] = values[i];
            }

            return true;
        }
    }

    /// <summary>Reads several input registers.</summary>
    /// <param name="start">First register offset.</param>
    /// <param name="count">Number of registers.</param>
    /// <returns>The register values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public ushort[]? ReadInputRegisters(int start, int count)
    {
        lock (_sync)
        {
            if (!Fits(start, count, _inputRegisters.Length))
            {
                return null;
            }

            var values = new ushort[count];
            Array.Copy(_inputRegisters, start, values, 0, count);
            return values;
        }
    }

    /// <summary>Writes several coils.</summary>
    /// <param name="start">First coil offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteCoils(int start, IReadOnlyList<bool> values)
    {
        Guard.NotNull(values, nameof(values));

        lock (_sync)
        {
            if (!Fits(start, values.Count, _coils.Length))
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                _coils[start + i] = values[i];
            }

            return true;
        }
    }

    /// <summary>Reads several coils.</summary>
    /// <param name="start">First coil offset.</param>
    /// <param name="count">Number of coils.</param>
    /// <returns>The coil values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public bool[]? ReadCoils(int start, int count)
    {
        lock (_sync)
        {
            if (!Fits(start, count, _coils.Length))
            {
                return null;
            }

            var values = new bool[count];
            Array.Copy(_coils, start, values, 0, count);
            return values;
        }
    }

    /// <summary>Writes several discrete inputs (usually done by the simulated device, not by the client).</summary>
    /// <param name="start">First input offset.</param>
    /// <param name="values">Values to write.</param>
    /// <returns><see langword="false"/> when the range does not fit the buffer.</returns>
    public bool TryWriteDiscreteInputs(int start, IReadOnlyList<bool> values)
    {
        Guard.NotNull(values, nameof(values));

        lock (_sync)
        {
            if (!Fits(start, values.Count, _discreteInputs.Length))
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                _discreteInputs[start + i] = values[i];
            }

            return true;
        }
    }

    /// <summary>Reads several discrete inputs.</summary>
    /// <param name="start">First input offset.</param>
    /// <param name="count">Number of inputs.</param>
    /// <returns>The input values, or <see langword="null"/> when the range does not fit the buffer.</returns>
    public bool[]? ReadDiscreteInputs(int start, int count)
    {
        lock (_sync)
        {
            if (!Fits(start, count, _discreteInputs.Length))
            {
                return null;
            }

            var values = new bool[count];
            Array.Copy(_discreteInputs, start, values, 0, count);
            return values;
        }
    }

    internal bool TryWriteSingleCoil(int index, bool value)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _coils.Length)
            {
                return false;
            }

            _coils[index] = value;
            return true;
        }
    }

    internal bool TryWriteSingleRegister(int index, ushort value)
    {
        lock (_sync)
        {
            if (index < 0 || index >= _holdingRegisters.Length)
            {
                return false;
            }

            _holdingRegisters[index] = value;
            return true;
        }
    }

    private static bool Fits(int start, int count, int length) =>
        start >= 0 && count >= 0 && (long)start + count <= length;

    private static ushort[] CreateBuffer(int size, string name) => new ushort[ValidateSize(size, name)];

    private static bool[] CreateBits(int size, string name) => new bool[ValidateSize(size, name)];

    private static int ValidateSize(int size, string name)
    {
        if (size is < 1 or > 65536)
        {
            throw new ArgumentOutOfRangeException(name, size, "Buffer sizes must be between 1 and 65536.");
        }

        return size;
    }
}
