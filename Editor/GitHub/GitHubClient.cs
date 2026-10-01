using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Aeternum.Packages
{
    /// <summary>A git tag of a catalog package whose name, minus the package's tag prefix, is a version.</summary>
    internal sealed class VersionTag
    {
        public string Name;
        public SemVersion Version;
        public string ObjectSha;
        public string ObjectType;
    }

    /// <summary>
    /// The few GitHub REST calls AGS Packages makes. The token goes only to api.github.com, in the
    /// Authorization header; error messages carry status codes and resource names, never headers.
    /// </summary>
    internal sealed class GitHubClient
    {
        const string ApiRoot = "https://api.github.com";

        // Redirects are followed by hand, so the Authorization header never leaves api.github.com.
        static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(10),
        };

        readonly GitCredential credential;

        public GitHubClient(GitCredential credential)
        {
            this.credential = credential ?? throw new ArgumentNullException(nameof(credential));
        }

        public string Username => credential.Username;

        /// <summary>The login the token belongs to. Throws <see cref="GitHubAccessException"/> on 401.</summary>
        public async Task<string> GetLoginAsync()
        {
            var user = Json.ParseObject(await GetStringAsync("/user", "your GitHub account"));
            return user.GetString("login");
        }

        /// <summary>A file's raw contents. Throws <see cref="GitHubAccessException"/> on 404 (no access).</summary>
        public Task<string> GetFileAsync(string repo, string path, string gitRef)
        {
            var url = $"/repos/{repo}/contents/{EscapePath(path)}";
            if (!string.IsNullOrEmpty(gitRef))
                url += "?ref=" + Uri.EscapeDataString(gitRef);
            return GetStringAsync(url, $"{path} in {repo}", "application/vnd.github.raw");
        }

        /// <summary>The tags named prefix + version, newest version first.</summary>
        public async Task<List<VersionTag>> GetVersionTagsAsync(string repo, string tagPrefix)
        {
            var prefix = tagPrefix ?? "";
            var tags = new List<VersionTag>();
            var url = $"/repos/{repo}/git/matching-refs/tags/{EscapePath(prefix)}";
            foreach (var item in await GetPagedArrayAsync(url, $"the tags of {repo}"))
            {
                if (!(item is Dictionary<string, object> reference))
                    continue;
                var refName = reference.GetString("ref") ?? "";
                const string tagsPrefix = "refs/tags/";
                if (!refName.StartsWith(tagsPrefix + prefix, StringComparison.Ordinal))
                    continue;
                var name = refName.Substring(tagsPrefix.Length);
                if (!SemVersion.TryParse(name.Substring(prefix.Length), out var version))
                    continue;
                var target = reference.GetObject("object");
                tags.Add(new VersionTag
                {
                    Name = name,
                    Version = version,
                    ObjectSha = target.GetString("sha"),
                    ObjectType = target.GetString("type"),
                });
            }
            tags.Sort((a, b) => b.Version.CompareTo(a.Version));
            return tags;
        }

        /// <summary>The commit a tag points to, following annotated tags.</summary>
        public async Task<string> ResolveCommitAsync(string repo, VersionTag tag)
        {
            var sha = tag.ObjectSha;
            var type = tag.ObjectType;
            for (var depth = 0; type == "tag" && depth < 5; depth++)
            {
                var tagObject = Json.ParseObject(await GetStringAsync($"/repos/{repo}/git/tags/{sha}", $"tag {tag.Name} of {repo}"));
                var target = tagObject.GetObject("object");
                sha = target.GetString("sha");
                type = target.GetString("type");
            }
            if (type != "commit" || string.IsNullOrEmpty(sha))
                throw new AgsPackagesException($"Tag {tag.Name} of {repo} doesn't point to a commit.");
            return sha;
        }

        /// <summary>Downloads the zip archive of a commit to <paramref name="destination"/>.</summary>
        public async Task DownloadZipballAsync(string repo, string commitSha, string destination)
        {
            var what = $"the archive of {repo} at {commitSha.Substring(0, Math.Min(7, commitSha.Length))}";
            using (var response = await SendAsync($"/repos/{repo}/zipball/{commitSha}", what, "application/vnd.github+json"))
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await SaveAsync(response, destination);
                    return;
                }

                // GitHub answers with a short-lived download URL that carries its own token: it's
                // fetched without the Authorization header and never logged.
                var location = response.Headers.Location;
                if (!IsRedirect(response.StatusCode) || location == null || location.Scheme != Uri.UriSchemeHttps)
                    throw Failure(response.StatusCode, what);

                try
                {
                    using (var download = await Http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead))
                    {
                        if (download.StatusCode != HttpStatusCode.OK)
                            throw Failure(download.StatusCode, what);
                        await SaveAsync(download, destination);
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
                {
                    throw new AgsPackagesException($"Could not download {what}: the connection failed.");
                }
            }
        }

        static async Task SaveAsync(HttpResponseMessage response, string destination)
        {
            using (var source = await response.Content.ReadAsStreamAsync())
            using (var file = File.Create(destination))
                await source.CopyToAsync(file);
        }

        async Task<List<object>> GetPagedArrayAsync(string url, string what)
        {
            var items = new List<object>();
            for (var page = 0; url != null && page < 50; page++)
            {
                using (var response = await SendAsync(url, what, "application/vnd.github+json"))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw Failure(response.StatusCode, what);
                    items.AddRange(Json.ParseArray(await response.Content.ReadAsStringAsync()));
                    url = NextPage(response);
                }
            }
            return items;
        }

        async Task<string> GetStringAsync(string url, string what, string accept = "application/vnd.github+json")
        {
            using (var response = await SendAsync(url, what, accept))
            {
                if (response.StatusCode != HttpStatusCode.OK)
                    throw Failure(response.StatusCode, what);
                return await response.Content.ReadAsStringAsync();
            }
        }

        async Task<HttpResponseMessage> SendAsync(string url, string what, string accept)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url.StartsWith("https://", StringComparison.Ordinal) ? url : ApiRoot + url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
            request.Headers.Accept.ParseAdd(accept);
            request.Headers.UserAgent.ParseAdd("AGS-Packages");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            try
            {
                return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException)
            {
                throw new GitHubAccessException(GitHubAccess.Unreachable, $"Could not reach GitHub to read {what}.");
            }
            catch (Exception e)
            {
                // The request carries the token: no message from inside it is passed on.
                throw new AgsPackagesException($"Could not send the request for {what} ({e.GetType().Name}).");
            }
            finally
            {
                request.Dispose();
            }
        }

        Exception Failure(HttpStatusCode status, string what)
        {
            switch ((int)status)
            {
                case 401:
                    return new GitHubAccessException(GitHubAccess.BadCredential,
                        $"GitHub rejected the credential Git has for {Username}. To sign in again, run " +
                        $"\"git credential-manager github logout {Username}\" and press Connect.");
                case 403:
                case 404:
                    // GitHub answers 404 for a private repository you can't read.
                    return new GitHubAccessException(GitHubAccess.NoAccess,
                        $"{Username} can't read {what} (GitHub answered {(int)status}).");
                default:
                    return new AgsPackagesException($"GitHub answered {(int)status} when reading {what}.");
            }
        }

        static bool IsRedirect(HttpStatusCode status) =>
            status == HttpStatusCode.Found || status == HttpStatusCode.MovedPermanently ||
            status == HttpStatusCode.SeeOther || status == HttpStatusCode.TemporaryRedirect || (int)status == 308;

        static string NextPage(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Link", out var values))
                return null;
            foreach (var link in values.SelectMany(v => v.Split(',')))
            {
                var parts = link.Split(';');
                if (parts.Length < 2 || !parts.Skip(1).Any(p => p.Trim() == "rel=\"next\""))
                    continue;
                var next = parts[0].Trim().TrimStart('<').TrimEnd('>');
                return next.StartsWith(ApiRoot + "/", StringComparison.Ordinal) ? next : null;
            }
            return null;
        }

        static string EscapePath(string path) =>
            string.Join("/", (path ?? "").Split('/').Select(Uri.EscapeDataString));
    }

    internal enum GitHubAccess
    {
        NoGit,
        NoCredential,
        WrongAccount,
        Unreachable,
        BadCredential,
        NoAccess,
    }

    /// <summary>GitHub couldn't be reached, or the credential can't read what was asked.</summary>
    internal sealed class GitHubAccessException : AgsPackagesException
    {
        public GitHubAccess Access { get; }

        public GitHubAccessException(GitHubAccess access, string message) : base(message)
        {
            Access = access;
        }
    }
}
