using System.Runtime.InteropServices;
using System.Text;

namespace CopilotLlm.Infrastructure;

/// <summary>
/// Reads credentials from Windows Credential Manager.
/// </summary>
public static class CredentialManager
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    /// <summary>
    /// Reads a credential by exact target name, or finds the first match by prefix.
    /// </summary>
    public static string? GetCredential(string targetPrefix)
    {
        if (TryRead(targetPrefix, out string? value))
            return value;

        if (CredEnumerate($"{targetPrefix}*", 0, out int count, out IntPtr credArray))
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr credPtr = Marshal.ReadIntPtr(credArray, i * IntPtr.Size);
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    if (cred.TargetName.StartsWith(targetPrefix) && cred.CredentialBlobSize > 0)
                    {
                        byte[] bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                        return Encoding.UTF8.GetString(bytes);
                    }
                }
            }
            finally
            {
                CredFree(credArray);
            }
        }

        return null;
    }

    private static bool TryRead(string target, out string? value)
    {
        value = null;
        if (!CredRead(target, 1, 0, out IntPtr credPtr))
            return false;

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlobSize > 0)
            {
                byte[] bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, cred.CredentialBlobSize);
                value = Encoding.UTF8.GetString(bytes);
                return true;
            }
        }
        finally
        {
            CredFree(credPtr);
        }
        return false;
    }
}
