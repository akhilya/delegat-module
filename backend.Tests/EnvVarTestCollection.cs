namespace Delegat.Tests;

/// <summary>
/// Serializes tests that mutate process environment (quota env vars).
/// </summary>
[CollectionDefinition("EnvVars", DisableParallelization = true)]
public sealed class EnvVarTestCollection;
