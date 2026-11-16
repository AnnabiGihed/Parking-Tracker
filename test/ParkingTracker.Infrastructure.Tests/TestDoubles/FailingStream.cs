namespace ParkingTracker.Infrastructure.Tests.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A readable stream that fails with an <see cref="IOException"/> on the first read, as a connection that drops
///              while a body is being received.
/// </summary>
internal sealed class FailingStream : Stream
{
	#region Properties
	/// <inheritdoc />
	public override bool CanRead => true;

	/// <inheritdoc />
	public override bool CanSeek => false;

	/// <inheritdoc />
	public override bool CanWrite => false;

	/// <inheritdoc />
	public override long Length => throw new NotSupportedException();

	/// <inheritdoc />
	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection dropped");

	/// <inheritdoc />
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("connection dropped");

	/// <inheritdoc />
	public override void Flush()
	{
	}

	/// <inheritdoc />
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

	/// <inheritdoc />
	public override void SetLength(long value) => throw new NotSupportedException();

	/// <inheritdoc />
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	#endregion
}
