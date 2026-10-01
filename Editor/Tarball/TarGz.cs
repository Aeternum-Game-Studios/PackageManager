using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Aeternum.Packages
{
    /// <summary>
    /// Writes a package tarball the way npm does (files only, under package/), with fixed
    /// metadata: the same files always give the same bytes, whoever generates the tarball.
    /// </summary>
    internal static class TarGz
    {
        const int BlockSize = 512;

        // npm's fixed timestamp, 1985-10-26T08:15:00Z.
        const long FixedModifiedTime = 499162500;

        public static void Write(string destination, IReadOnlyDictionary<string, byte[]> files)
        {
            using (var fileStream = File.Create(destination))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                foreach (var path in files.Keys.OrderBy(p => p, StringComparer.Ordinal))
                    WriteFile(gzip, "package/" + path, files[path]);
                gzip.Write(new byte[BlockSize * 2], 0, BlockSize * 2);
            }
        }

        static void WriteFile(Stream stream, string path, byte[] data)
        {
            if (!TrySplitUstarName(path, out var name, out var prefix))
            {
                // A pax header carries a name ustar can't hold: too long, or not ASCII.
                WriteEntry(stream, "PaxHeader/" + Truncate(path, 80), "", 'x', PaxRecord("path", path));
                name = Truncate(path, 100);
                prefix = "";
            }
            WriteEntry(stream, name, prefix, '0', data);
        }

        static void WriteEntry(Stream stream, string name, string prefix, char type, byte[] data)
        {
            var header = new byte[BlockSize];
            WriteText(header, 0, 100, name);
            WriteOctal(header, 100, 8, Convert.ToInt64("644", 8));
            WriteOctal(header, 108, 8, 0);
            WriteOctal(header, 116, 8, 0);
            WriteOctal(header, 124, 12, data.LongLength);
            WriteOctal(header, 136, 12, FixedModifiedTime);
            header[156] = (byte)type;
            WriteText(header, 257, 6, "ustar");
            WriteText(header, 263, 2, "00");
            WriteText(header, 345, 155, prefix);

            for (var i = 148; i < 156; i++)
                header[i] = (byte)' ';
            long checksum = 0;
            foreach (var b in header)
                checksum += b;
            WriteText(header, 148, 6, Convert.ToString(checksum, 8).PadLeft(6, '0'));
            header[154] = 0;
            header[155] = (byte)' ';

            stream.Write(header, 0, header.Length);
            stream.Write(data, 0, data.Length);
            var padding = (BlockSize - data.Length % BlockSize) % BlockSize;
            if (padding > 0)
                stream.Write(new byte[padding], 0, padding);
        }

        static bool TrySplitUstarName(string path, out string name, out string prefix)
        {
            name = path;
            prefix = "";
            if (path.Any(c => c < 0x20 || c > 0x7e))
                return false;
            if (path.Length <= 100)
                return true;
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                if (slash > 155)
                    break;
                if (path.Length - slash - 1 <= 100)
                {
                    prefix = path.Substring(0, slash);
                    name = path.Substring(slash + 1);
                    return true;
                }
            }
            return false;
        }

        static byte[] PaxRecord(string key, string value)
        {
            // "<length> <key>=<value>\n", where <length> counts the whole record, itself included.
            var body = Encoding.UTF8.GetByteCount($" {key}={value}\n");
            var length = body + 1;
            while (length != body + length.ToString().Length)
                length = body + length.ToString().Length;
            return Encoding.UTF8.GetBytes($"{length} {key}={value}\n");
        }

        static string Truncate(string text, int maxLength)
        {
            var ascii = new string(text.Select(c => c < 0x20 || c > 0x7e ? '_' : c).ToArray());
            return ascii.Length <= maxLength ? ascii : ascii.Substring(ascii.Length - maxLength);
        }

        static void WriteText(byte[] header, int offset, int length, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, header, offset, Math.Min(bytes.Length, length));
        }

        static void WriteOctal(byte[] header, int offset, int length, long value)
        {
            WriteText(header, offset, length - 1, Convert.ToString(value, 8).PadLeft(length - 1, '0'));
        }
    }
}
