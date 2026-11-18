using ParkingTracker.Application.Features.Shared.Abstractions;
using ParkingTracker.Domain.Features.ResidentCards.Aggregates;
using Pivot.Framework.Domain.Shared;

namespace ParkingTracker.Application.Features.ResidentCards.Abstractions;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : Port to the residents' card on the device. The phone holds one card, so the port loads "the" card and a change
///              replaces whatever is stored; the card keeps its identifier across loads. Changes are staged in the
///              <see cref="ParkingTracker.Application.Features.Shared.Abstractions.IDeviceChangeScope"/> of the operation and applied
///              when the scope commits.
/// </summary>
public interface IResidentCardRepository
{
	#region Methods
	/// <summary>
	/// Loads the card, rebuilt through <see cref="ResidentCard.Restore"/> so that a stored card that breaks a rule is refused.
	/// </summary>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>A task with the card; the failure <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.NotStored"/>
	/// when none is stored; <see cref="ParkingTracker.Application.Features.Shared.Abstractions.DeviceDataErrors.KeyUnavailable"/> when the
	/// key cannot be read now (the card may exist); or another failure when a field cannot be decrypted, the stored card breaks a rule
	/// or the database fails. A card is never returned with fields left out.</returns>
	Task<Result<ResidentCard>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages a new card for the next commit; nothing happens at commit when a card is stored already, so that a newer card is never overwritten.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="card">The card to store.</param>
	/// <param name="cancellationToken">Cancels the encryption of its fields.</param>
	/// <returns>A task with success, or a failure when the fields cannot be encrypted.</returns>
	Task<Result> AddAsync(IDeviceChangeScope scope, ResidentCard card, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the replacement of the stored card by <paramref name="card"/> for the next commit.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	/// <param name="card">The card that replaces the stored one.</param>
	/// <param name="cancellationToken">Cancels the encryption of its fields.</param>
	/// <returns>A task with success, or a failure when the fields cannot be encrypted.</returns>
	Task<Result> UpdateAsync(IDeviceChangeScope scope, ResidentCard card, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stages the removal of the stored card for the next commit; nothing happens at commit when none is stored.
	/// </summary>
	/// <param name="scope">The scope of the operation.</param>
	void Remove(IDeviceChangeScope scope);
	#endregion
}
