using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DragonLauncher
{
    internal static class TaskbarIcon
    {
        private static readonly Guid AppUserModel = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        private const string AppId = "CodexDragon.ChatGPT";
        private const uint RelaunchCommand = 2, RelaunchIconResource = 3, RelaunchDisplayName = 4, ApplicationId = 5;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            internal Guid Format;
            internal uint Id;
        }

        // The launcher is built for x64. PROPVARIANT uses an 8-byte header and a 16-byte union.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] internal ushort Type;
            [FieldOffset(8)] internal IntPtr Pointer;
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        internal static string Apply(IntPtr window, string launcherDirectory)
        {
            string icon = Path.Combine(launcherDirectory, "chatgpt-local.ico");
            string launcher = Path.Combine(launcherDirectory, "Launch.exe");
            if (!File.Exists(icon) || !File.Exists(launcher)) return "skipped (local icon or launcher missing)";
            try
            {
                IPropertyStore store;
                Guid iid = typeof(IPropertyStore).GUID;
                Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out store));
                try
                {
                    uint[] properties = { RelaunchCommand, RelaunchIconResource, RelaunchDisplayName, ApplicationId };
                    foreach (uint id in properties)
                    {
                        var key = new PropertyKey { Format = AppUserModel, Id = id };
                        PropVariant current;
                        store.GetValue(ref key, out current);
                        try
                        {
                            if (current.Type != 0) return "skipped (existing window configuration)";
                        }
                        finally { PropVariantClear(ref current); }
                    }

                    int written = 0;
                    try
                    {
                        Set(store, RelaunchCommand, "\"" + launcher + "\"");
                        written++;
                        Set(store, RelaunchIconResource, icon + ",0");
                        written++;
                        Set(store, RelaunchDisplayName, "ChatGPT");
                        written++;
                        // Setting the AppUserModelID last triggers the taskbar refresh.
                        Set(store, ApplicationId, AppId);
                        return "applied";
                    }
                    catch (COMException)
                    {
                        for (int index = written - 1; index >= 0; index--)
                        {
                            try { Set(store, properties[index], null); }
                            catch (COMException) { }
                        }
                        throw;
                    }
                }
                finally { Marshal.ReleaseComObject(store); }
            }
            catch (COMException error)
            {
                return "unavailable (0x" + error.ErrorCode.ToString("X8") + ")";
            }
        }

        private static void Set(IPropertyStore store, uint id, string text)
        {
            var key = new PropertyKey { Format = AppUserModel, Id = id };
            var value = new PropVariant();
            if (text != null)
            {
                value.Type = 31; // VT_LPWSTR
                value.Pointer = Marshal.StringToCoTaskMemUni(text);
            }
            try { store.SetValue(ref key, ref value); }
            finally { PropVariantClear(ref value); }
        }
    }
}
