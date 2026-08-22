using System;
using System.IO;
using Xunit;

namespace Byzantium1071.Tests
{
    public sealed class MathSourceIsolationTests
    {
        [Fact]
        public void MathSourcesDoNotReferenceTaleWorlds()
        {
            string repositoryRoot = FindRepositoryRoot();
            string mathDirectory = Path.Combine(repositoryRoot, "Campaign", "Math");

            Assert.True(Directory.Exists(mathDirectory), $"Missing pure source directory: {mathDirectory}");

            foreach (string sourceFile in Directory.EnumerateFiles(mathDirectory, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(sourceFile);
                Assert.False(
                    source.Contains("TaleWorlds", StringComparison.Ordinal),
                    $"{sourceFile} must remain independent from TaleWorlds.");
            }
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
