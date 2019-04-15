using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Harmony.ILCopying;
using UnityEngine;
using Logger = BepInEx.Logger;

namespace INIfier
{
    /// <summary>
    /// Used to replace the contents of TextAssets with data from .ini files.
    /// Conversion of https://www.nexusmods.com/site/mods/30
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [BepInPlugin("inifier", "INIfier BepInEx version", "0.1")]
    public class INIfier : BaseUnityPlugin
    {
        private const string ReplacementFileExtension = ".ini";
        private const string DumpFileExtension = ".found";

        private static string _assetsPath;
        private static bool _enabled;

        private static List<byte> _originalBytesCode = new List<byte>();
        private static long _originalBytesLocation;
        private static long _replacementBytesLocation;

        private static List<byte> _originalTextCode = new List<byte>();
        private static long _originalTextLocation;
        private static long _replacementTextLocation;

        /// <summary>
        /// Gets the contents of a given replacement file as a byte array
        /// </summary>
        /// <param name="name">
        /// The file to read from.
        /// If the name does not end in .ini it will be appended automatically
        /// </param>
        /// <param name="getFoundFile">False to read from .ini file, True to read from .found file</param>
        /// <returns></returns>
        public static byte[] GetFileBytes(string name, bool getFoundFile = false)
        {
            return GetFileContent(name, ReplaceType.Bytes, getFoundFile) as byte[];
        }

        /// <summary>
        /// Get the contents of a given replacement file as a string
        /// </summary>
        /// <param name="name">
        /// The file to read from.
        /// If the name does not end in .ini it will be appended automatically
        /// </param>
        /// <param name="getFoundFile">False to read from .ini file, True to read from .found file</param>
        public static string GetFileText(string name, bool getFoundFile = false)
        {
            return GetFileContent(name, ReplaceType.Text, getFoundFile) as string;
        }

        /// <summary>
        /// Checks whether a .found file exists for a given TextAsset
        /// </summary>
        /// <param name="name">
        /// The file to check for.
        /// If the name does not end in .found it will be appended automatically
        /// </param>
        public static bool IsFileFound(string name)
        {
            CheckOrFixName(ref name, DumpFileExtension);
            return File.Exists(Path.Combine(_assetsPath, name));
        }

        /// <summary>
        /// Checks whether a .ini file exists for a given TextAsset
        /// </summary>
        /// <param name="name">
        /// The file to check for.
        /// If the name does not end in .ini it will be appended automatically
        /// </param>
        public static bool IsFileRegistered(string name)
        {
            CheckOrFixName(ref name);
            return File.Exists(Path.Combine(_assetsPath, name));
        }

        /// <summary>
        /// Registers a file to replace a TextAsset of the same name.
        /// </summary>
        /// <param name="name">
        /// The file to register.
        /// If the name does not end in .ini it will be appended automatically
        /// </param>
        /// <param name="content">The content to place in the file.</param>
        /// <param name="overwrite">Whether to overwrite an existing file.</param>
        public static void RegisterFile(string name, string content, bool overwrite = false)
        {
            DoRegister(name, content, ReplaceType.Text, overwrite);
        }

        /// <summary>
        /// Registers a file to replace a TextAsset of the same name.
        /// </summary>
        /// <param name="name">
        /// The file to register.
        /// If the name does not end in .ini it will be appended automatically
        /// </param>
        /// <param name="content">The content to place in the file.</param>
        /// <param name="overwrite">Whether to overwrite an existing file.</param>
        public static void RegisterFile(string name, byte[] content, bool overwrite = false)
        {
            DoRegister(name, content, ReplaceType.Bytes, overwrite);
        }

        private void Awake()
        {
            _assetsPath = Path.Combine(Paths.PluginPath, "Assets");

            if (!Directory.Exists(_assetsPath))
            {
                try
                {
                    Logger.Log(LogLevel.Info, "No Assets directory, creating...");
                    Directory.CreateDirectory(_assetsPath);
                }
                catch (Exception)
                {
                    Logger.Log(LogLevel.Error, "Error creating Assets directory " + _assetsPath);
                    throw;
                }
            }

            _enabled = true;
            Load();
        }

        private static void BackupMethodStart(long location, ref List<byte> array)
        {
            var flag = IntPtr.Size == 8;
            if (flag)
            {
                var flag2 = CompareBytes(
                    location, new byte[]
                    {
                        233
                    });
                if (flag2)
                {
                    var num = ReadInt(location + 1L);
                    location += 5 + num;
                }
                for (var i = 0; i < 12; i++)
                    array.Add(ReadByte(location + i));
            }
            else
            {
                for (var j = 0; j < 6; j++)
                    array.Add(ReadByte(location + j));
            }
        }

        private static void CheckOrFixName(ref string name, string extension = ReplacementFileExtension)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Filename cannot be null or empty.", nameof(name));

            if (name.Length < extension.Length || name.Substring(name.Length - extension.Length) != extension)
                name += extension;
        }

        private static unsafe bool CompareBytes(long memory, byte[] values)
        {
            var p = (byte*)memory;
            foreach (var value in values)
            {
                if (value != *p) return false;
                p++;
            }
            return true;
        }

        private static void DoRegister(string name, object content, ReplaceType replaceType, bool overwrite)
        {
            CheckOrFixName(ref name);
            Logger.Log(LogLevel.Info, "Trying to register " + name);
            if (File.Exists(_assetsPath + name) && !overwrite)
            {
                Logger.Log(LogLevel.Error, "Error: File already exists, overwrite not specified");
                throw new ArgumentException("File already exists, overwrite not specified.", nameof(name));
            }

            try
            {
                if (replaceType != ReplaceType.Text)
                {
                    if (replaceType == ReplaceType.Bytes)
                        File.WriteAllBytes(_assetsPath + name, content as byte[] ?? throw new InvalidOperationException());
                }
                else
                    File.WriteAllText(_assetsPath + name, content as string, Encoding.UTF8);
                Logger.Log(LogLevel.Info, "File registered");
            }
            catch (Exception ex)
            {
                Logger.Log(LogLevel.Error, "Error registering file");
                LogException(ex);
                throw;
            }
        }

        private static object DoReplace(TextAsset asset, ReplaceType replaceType)
        {
            object obj = null;
            var text = "";
            if (replaceType != ReplaceType.Text)
            {
                if (replaceType == ReplaceType.Bytes)
                {
                    try
                    {
                        RestoreMethodStart(_originalBytesLocation, _originalBytesCode);
                        obj = asset.bytes;
                        Memory.WriteJump(_originalBytesLocation, _replacementBytesLocation);
                        text = "bytes";
                    }
                    catch (Exception ex)
                    {
                        Logger.Log(LogLevel.Error, "Error getting bytes");
                        LogException(ex);
                        throw;
                    }
                }
            }
            else
            {
                try
                {
                    RestoreMethodStart(_originalTextLocation, _originalTextCode);
                    obj = asset.text;
                    Memory.WriteJump(_originalTextLocation, _replacementTextLocation);
                    text = "text";
                }
                catch (Exception ex2)
                {
                    Logger.Log(LogLevel.Error, "Error getting text");
                    LogException(ex2);
                    throw;
                }
            }

            var name = Path.Combine(_assetsPath, asset.name);
            var replacementFilePath = name + ReplacementFileExtension;
            if (File.Exists(replacementFilePath))
            {
                if (_enabled)
                {
                    Logger.Log(LogLevel.Info, "Replacing " + text + " in " + name);
                    try
                    {
                        if (replaceType != ReplaceType.Text)
                        {
                            if (replaceType == ReplaceType.Bytes)
                                obj = File.ReadAllBytes(replacementFilePath);
                        }
                        else
                            obj = File.ReadAllText(replacementFilePath, Encoding.UTF8);
                    }
                    catch (Exception e)
                    {
                        Logger.Log(LogLevel.Error, string.Concat("Error replacing ", text, " in ", name, ", using original"));
                        LogException(e);
                    }
                }
            }
            else
            {
                var dumpFilePath = name + DumpFileExtension;
                if (!File.Exists(dumpFilePath))
                {
                    Logger.Log(LogLevel.Info, "File " + replacementFilePath + " not found, creating 'found' file");
                    try
                    {
                        if (replaceType != ReplaceType.Text)
                        {
                            if (replaceType == ReplaceType.Bytes)
                                File.WriteAllBytes(dumpFilePath, obj as byte[] ?? throw new InvalidOperationException());
                        }
                        else
                            File.WriteAllText(dumpFilePath, obj as string, Encoding.UTF8);
                    }
                    catch (Exception e2)
                    {
                        Logger.Log(LogLevel.Error, "Error creating file: " + dumpFilePath);
                        LogException(e2);
                    }
                }

                if (File.Exists(replacementFilePath))
                    obj = DoReplace(asset, replaceType);
            }
            return obj;
        }

        private static object GetFileContent(string name, ReplaceType replaceType, bool getFoundFile)
        {
            if (getFoundFile)
                CheckOrFixName(ref name, DumpFileExtension);
            else
                CheckOrFixName(ref name);

            if (!File.Exists(_assetsPath + name))
                throw new ArgumentException("File does not exist.", nameof(name));

            if (replaceType == ReplaceType.Text)
                return File.ReadAllText(_assetsPath + name, Encoding.UTF8);
            if (replaceType == ReplaceType.Bytes)
                return File.ReadAllBytes(_assetsPath + name);
            return null;
        }

        private static void Load()
        {
            var hadIssues = false;

            _originalTextLocation = Memory.GetMethodStart(typeof(TextAsset).GetProperty("text").GetGetMethod());
            _replacementTextLocation = Memory.GetMethodStart(typeof(INIfier).GetMethod(nameof(ReplaceText), BindingFlags.Static | BindingFlags.NonPublic));
            try
            {
                BackupMethodStart(_originalTextLocation, ref _originalTextCode);
                Memory.WriteJump(_originalTextLocation, _replacementTextLocation);
            }
            catch (Exception e2)
            {
                if (_originalTextCode.Count != 0)
                {
                    Logger.Log(LogLevel.Error, "Trying to restore text method, may cause instabilty");
                    RestoreMethodStart(_originalTextLocation, _originalTextCode);
                }
                hadIssues = true;
                LogException(e2);
            }

            _originalBytesLocation = Memory.GetMethodStart(typeof(TextAsset).GetProperty("bytes").GetGetMethod());
            _replacementBytesLocation = Memory.GetMethodStart(typeof(INIfier).GetMethod(nameof(ReplaceBytes), BindingFlags.Static | BindingFlags.NonPublic));
            try
            {
                BackupMethodStart(_originalBytesLocation, ref _originalBytesCode);
                Memory.WriteJump(_originalBytesLocation, _replacementBytesLocation);
            }
            catch (Exception e3)
            {
                if (_originalBytesCode.Count != 0)
                {
                    Logger.Log(LogLevel.Error, "Trying to restore bytes method, may cause instabilty");
                    RestoreMethodStart(_originalBytesLocation, _originalBytesCode);
                }
                hadIssues = true;
                LogException(e3);
            }

            if (hadIssues)
            {
                Logger.Log(LogLevel.Error, "Problem during method rewriting. See above messages for details.");
                Logger.Log(LogLevel.Error, "INIfier cannot continue, exiting");
            }
        }

        private static void LogException(Exception e)
        {
            var text = "Exception info: \n" + e.Message + "\n";
            var flag = e.InnerException != null;
            if (flag)
                text = text + e.InnerException.Message + "\n";
            text += e.StackTrace;
            Logger.Log(LogLevel.Error, text);
        }

        private static unsafe byte ReadByte(long memory)
        {
            var p = (byte*)memory;
            return *p;
        }

        private static unsafe int ReadInt(long memory)
        {
            var p = (int*)memory;
            return *p;
        }

        private static byte[] ReplaceBytes(TextAsset asset)
        {
            return DoReplace(asset, ReplaceType.Bytes) as byte[];
        }

        private static string ReplaceText(TextAsset asset)
        {
            return DoReplace(asset, ReplaceType.Text) as string;
        }

        private static void RestoreMethodStart(long location, List<byte> array)
        {
            Memory.UnprotectMemoryPage(location);
            var flag = IntPtr.Size == 8;
            if (flag)
            {
                if (CompareBytes(location, new byte[] { 233 }))
                {
                    var num = ReadInt(location + 1L);
                    location += 5 + num;
                }
            }
            foreach (var b in array)
                location = Memory.WriteByte(location, b);
        }

        private enum ReplaceType
        {
            Text,
            Bytes
        }
    }
}
