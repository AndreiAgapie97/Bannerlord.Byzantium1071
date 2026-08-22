using System.IO;
using Xunit;

namespace Byzantium1071.GameTests
{
    public sealed class GameTestEnvironmentTests
    {
        [Fact]
        public void MissingGameInstallationDoesNotBlockTheGameTestProject()
        {
            const string gameExecutable =
                @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\Bannerlord.exe";

            if (!File.Exists(gameExecutable))
            {
                return;
            }

            Assert.True(File.Exists(gameExecutable));
        }
    }
}
