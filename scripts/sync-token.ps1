# Extracts the Copilot OAuth token from Windows Credential Manager
# and writes it to faux-foundation/.env for container builds.
#
# Usage: .\scripts\sync-token.ps1

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public class CredReader {
    [DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr creds);
    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr cred);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct CRED { public int Flags; public int Type; public string TargetName; public string Comment;
        public long LastWritten; public int BlobSize; public IntPtr Blob; public int Persist;
        public int AttrCount; public IntPtr Attrs; public string Alias; public string UserName; }
    public static string Read(string filter) {
        if (CredEnumerate(filter, 0, out int count, out IntPtr arr)) {
            try {
                for (int i = 0; i < count; i++) {
                    var p = Marshal.ReadIntPtr(arr, i * IntPtr.Size);
                    var c = Marshal.PtrToStructure<CRED>(p);
                    if (c.BlobSize > 0) {
                        var b = new byte[c.BlobSize];
                        Marshal.Copy(c.Blob, b, 0, c.BlobSize);
                        return Encoding.UTF8.GetString(b);
                    }
                }
            } finally { CredFree(arr); }
        }
        return null;
    }
}
'@ -ErrorAction SilentlyContinue

$token = [CredReader]::Read("copilot-cli/*")
if (-not $token) {
    Write-Error "No Copilot CLI credential found. Run 'copilot' and complete /login first."
    exit 1
}

$envFile = Join-Path $PSScriptRoot ".." ".." "faux-foundation" ".env"
$resolved = Resolve-Path $envFile -ErrorAction SilentlyContinue
if ($resolved) { $envFile = $resolved.Path }

if (Test-Path $envFile) {
    $content = Get-Content $envFile -Raw
    if ($content -match "COPILOT_TOKEN=") {
        $content = $content -replace "COPILOT_TOKEN=.*", "COPILOT_TOKEN=$token"
    } else {
        $content = $content.TrimEnd() + "`nCOPILOT_TOKEN=$token`n"
    }
    $content | Set-Content $envFile -NoNewline
} else {
    "COPILOT_TOKEN=$token`n" | Set-Content $envFile -NoNewline
}

Write-Host "Token synced ($($token.Substring(0,8))...) -> $envFile"
