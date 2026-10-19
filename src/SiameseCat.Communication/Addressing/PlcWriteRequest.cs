namespace SiameseCat.Communication;

/// <summary>One address/value pair in a batch write.</summary>
/// <param name="Address">Target address.</param>
/// <param name="Value">Value to write.</param>
public readonly record struct PlcWriteRequest(PlcAddress Address, PlcValue Value);
