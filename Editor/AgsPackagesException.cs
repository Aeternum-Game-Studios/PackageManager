using System;

namespace Aeternum.Packages
{
    /// <summary>An AGS Packages operation failed. The message never contains a credential.</summary>
    public class AgsPackagesException : Exception
    {
        public AgsPackagesException(string message) : base(message) { }
    }
}
