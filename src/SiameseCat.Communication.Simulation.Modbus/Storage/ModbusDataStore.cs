using SiameseCat.Communication;

namespace SiameseCat.Communication.Simulation.Modbus;

/// <summary>
/// Thread-safe register and coil storage used by the Modbus TCP and RTU slaves.
/// The application can preload values or change them while a client is connected, and can listen
/// to <see cref="HoldingRegistersWritten"/>, <see cref="InputRegistersWritten"/>,
/// <see cref="CoilsWritten"/> and <see cref="DiscreteInputsWritten"/> to react to writes.
/// </summary>
/// <remarks>
/// Each area has its own lock, so traffic on holding registers does not block coil access.
/// Coils and discrete inputs are stored as a bit map.
/// </remarks>
public sealed partial class ModbusDataStore
{
    private readonly object _holdingSync = new();
    private readonly object _inputSync = new();
    private readonly object _coilSync = new();
    private readonly object _discreteSync = new();

    private readonly ushort[] _holdingRegisters;
    private readonly ushort[] _inputRegisters;
    private readonly uint[] _coils;
    private readonly uint[] _discreteInputs;
    private readonly int _coilCount;
    private readonly int _discreteInputCount;
    private int _exceptionStatus;
    private long _eventCount;

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
        _holdingRegisters = new ushort[ValidateSize(holdingRegisters, nameof(holdingRegisters))];
        _inputRegisters = new ushort[ValidateSize(inputRegisters, nameof(inputRegisters))];
        _coilCount = ValidateSize(coils, nameof(coils));
        _discreteInputCount = ValidateSize(discreteInputs, nameof(discreteInputs));
        _coils = new uint[BitBufferLength(_coilCount)];
        _discreteInputs = new uint[BitBufferLength(_discreteInputCount)];
    }

    /// <summary>Raised after holding registers were written, including through a Modbus request.</summary>
    public event EventHandler<ModbusRegisterWriteEventArgs>? HoldingRegistersWritten;

    /// <summary>Raised after input registers were written.</summary>
    public event EventHandler<ModbusRegisterWriteEventArgs>? InputRegistersWritten;

    /// <summary>Raised after coils were written, including through a Modbus request.</summary>
    public event EventHandler<ModbusCoilWriteEventArgs>? CoilsWritten;

    /// <summary>Raised after discrete inputs were written.</summary>
    public event EventHandler<ModbusCoilWriteEventArgs>? DiscreteInputsWritten;

    /// <summary>Gets the number of holding registers.</summary>
    public int HoldingRegisterCount => _holdingRegisters.Length;

    /// <summary>Gets the number of input registers.</summary>
    public int InputRegisterCount => _inputRegisters.Length;

    /// <summary>Gets the number of coils.</summary>
    public int CoilCount => _coilCount;

    /// <summary>Gets the number of discrete inputs.</summary>
    public int DiscreteInputCount => _discreteInputCount;

    /// <summary>
    /// Gets or sets the exception status byte returned by function code 07.
    /// The application updates it to simulate device faults.
    /// </summary>
    public byte ExceptionStatus
    {
        get => (byte)Volatile.Read(ref _exceptionStatus);
        set => Volatile.Write(ref _exceptionStatus, value);
    }

    /// <summary>Gets how many write requests were completed, as reported by function code 11.</summary>
    public long EventCount => Interlocked.Read(ref _eventCount);

    /// <summary>Gets or sets a holding register. Indexes outside the buffer are ignored.</summary>
    /// <param name="index">Register offset.</param>
    public ushort this[int index]
    {
        get
        {
            lock (_holdingSync)
            {
                return index >= 0 && index < _holdingRegisters.Length ? _holdingRegisters[index] : (ushort)0;
            }
        }

        set => TryWriteSingleRegister(index, value);
    }

    /// <summary>Captures a consistent-enough copy of every area for later restore with <see cref="Load"/>.</summary>
    /// <returns>A snapshot that owns its buffers.</returns>
    public ModbusDataStoreSnapshot Snapshot()
    {
        ushort[] holding;
        lock (_holdingSync)
        {
            holding = (ushort[])_holdingRegisters.Clone();
        }

        ushort[] input;
        lock (_inputSync)
        {
            input = (ushort[])_inputRegisters.Clone();
        }

        bool[] coils;
        lock (_coilSync)
        {
            coils = ReadBitsUnlocked(0, _coilCount, _coils);
        }

        bool[] discreteInputs;
        lock (_discreteSync)
        {
            discreteInputs = ReadBitsUnlocked(0, _discreteInputCount, _discreteInputs);
        }

        return new ModbusDataStoreSnapshot(holding, input, coils, discreteInputs);
    }

    /// <summary>Replaces every area with the contents of a snapshot and raises the write events.</summary>
    /// <param name="snapshot">Snapshot taken from a store with the same buffer sizes.</param>
    /// <exception cref="ArgumentException">The snapshot dimensions differ from this store.</exception>
    public void Load(ModbusDataStoreSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));

        if (snapshot.HoldingRegisters.Count != _holdingRegisters.Length
            || snapshot.InputRegisters.Count != _inputRegisters.Length
            || snapshot.Coils.Count != _coilCount
            || snapshot.DiscreteInputs.Count != _discreteInputCount)
        {
            throw new ArgumentException(
                "The snapshot dimensions do not match this store. Create a store with the same sizes before loading.",
                nameof(snapshot));
        }

        WriteRegisters(0, snapshot.HoldingRegisters, _holdingRegisters, _holdingSync, HoldingRegistersWritten);
        WriteRegisters(0, snapshot.InputRegisters, _inputRegisters, _inputSync, InputRegistersWritten);
        WriteCoils(0, snapshot.Coils, _coils, _coilCount, _coilSync, CoilsWritten);
        WriteCoils(0, snapshot.DiscreteInputs, _discreteInputs, _discreteInputCount, _discreteSync, DiscreteInputsWritten);
    }

    /// <summary>Clears every area to zero and raises the write events, simulating a device power cycle.</summary>
    public void Reset()
    {
        WriteRegisters(0, new ushort[_holdingRegisters.Length], _holdingRegisters, _holdingSync, HoldingRegistersWritten);
        WriteRegisters(0, new ushort[_inputRegisters.Length], _inputRegisters, _inputSync, InputRegistersWritten);
        WriteCoils(0, new bool[_coilCount], _coils, _coilCount, _coilSync, CoilsWritten);
        WriteCoils(0, new bool[_discreteInputCount], _discreteInputs, _discreteInputCount, _discreteSync, DiscreteInputsWritten);
        ExceptionStatus = 0;
        Interlocked.Exchange(ref _eventCount, 0);
    }

    internal void RecordEvent() => Interlocked.Increment(ref _eventCount);

    private static bool Fits(int start, int count, int length) =>
        start >= 0 && count >= 0 && (long)start + count <= length;

    private static int BitBufferLength(int count) => (count + 31) / 32;

    private static int ValidateSize(int size, string name) => SimulatorLimits.Validate(size, name);
}
