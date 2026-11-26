namespace ParkingTracker.Infrastructure.Tests.Features.Shared.TestDoubles;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : A read-only stream that cannot seek or tell its length, as a network or pipe stream, so a test can prove that a body which
///              cannot be read twice is still sent once in full.
/// </summary>
internal sealed class ForwardOnlyStream : Stream
{
	#region Fields
	/// <summary>
	/// The bytes the stream gives, read once from start to end.
	/// </summary>
	private readonly MemoryStream inner;
	#endregion

	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="ForwardOnlyStream"/> class.
	/// </summary>
	/// <param name="bytes">The bytes the stream gives.</param>
	public ForwardOnlyStream(byte[] bytes)
	{
		inner = new MemoryStream(bytes, writable: false);
	}
	#endregion

	#region Properties
	/// <inheritdoc />
	public override bool CanRead => true;

	/// <inheritdoc />
	public override bool CanSeek => false;

	/// <inheritdoc />
	public override bool CanWrite => false;

	/// <inheritdoc />
	public override long Length => throw new NotSupportedException("A forward-only stream has no known length.");

	/// <inheritdoc />
	public override long Position
	{
		get => throw new NotSupportedException("A forward-only stream has no position.");
		set => throw new NotSupportedException("A forward-only stream cannot seek.");
	}
	#endregion

	#region Overrides
	/// <inheritdoc />
	public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

	/// <inheritdoc />
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("A forward-only stream cannot seek.");

	/// <inheritdoc />
	public override void SetLength(long value) => throw new NotSupportedException("A forward-only stream cannot change its length.");

	/// <inheritdoc />
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("A forward-only stream is read-only.");

	/// <inheritdoc />
	public override void Flush()
	{
		// Nothing is written, so there is nothing to flush.
	}

	/// <inheritdoc />
	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			inner.Dispose();
		}

		base.Dispose(disposing);
	}
	#endregion
}
