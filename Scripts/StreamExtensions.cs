using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

public static class StreamExtensions
{
    public static async Task CopyToAsync(this Stream source, Stream destination, int bufferSize, IProgress<long> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Has to be readable", nameof(source));
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Has to be writable", nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegative(bufferSize);

        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
        {
            await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
            totalBytesRead += bytesRead;
            progress?.Report(totalBytesRead);
        }
    }
}

public class FileAccessStream : Stream
{
    private readonly FileAccess _fileAccess;
    private readonly long _fileLength;
    private long _position;

    public FileAccess.ModeFlags ModeFlags { get; private init; }

    public const int BufferSize = 1048576; // 1MB. Adjust this as needed for performance.

    /// <summary>
    /// Initializes a new instance of the <see cref="FileAccessStream"/> class.
    /// Opens the file at the specified path using the given mode flags.
    /// </summary>
    /// <param name="path">The path to the file to open.</param>
    /// <param name="modeFlags">The mode flags specifying how the file should be opened.</param>
    /// <exception cref="IOException">Thrown when the file cannot be opened.</exception>
    public FileAccessStream(string path, FileAccess.ModeFlags modeFlags)
    {
        _fileAccess = FileAccess.Open(path, modeFlags) ?? throw new IOException("Failed to open file.");
        _fileLength = (long)_fileAccess.GetLength();
        _position = 0;
        ModeFlags = modeFlags;
    }

    /// <summary>
    /// Gets a value indicating whether the current stream supports reading.
    /// Always returns true because FileAccess supports reading.
    /// </summary>
    public override bool CanRead => (ModeFlags & FileAccess.ModeFlags.Read) != 0;

    /// <summary>
    /// Gets a value indicating whether the current stream supports seeking.
    /// Always returns true because FileAccess supports seeking.
    /// </summary>
    public override bool CanSeek => true;

    /// <summary>
    /// Gets a value indicating whether the current stream supports writing.
    /// Returns true if the FileAccess mode includes writing.
    /// </summary>
    public override bool CanWrite => (ModeFlags & FileAccess.ModeFlags.Write) != 0;

    /// <summary>
    /// Gets the length of the file stream in bytes.
    /// </summary>
    public override long Length => _fileLength;

    /// <summary>
    /// Gets or sets the position within the current stream.
    /// </summary>
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    /// Flushes any buffered data to the file.
    /// Calls the FileAccess.Flush method.
    /// </summary>
    public override void Flush()
    {
        try
        {
            _fileAccess.Flush();
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to flush the file.", ex);
        }
    }

    /// <summary>
    /// Reads a sequence of bytes from the current stream and advances the position within the stream by the number of bytes read.
    /// </summary>
    /// <param name="buffer">An array of bytes. When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
    /// <param name="count">The maximum number of bytes to be read from the current stream.</param>
    /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero if the end of the stream has been reached.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        if (buffer.Length - offset < count) throw new ArgumentException("The buffer is too small.");

        if (_position >= _fileLength) return 0;

        int bytesRead = 0;
        try
        {
            while (count > 0)
            {
                int bytesToRead = (int)Math.Min(count, BufferSize);
                byte[] chunk = _fileAccess.GetBuffer(bytesToRead);
                if (chunk.Length == 0) break;

                Array.Copy(chunk, 0, buffer, offset, chunk.Length);
                offset += chunk.Length;
                count -= chunk.Length;
                bytesRead += chunk.Length;
                _position += chunk.Length;
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to read from the file.", ex);
        }

        return bytesRead;
    }

    /// <summary>
    /// Sets the position within the current stream.
    /// </summary>
    /// <param name="offset">A byte offset relative to the origin parameter.</param>
    /// <param name="origin">A value of type SeekOrigin indicating the reference point used to obtain the new position.</param>
    /// <returns>The new position within the current stream.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition;
        switch (origin)
        {
            case SeekOrigin.Begin:
                newPosition = offset;
                break;
            case SeekOrigin.Current:
                newPosition = _position + offset;
                break;
            case SeekOrigin.End:
                newPosition = _fileLength + offset;
                break;
            default:
                throw new ArgumentException("Invalid seek origin.", nameof(origin));
        }

        if (newPosition < 0) throw new IOException("Cannot seek to a negative position.");
        if (newPosition > _fileLength) throw new IOException("Cannot seek beyond the end of the stream.");

        try
        {
            _fileAccess.Seek((ulong)newPosition);
            _position = newPosition;
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to seek in the file.", ex);
        }

        return _position;
    }

    /// <summary>
    /// Sets the length of the current stream.
    /// Always throws NotSupportedException because FileAccessStream does not support setting the length.
    /// </summary>
    /// <param name="value">The desired length of the current stream in bytes.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Writes a sequence of bytes to the current stream and advances the current position within this stream by the number of bytes written.
    /// </summary>
    /// <param name="buffer">An array of bytes. This method copies count bytes from buffer to the current stream.</param>
    /// <param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream.</param>
    /// <param name="count">The number of bytes to be written to the current stream.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        if (buffer.Length - offset < count) throw new ArgumentException("The buffer is too small.");

        try
        {
            while (count > 0)
            {
                int bytesToWrite = (int)Math.Min(count, BufferSize);
                byte[] chunk = new byte[bytesToWrite];
                Array.Copy(buffer, offset, chunk, 0, bytesToWrite);

                _fileAccess.StoreBuffer(chunk);
                offset += bytesToWrite;
                count -= bytesToWrite;
                _position += bytesToWrite;
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to write to the file.", ex);
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="FileAccessStream"/> and optionally releases the managed resources.
    /// Closes the file by calling FileAccess.Close().
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileAccess?.Close();
        }
        base.Dispose(disposing);
    }
}