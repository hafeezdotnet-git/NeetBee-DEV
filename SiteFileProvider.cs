using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace NeetBee.Web;

/// <summary>
/// Serves only the public parts of the NEET BEE static site. Everything else under the site root
/// (backend source, appsettings, "Other HTML Templages", the solution itself) is reported as not found.
/// </summary>
public sealed class SiteFileProvider : IFileProvider
{
    private static readonly string[] AllowedDirectories = { "css", "js", "assets", "login" };

    private readonly PhysicalFileProvider _inner;

    public SiteFileProvider(string root)
    {
        _inner = new PhysicalFileProvider(root);
    }

    public IFileInfo GetFileInfo(string subpath) =>
        IsAllowed(subpath) ? _inner.GetFileInfo(subpath) : new NotFoundFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => _inner.Watch(filter);

    private static bool IsAllowed(string subpath)
    {
        var path = subpath.Replace('\\', '/').TrimStart('/');

        if (path.Length == 0 || path.Contains("..", StringComparison.Ordinal))
            return false;

        var slash = path.IndexOf('/');
        if (slash < 0)
            return path.EndsWith(".html", StringComparison.OrdinalIgnoreCase);

        var first = path[..slash];
        return AllowedDirectories.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    IChangeToken IFileProvider.Watch(string filter)
    {
        throw new NotImplementedException();
    }
}
