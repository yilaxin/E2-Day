using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace E2Day.Desktop
{
    // The Unicode shell interface preserves E²-Day in shortcut names and descriptions.
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class NativeShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IUnicodeShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr item);
        void SetIDList(IntPtr item);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    public sealed class ShortcutDetails
    {
        public string TargetPath { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string Description { get; set; }
        public string IconPath { get; set; }
        public int IconIndex { get; set; }
    }

    public static class ShellLink
    {
        public static ShortcutDetails Read(string filename)
        {
            var native = new NativeShellLink();
            try
            {
                ((IPersistFile)native).Load(filename, 0);
                var link = (IUnicodeShellLink)native;
                var text = new StringBuilder(32768);
                var result = new ShortcutDetails();
                link.GetPath(text, text.Capacity, IntPtr.Zero, 4);
                result.TargetPath = text.ToString(); text.Clear();
                link.GetArguments(text, text.Capacity);
                result.Arguments = text.ToString(); text.Clear();
                link.GetWorkingDirectory(text, text.Capacity);
                result.WorkingDirectory = text.ToString(); text.Clear();
                link.GetDescription(text, text.Capacity);
                result.Description = text.ToString(); text.Clear();
                int index;
                link.GetIconLocation(text, text.Capacity, out index);
                result.IconPath = text.ToString(); result.IconIndex = index;
                return result;
            }
            finally { Marshal.FinalReleaseComObject(native); }
        }

        public static void Save(string filename, string target, string arguments, string directory, string description, string icon)
        {
            var native = new NativeShellLink();
            try
            {
                var link = (IUnicodeShellLink)native;
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(directory);
                link.SetDescription(description);
                link.SetIconLocation(icon, 0);
                link.SetShowCmd(1);
                ((IPersistFile)native).Save(filename, true);
            }
            finally { Marshal.FinalReleaseComObject(native); }
            Refresh();
        }

        public static void Refresh() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
