using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SuperAutoDesktop {
    public static class TaskbarIdentity {
        public const string AppId="Qiye.StudyDesktop";
        [StructLayout(LayoutKind.Sequential)] private struct Key {public Guid Format;public uint Id;public Key(uint id){Format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");Id=id;}}
        [StructLayout(LayoutKind.Explicit,Size=24)] private struct Value {[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Text;}
        [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface Store {
            [PreserveSig]int GetCount(out uint count);
            [PreserveSig]int GetAt(uint index,out Key key);
            [PreserveSig]int GetValue(ref Key key,out Value value);
            [PreserveSig]int SetValue(ref Key key,ref Value value);
            [PreserveSig]int Commit();
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
        [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,out Store store);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHGetPropertyStoreFromParsingName(string path,IntPtr context,uint flags,ref Guid iid,out Store store);
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref Value value);
        public static void ApplyProcess(){Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));}
        private static void Set(Store store,uint property,string text) {
            var key=new Key(property);var value=new Value {Type=31,Text=Marshal.StringToCoTaskMemUni(text)};
            try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));}finally{PropVariantClear(ref value);}
        }
        private static void Apply(Store store,string executable,string icon) {
            Set(store,5,AppId);Set(store,2,"\""+executable+"\"");Set(store,3,icon+",0");Set(store,4,"栖页");
        }
        public static void ApplyWindow(IntPtr window,string executable,string icon) {
            Guid iid=typeof(Store).GUID;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out store));
            try{Apply(store,executable,icon);}finally{Marshal.ReleaseComObject(store);}
        }
        public static void ApplyShortcut(string path,string executable,string icon) {
            Guid iid=typeof(Store).GUID;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,2,ref iid,out store));
            try{Apply(store,executable,icon);Marshal.ThrowExceptionForHR(store.Commit());}finally{Marshal.ReleaseComObject(store);}
        }
        public static string WindowAppId(IntPtr window) {
            Guid iid=typeof(Store).GUID;Store store;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out store));
            try {var key=new Key(5);Value value;Marshal.ThrowExceptionForHR(store.GetValue(ref key,out value));try{return value.Type==31?Marshal.PtrToStringUni(value.Text):"";}finally{PropVariantClear(ref value);}}
            finally{Marshal.ReleaseComObject(store);}
        }
    }
}
