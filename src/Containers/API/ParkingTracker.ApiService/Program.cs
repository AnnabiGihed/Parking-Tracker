using ParkingTracker.ApiService.Features.Hosting;

namespace ParkingTracker.ApiService;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : The entry point of the API host. It only wires: <see cref="ApiHostExtensions"/> holds the registrations and the
///              request pipeline, so the host tests can start the same composition.
/// </summary>
public sealed class Program
{
	#region Constructors
	/// <summary>
	/// Initializes a new instance of the <see cref="Program"/> class; private, because the type exists for its entry point and as the anchor of the host tests.
	/// </summary>
	private Program()
	{
	}
	#endregion

	#region Public Methods
	/// <summary>
	/// Builds and runs the API.
	/// </summary>
	/// <param name="args">The command-line arguments.</param>
	public static void Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);
		builder.AddParkingApi();
		var app = builder.Build();
		app.UseParkingApi();
		app.Run();
	}
	#endregion
}
