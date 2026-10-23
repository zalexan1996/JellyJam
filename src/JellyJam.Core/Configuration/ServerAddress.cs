namespace JellyJam.Core.Configuration;

public static class ServerAddress
{
    public static string Normalize(string address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Enter an HTTP(S) server URL without credentials, a query, or a fragment.");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }
}
