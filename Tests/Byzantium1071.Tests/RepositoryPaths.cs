using System;
using System.IO;

namespace Byzantium1071.Tests
{
    internal static class RepositoryPaths
    {
        private static readonly Lazy<string> RepositoryRoot = new(FindRepositoryRoot);

        internal static string FromRoot(params string[] pathSegments)
        {
            string[] segments = new string[pathSegments.Length + 1];
            segments[0] = RepositoryRoot.Value;
            Array.Copy(pathSegments, 0, segments, 1, pathSegments.Length);
            return Path.Combine(segments);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not find the repository root.");
        }
    }
}
