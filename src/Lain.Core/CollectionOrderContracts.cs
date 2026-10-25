namespace Lain.Core;

/// <summary>A full, unique permutation of all global collection IDs. Does not change membership or routers.</summary>
public sealed record CollectionOrderRequest(string[] CollectionIds);
