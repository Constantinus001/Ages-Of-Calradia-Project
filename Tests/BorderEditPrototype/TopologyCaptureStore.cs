using System;
using System.IO;
using System.Text;

namespace Aoc.BorderEditPrototype
{
    // Persists the complete, hash-pinned renderer observation. This is diagnostic
    // evidence only: it neither owns nor changes native frontier entities.
    internal static class TopologyCaptureStore
    {
        internal static string Write(
            string root,
            string rendererHash,
            string layoutIdentity,
            TopologyFingerprint topology)
        {
            if (string.IsNullOrWhiteSpace(root)
                || !IsSha256(rendererHash)
                || !IsSha256(layoutIdentity)
                || topology == null
                || !IsSha256(topology.Signature)
                || topology.Tokens == null
                || topology.Tokens.Length == 0)
                throw new ArgumentException("Topology capture input is incomplete.");

            foreach (string token in topology.Tokens)
                if (string.IsNullOrWhiteSpace(token)
                    || token.IndexOf('\r') >= 0
                    || token.IndexOf('\n') >= 0)
                    throw new ArgumentException("Topology capture token is invalid.");

            Directory.CreateDirectory(root);
            string path = Path.Combine(root, topology.Signature + ".topology");
            string contents = Contents(rendererHash, layoutIdentity, topology);
            if (File.Exists(path))
            {
                if (string.Equals(File.ReadAllText(path, Encoding.UTF8), contents, StringComparison.Ordinal))
                    return path;
                throw new InvalidDataException("Topology capture signature collision.");
            }

            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, contents, new UTF8Encoding(false));
                File.Move(temporary, path);
                return path;
            }
            catch (IOException)
            {
                if (File.Exists(path)
                    && string.Equals(File.ReadAllText(path, Encoding.UTF8), contents, StringComparison.Ordinal))
                    return path;
                throw;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string Contents(string rendererHash, string layoutIdentity, TopologyFingerprint topology)
        {
            var output = new StringBuilder();
            output.AppendLine("format=1");
            output.AppendLine("rendererHash=" + rendererHash);
            output.AppendLine("layoutIdentity=" + layoutIdentity);
            output.AppendLine("topologySignature=" + topology.Signature);
            output.AppendLine("tokens=" + topology.Tokens.Length);
            foreach (string token in topology.Tokens) output.AppendLine(token);
            return output.ToString();
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char character in value)
                if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
    }
}
