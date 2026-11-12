using Byzantium1071.Campaign.Settings;

namespace Byzantium1071.Campaign
{
    internal static class B1071_TestHooks
    {
        internal static IB1071Settings? Settings { get; set; }
        internal static IB1071Random? Random { get; set; }

        internal static void Reset()
        {
            Settings = null;
            Random = null;
        }
    }
}
