using System;
using System.Globalization;

namespace Aeternum.Packages
{
    /// <summary>A semantic version (major.minor.patch, optional -prerelease and +build), ordered as SemVer 2.0.</summary>
    internal readonly struct SemVersion : IComparable<SemVersion>
    {
        public readonly int Major;
        public readonly int Minor;
        public readonly int Patch;
        public readonly string Prerelease;
        readonly string text;

        SemVersion(int major, int minor, int patch, string prerelease, string text)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            this.text = text;
        }

        public static bool TryParse(string value, out SemVersion version)
        {
            version = default;
            if (string.IsNullOrEmpty(value))
                return false;

            var core = value;
            var plus = core.IndexOf('+');
            if (plus >= 0)
                core = core.Substring(0, plus);
            string prerelease = null;
            var dash = core.IndexOf('-');
            if (dash >= 0)
            {
                prerelease = core.Substring(dash + 1);
                core = core.Substring(0, dash);
                if (prerelease.Length == 0)
                    return false;
            }

            var parts = core.Split('.');
            if (parts.Length != 3 ||
                !TryParsePart(parts[0], out var major) ||
                !TryParsePart(parts[1], out var minor) ||
                !TryParsePart(parts[2], out var patch))
                return false;

            version = new SemVersion(major, minor, patch, prerelease, value);
            return true;
        }

        static bool TryParsePart(string part, out int number)
        {
            number = 0;
            return part.Length > 0 && part.Trim() == part &&
                   int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        public int CompareTo(SemVersion other)
        {
            var result = Major.CompareTo(other.Major);
            if (result == 0) result = Minor.CompareTo(other.Minor);
            if (result == 0) result = Patch.CompareTo(other.Patch);
            if (result != 0)
                return result;

            // A prerelease sorts before its release.
            if (Prerelease == null || other.Prerelease == null)
                return Prerelease == null ? (other.Prerelease == null ? 0 : 1) : -1;

            var mine = Prerelease.Split('.');
            var theirs = other.Prerelease.Split('.');
            for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
            {
                var mineIsNumber = int.TryParse(mine[i], NumberStyles.None, CultureInfo.InvariantCulture, out var mineNumber);
                var theirsIsNumber = int.TryParse(theirs[i], NumberStyles.None, CultureInfo.InvariantCulture, out var theirsNumber);
                if (mineIsNumber && theirsIsNumber)
                    result = mineNumber.CompareTo(theirsNumber);
                else if (mineIsNumber != theirsIsNumber)
                    result = mineIsNumber ? -1 : 1;
                else
                    result = string.CompareOrdinal(mine[i], theirs[i]);
                if (result != 0)
                    return result;
            }
            return mine.Length.CompareTo(theirs.Length);
        }

        public override string ToString() => text ?? $"{Major}.{Minor}.{Patch}";
    }
}
