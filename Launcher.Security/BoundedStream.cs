namespace Launcher.Security;

public static class BoundedStream
{
    public static async Task CopyAsync(Stream source, Stream destination, long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[32 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidDataException("The response exceeds the allowed size.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
