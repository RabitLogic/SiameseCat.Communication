using SiameseCat.Server;

namespace SiameseCat.Tests.Server;

public class ModbusDataStoreTests
{
    [Fact]
    public void WriteHoldingRegisters_RaisesEventWithPreviousValues()
    {
        var store = new ModbusDataStore();
        var initial = new ushort[] { 1, 2, 3 };
        var written = new ushort[] { 20, 30 };
        store.TryWriteHoldingRegisters(0, initial);

        ModbusRegisterWriteEventArgs? captured = null;
        store.HoldingRegistersWritten += (_, args) => captured = args;

        Assert.True(store.TryWriteHoldingRegisters(1, written));

        var expectedPrevious = new ushort[] { 2, 3 };
        var expectedAll = new ushort[] { 1, 20, 30 };
        Assert.NotNull(captured);
        Assert.Equal(1, captured!.Start);
        Assert.Equal(written, captured.Values);
        Assert.Equal(expectedPrevious, captured.PreviousValues);
        Assert.Equal(expectedAll, store.ReadHoldingRegisters(0, 3));
    }

    [Fact]
    public void WriteCoils_RaisesEventOnlyForItsOwnArea()
    {
        var store = new ModbusDataStore();
        var coilEvents = 0;
        var discreteEvents = 0;
        store.CoilsWritten += (_, _) => coilEvents++;
        store.DiscreteInputsWritten += (_, _) => discreteEvents++;

        var pair = new[] { true, true };
        var single = new[] { true };
        store.TryWriteCoils(31, pair);
        store.TryWriteCoils(63, single);

        Assert.Equal(2, coilEvents);
        Assert.Equal(0, discreteEvents);
        Assert.Equal(pair, store.ReadCoils(31, 2));
        Assert.Equal(single, store.ReadCoils(63, 1));
        Assert.False(store.ReadCoils(62, 1)![0]);
    }

    [Fact]
    public void WriteOutsideBuffer_IsRejectedWithoutEvent()
    {
        var store = new ModbusDataStore(coils: 32);
        var raised = 0;
        store.CoilsWritten += (_, _) => raised++;

        var tooMany = new[] { true, true };
        var single = new[] { true };
        Assert.False(store.TryWriteCoils(31, tooMany));
        Assert.Null(store.ReadCoils(31, 2));

        Assert.True(store.TryWriteCoils(31, single));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Indexer_Set_RaisesEvent()
    {
        var store = new ModbusDataStore();
        ModbusRegisterWriteEventArgs? captured = null;
        store.HoldingRegistersWritten += (_, args) => captured = args;

        store[7] = 99;

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.Start);
        Assert.Equal((ushort)99, store[7]);
    }

    [Fact]
    public void SnapshotAndLoad_RoundTrip()
    {
        var source = new ModbusDataStore(holdingRegisters: 40, inputRegisters: 2, coils: 40, discreteInputs: 8);
        var registers = new ushort[] { 5, 6, 7, 8 };
        var inputs = new ushort[] { 11, 12 };
        var coils = new[] { true, false, true };
        var high = new[] { true };
        var discreteInputs = new[] { false, true };
        source.TryWriteHoldingRegisters(0, registers);
        source.TryWriteInputRegisters(0, inputs);
        source.TryWriteCoils(0, coils);
        source.TryWriteCoils(32, high);
        source.TryWriteDiscreteInputs(0, discreteInputs);

        var target = new ModbusDataStore(holdingRegisters: 40, inputRegisters: 2, coils: 40, discreteInputs: 8);
        target.Load(source.Snapshot());

        Assert.Equal(source.ReadHoldingRegisters(0, 40), target.ReadHoldingRegisters(0, 40));
        Assert.Equal(source.ReadInputRegisters(0, 2), target.ReadInputRegisters(0, 2));
        Assert.Equal(source.ReadCoils(0, 40), target.ReadCoils(0, 40));
        Assert.Equal(source.ReadDiscreteInputs(0, 8), target.ReadDiscreteInputs(0, 8));
    }

    [Fact]
    public void Load_RaisesEvents()
    {
        var source = new ModbusDataStore();
        var initial = new ushort[] { 1 };
        source.TryWriteHoldingRegisters(0, initial);
        var snapshot = source.Snapshot();

        var target = new ModbusDataStore();
        var raised = 0;
        target.HoldingRegistersWritten += (_, _) => raised++;

        target.Load(snapshot);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Load_WithDifferentDimensions_Throws()
    {
        var snapshot = new ModbusDataStore(holdingRegisters: 4).Snapshot();
        var target = new ModbusDataStore(holdingRegisters: 8);

        Assert.Throws<ArgumentException>(() => target.Load(snapshot));
    }

    [Fact]
    public void Reset_ClearsEveryArea()
    {
        var store = new ModbusDataStore(holdingRegisters: 4, inputRegisters: 2, coils: 8, discreteInputs: 2);
        var registers = new ushort[] { 1, 2, 3, 4 };
        var inputs = new ushort[] { 5, 6 };
        var bits = new[] { true, true };
        store.TryWriteHoldingRegisters(0, registers);
        store.TryWriteInputRegisters(0, inputs);
        store.TryWriteCoils(0, bits);
        store.TryWriteDiscreteInputs(0, bits);

        store.Reset();

        var emptyRegisters = new ushort[] { 0, 0, 0, 0 };
        var emptyInputs = new ushort[] { 0, 0 };
        var emptyBits = new[] { false, false };
        Assert.Equal(emptyRegisters, store.ReadHoldingRegisters(0, 4));
        Assert.Equal(emptyInputs, store.ReadInputRegisters(0, 2));
        Assert.Equal(emptyBits, store.ReadCoils(0, 2));
        Assert.Equal(emptyBits, store.ReadDiscreteInputs(0, 2));
    }

    [Fact]
    public void InvalidSizes_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusDataStore(coils: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModbusDataStore(discreteInputs: 65537));
    }
}
