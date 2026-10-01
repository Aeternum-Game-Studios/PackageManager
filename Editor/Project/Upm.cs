using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Aeternum.Packages
{
    /// <summary>
    /// Package Manager requests as tasks. Requests complete on Editor update ticks, so they're
    /// polled from EditorApplication.update: a blocking wait on the main thread would never end.
    /// </summary>
    internal static class Upm
    {
        public static Task AddAsync(string dependency) =>
            Run(Client.AddAndRemove(new[] { dependency }, null));

        public static Task RemoveAsync(string name) =>
            Run(Client.Remove(name));

        static Task Run(Request request)
        {
            var completion = new TaskCompletionSource<bool>();

            void Poll()
            {
                if (!request.IsCompleted)
                    return;
                EditorApplication.update -= Poll;
                if (request.Status == StatusCode.Success)
                    completion.SetResult(true);
                else
                    completion.SetException(new AgsPackagesException($"The Package Manager failed: {request.Error?.message}"));
            }

            EditorApplication.update += Poll;
            return completion.Task;
        }
    }
}
