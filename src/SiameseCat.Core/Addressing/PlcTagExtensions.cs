namespace SiameseCat.Core;

/// <summary>Reads tags through an <see cref="IPlcDriver"/>.</summary>
public static class PlcTagExtensions
{
    /// <summary>Reads the value of a tag using the driver's address parser.</summary>
    /// <param name="driver">Connected driver.</param>
    /// <param name="tag">Tag to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<PlcValue> ReadTagAsync(
        this IPlcDriver driver,
        PlcTag tag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(tag);

        if (driver is not IReadable readable)
        {
            throw new NotSupportedException($"Driver '{driver.Descriptor.Id}' does not support reading.");
        }

        return readable.ReadAsync(driver.AddressParser.Parse(tag.Address, tag.DataType), cancellationToken);
    }

    /// <summary>Reads several tags, using native batch reads when the driver supports them.</summary>
    /// <param name="driver">Connected driver.</param>
    /// <param name="tags">Tags to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<PlcValue[]> ReadTagsAsync(
        this IPlcDriver driver,
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(tags);

        var addresses = new PlcAddress[tags.Count];
        for (var i = 0; i < tags.Count; i++)
        {
            addresses[i] = driver.AddressParser.Parse(tags[i].Address, tags[i].DataType);
        }

        if (driver is IBatchReadable batch)
        {
            return await batch.ReadBatchAsync(addresses, cancellationToken).ConfigureAwait(false);
        }

        if (driver is not IReadable readable)
        {
            throw new NotSupportedException($"Driver '{driver.Descriptor.Id}' does not support reading.");
        }

        var values = new PlcValue[addresses.Length];
        for (var i = 0; i < addresses.Length; i++)
        {
            values[i] = await readable.ReadAsync(addresses[i], cancellationToken).ConfigureAwait(false);
        }

        return values;
    }
}
