using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Aeternum.Packages
{
    /// <summary>
    /// The GitHub credential Git already has on this machine (Git Credential Manager on Windows),
    /// read with <c>git credential fill</c>. It lives in memory for one operation only: it's never
    /// written to a file, EditorPrefs or the log.
    /// </summary>
    internal sealed class GitCredential
    {
        public string Username { get; }

        /// <summary>The token. Only <see cref="GitHubClient"/> reads it, to build the Authorization header.</summary>
        internal string Secret { get; }

        GitCredential(string username, string secret)
        {
            Username = username;
            Secret = secret;
        }

        public override string ToString() => $"GitHub credential for {Username}";

        /// <summary>
        /// Asks Git for the github.com credential of <paramref name="username"/> (any account when
        /// empty). Returns null when Git has none. Unless <paramref name="interactive"/>, Git
        /// Credential Manager is told never to show a sign-in window.
        /// </summary>
        public static async Task<GitCredential> FillAsync(string username, bool interactive)
        {
            var query = new StringBuilder("protocol=https\nhost=github.com\n");
            if (!string.IsNullOrEmpty(username))
                query.Append("username=").Append(username).Append('\n');
            query.Append('\n');

            var timeout = interactive ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(30);
            var result = await Task.Run(() => RunGit("credential fill", query.ToString(), interactive, timeout));
            if (result.ExitCode != 0)
                return null;

            string user = null, secret = null;
            foreach (var line in result.Output.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.StartsWith("username=", StringComparison.Ordinal))
                    user = trimmed.Substring("username=".Length);
                else if (trimmed.StartsWith("password=", StringComparison.Ordinal))
                    secret = trimmed.Substring("password=".Length);
            }
            if (string.IsNullOrEmpty(secret))
                return null;
            return new GitCredential(string.IsNullOrEmpty(user) ? username : user, secret);
        }

        /// <summary>Tells Git the credential worked, so Git Credential Manager keeps it in the Windows credential store.</summary>
        public Task ApproveAsync()
        {
            var input = $"protocol=https\nhost=github.com\nusername={Username}\npassword={Secret}\n\n";
            return Task.Run(() => RunGit("credential approve", input, false, TimeSpan.FromSeconds(30)));
        }

        readonly struct GitResult
        {
            public readonly int ExitCode;
            public readonly string Output;

            public GitResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }
        }

        static GitResult RunGit(string arguments, string input, bool interactive, TimeSpan timeout)
        {
            var utf8 = new UTF8Encoding(false);
            var startInfo = new ProcessStartInfo("git", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Without an explicit BOM-less encoding, stdin can start with a BOM and git
                // rejects the query ("credential missing protocol field").
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
            };
            startInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            if (!interactive)
                startInfo.EnvironmentVariables["GCM_INTERACTIVE"] = "never";

            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                throw new GitHubAccessException(GitHubAccess.NoGit,
                    "Git was not found, so AGS Packages can't use your GitHub credential. Install Git for Windows.");
            }

            using (process)
            {
                // Stderr is read and dropped: it's never shown, so nothing Git prints can reach the log.
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                process.StandardInput.Write(input);
                process.StandardInput.Close();
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    return new GitResult(-1, "");
                }
                Task.WaitAll(output, errors);
                return new GitResult(process.ExitCode, output.Result);
            }
        }
    }
}
