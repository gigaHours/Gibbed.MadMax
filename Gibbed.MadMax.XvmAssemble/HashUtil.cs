using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    public static class HashUtil
    {
        public static uint HashString(string str)
        {
            return str.HashJenkins();
        }
    }
}
