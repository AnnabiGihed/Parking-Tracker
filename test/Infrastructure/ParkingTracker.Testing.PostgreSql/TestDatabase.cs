namespace ParkingTracker.Testing.PostgreSql;

/// <summary>
/// Author      : Gihed Annabi
/// Date        : 11-2026
/// Purpose     : One database of a test, with the connection strings of the three ways to reach it. The role strings are empty for a
///              database made without roles.
/// </summary>
/// <param name="Name">The name of the database.</param>
/// <param name="AdminConnectionString">The connection string of the superuser of the container.</param>
/// <param name="OwnerConnectionString">The connection string of the owner role, which migrates; empty without roles.</param>
/// <param name="ApiConnectionString">The connection string of the less-privileged role the API serves with; empty without roles.</param>
public sealed record TestDatabase(string Name, string AdminConnectionString, string OwnerConnectionString, string ApiConnectionString);
