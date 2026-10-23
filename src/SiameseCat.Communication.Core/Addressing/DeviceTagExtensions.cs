using SiameseCat.Communication.Drivers;

namespace SiameseCat.Communication;

/// <summary>Reads tags through an <see cref="IDeviceDriver"/>.</summary>
public static class DeviceTagExtensions
{
    /// <summary>Reads the value of a tag using the driver's address parser.</summary>
    /// <param name="driver">Connected driver.</param>
    /// <param name="tag">Tag to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<DeviceValue> ReadTagAsync(
        this IDeviceDriver driver,
        DeviceTag tag,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(driver, nameof(driver));
        Guard.NotNull(tag, nameof(tag));

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
    public static async Task<DeviceValue[]> ReadTagsAsync(
        this IDeviceDriver driver,
        IReadOnlyList<DeviceTag> tags,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(driver, nameof(driver));
        Guard.NotNull(tags, nameof(tags));

        var addresses = new DeviceAddress[tags.Count];
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

        var values = new DeviceValue[addresses.Length];
        for (var i = 0; i < addresses.Length; i++)
        {
            values[i] = await readable.ReadAsync(addresses[i], cancellationToken).ConfigureAwait(false);
        }

        return values;
    }
}
