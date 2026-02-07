using Gibbed.MadMax.FileFormats;

namespace Gibbed.MadMax.XvmAssemble
{
    internal static class HashUtil
    {
        public static uint HashString(string str)
        {
            return str.HashJenkins();
        }

        public static uint HashBytes(byte[] data)
        {
            // Jenkins lookup3 (hashlittle) with seed=0, matching StringHelpers.HashJenkins
            uint a, b, c;
            a = b = c = 0xDEADBEEF + (uint)data.Length + 0;

            int i = 0;
            int length = data.Length;
            while (i + 12 < length)
            {
                a += (uint)data[i++] |
                     ((uint)data[i++] << 8) |
                     ((uint)data[i++] << 16) |
                     ((uint)data[i++] << 24);
                b += (uint)data[i++] |
                     ((uint)data[i++] << 8) |
                     ((uint)data[i++] << 16) |
                     ((uint)data[i++] << 24);
                c += (uint)data[i++] |
                     ((uint)data[i++] << 8) |
                     ((uint)data[i++] << 16) |
                     ((uint)data[i++] << 24);

                a -= c; a ^= (c << 4) | (c >> 28); c += b;
                b -= a; b ^= (a << 6) | (a >> 26); a += c;
                c -= b; c ^= (b << 8) | (b >> 24); b += a;
                a -= c; a ^= (c << 16) | (c >> 16); c += b;
                b -= a; b ^= (a << 19) | (a >> 13); a += c;
                c -= b; c ^= (b << 4) | (b >> 28); b += a;
            }

            if (i < length) a += data[i++];
            if (i < length) a += (uint)data[i++] << 8;
            if (i < length) a += (uint)data[i++] << 16;
            if (i < length) a += (uint)data[i++] << 24;
            if (i < length) b += (uint)data[i++];
            if (i < length) b += (uint)data[i++] << 8;
            if (i < length) b += (uint)data[i++] << 16;
            if (i < length) b += (uint)data[i++] << 24;
            if (i < length) c += (uint)data[i++];
            if (i < length) c += (uint)data[i++] << 8;
            if (i < length) c += (uint)data[i++] << 16;
            if (i < length) c += (uint)data[i] << 24;

            // Jenkins lookup3: if length == 0 (and no tail bytes processed),
            // skip final mix and return initial c value (0xDEADBEEF for seed=0)
            if (data.Length == 0)
            {
                return c;
            }

            c ^= b; c -= (b << 14) | (b >> 18);
            a ^= c; a -= (c << 11) | (c >> 21);
            b ^= a; b -= (a << 25) | (a >> 7);
            c ^= b; c -= (b << 16) | (b >> 16);
            a ^= c; a -= (c << 4) | (c >> 28);
            b ^= a; b -= (a << 14) | (a >> 18);
            c ^= b; c -= (b << 24) | (b >> 8);

            return c;
        }
    }
}
