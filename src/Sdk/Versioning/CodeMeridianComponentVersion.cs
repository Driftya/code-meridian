namespace CodeMeridian.Sdk.Versioning;

public sealed record CodeMeridianComponentVersion(
    string Component,
    string ProductVersion,
    int GraphContractVersion,
    int CacheVersion);
