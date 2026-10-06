using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HiMate.Agent.Services;

public sealed class DpapiSecretStore
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("Crypt32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

    [DllImport("Kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public void Save(string path, string secret)
    {
        var plain = Encoding.UTF8.GetBytes(secret ?? string.Empty);
        var protectedBytes = Protect(plain);
        File.WriteAllBytes(path, protectedBytes);
    }

    public string Load(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        var protectedBytes = File.ReadAllBytes(path);
        return Encoding.UTF8.GetString(Unprotect(protectedBytes));
    }

    private static byte[] Protect(byte[] input) => Transform(input, true);
    private static byte[] Unprotect(byte[] input) => Transform(input, false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inPtr = Marshal.AllocHGlobal(input.Length);
        try
        {
            Marshal.Copy(input, 0, inPtr, input.Length);
            var inBlob = new DataBlob { cbData = input.Length, pbData = inPtr };
            var outBlob = new DataBlob();
            var ok = protect
                ? CryptProtectData(ref inBlob, "HiMate Agent device secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x1, ref outBlob);

            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var output = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, output, 0, outBlob.cbData);
            LocalFree(outBlob.pbData);
            return output;
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }
    }
}
