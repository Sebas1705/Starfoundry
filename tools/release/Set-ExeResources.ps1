<#
.SYNOPSIS
  Stamps the Starfoundry icon and the version information (product name, company, description, copyright, file and product version)
  into a Windows executable, with the Win32 resource API (BeginUpdateResource / UpdateResource), and nothing else to download.

.DESCRIPTION
  Why not the export preset's rcedit options: Godot's "application/modify_resources" shells out to rcedit.exe, a separate third-party
  download that has to be registered in the editor settings of every machine and runner. This script does the same job with Windows
  itself. It only works on an executable WITHOUT an embedded .pck (the release preset keeps the pack beside the exe on purpose; see
  docs/adr/0048-windows-release-pipeline.md), because rewriting resources would move the appended pack.

  It replaces every existing icon of the file (Godot's template carries the Godot icon) and the VS_VERSION_INFO block.
  Windows PowerShell 5.1 compatible (the C# below is C# 5 on purpose).

.PARAMETER Exe       The executable to modify in place.
.PARAMETER Icon      The .ico file (PNG or BMP images, as written by make-icon.ps1).
.PARAMETER Version   Semantic version (1.2.3 or 1.2.3-rc.1); the numeric part fills the binary file and product version.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Icon,
    [Parameter(Mandatory)][string]$Version,
    [string]$ProductName = 'Starfoundry',
    [string]$CompanyName = 'Sebas1705',
    [string]$FileDescription = 'Starfoundry',
    [string]$Copyright = 'Copyright (c) 2026 Sebas1705',
    [string]$InternalName,
    [string]$OriginalFilename
)

$ErrorActionPreference = 'Stop'

if (-not ('Starfoundry.PeResources' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Starfoundry
{
    public class ResName
    {
        public long Id;      // used when Text is null
        public string Text;
        public ushort Lang;
    }

    public static class PeResources
    {
        const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
        static readonly IntPtr RT_ICON = new IntPtr(3);
        static readonly IntPtr RT_GROUP_ICON = new IntPtr(14);
        static readonly IntPtr RT_VERSION = new IntPtr(16);

        delegate bool EnumNamesProc(IntPtr hModule, IntPtr type, IntPtr name, IntPtr lParam);
        delegate bool EnumLangsProc(IntPtr hModule, IntPtr type, IntPtr name, ushort lang, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr hFile, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeLibrary(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool EnumResourceNames(IntPtr h, IntPtr type, EnumNamesProc proc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool EnumResourceLanguages(IntPtr h, IntPtr type, IntPtr name, EnumLangsProc proc, IntPtr lParam);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] // the W entry point: string names are UTF-16
        static extern bool UpdateResource(IntPtr h, IntPtr type, IntPtr name, ushort lang, byte[] data, uint size);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool EndUpdateResource(IntPtr h, bool discard);

        static IntPtr ToPtr(ResName n, List<IntPtr> allocated)
        {
            if (n.Text == null) return new IntPtr(n.Id);
            IntPtr p = Marshal.StringToHGlobalUni(n.Text);
            allocated.Add(p);
            return p;
        }

        static List<ResName> Enumerate(IntPtr module, IntPtr type)
        {
            List<ResName> result = new List<ResName>();
            EnumResourceNames(module, type, delegate(IntPtr hm, IntPtr t, IntPtr name, IntPtr lp)
            {
                ResName rn = new ResName();
                if ((ulong)name.ToInt64() <= 0xFFFF) rn.Id = name.ToInt64(); else rn.Text = Marshal.PtrToStringUni(name);
                List<ushort> langs = new List<ushort>();
                EnumResourceLanguages(hm, t, name, delegate(IntPtr hm2, IntPtr t2, IntPtr n2, ushort lang, IntPtr lp2) { langs.Add(lang); return true; }, IntPtr.Zero);
                rn.Lang = langs.Count > 0 ? langs[0] : (ushort)1033;
                result.Add(rn);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        static void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException(what + " failed, Win32 error " + Marshal.GetLastWin32Error());
        }

        // ---- ICO -> RT_ICON / RT_GROUP_ICON --------------------------------------------------------------------------------
        class IcoImage { public byte W, H, Colors; public ushort Planes, Bits; public byte[] Data; }

        static List<IcoImage> ReadIco(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 6 || BitConverter.ToUInt16(b, 0) != 0 || BitConverter.ToUInt16(b, 2) != 1) throw new InvalidOperationException("Not an .ico file: " + path);
            int count = BitConverter.ToUInt16(b, 4);
            List<IcoImage> list = new List<IcoImage>();
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                IcoImage im = new IcoImage();
                im.W = b[e]; im.H = b[e + 1]; im.Colors = b[e + 2];
                im.Planes = BitConverter.ToUInt16(b, e + 4); im.Bits = BitConverter.ToUInt16(b, e + 6);
                int size = (int)BitConverter.ToUInt32(b, e + 8); int off = (int)BitConverter.ToUInt32(b, e + 12);
                im.Data = new byte[size];
                Array.Copy(b, off, im.Data, 0, size);
                list.Add(im);
            }
            return list;
        }

        static byte[] BuildGroup(List<IcoImage> images)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)images.Count);
            for (int i = 0; i < images.Count; i++)
            {
                IcoImage im = images[i];
                w.Write(im.W); w.Write(im.H); w.Write(im.Colors); w.Write((byte)0);
                w.Write(im.Planes); w.Write(im.Bits);
                w.Write((uint)im.Data.Length); w.Write((ushort)(i + 1));
            }
            w.Flush();
            return ms.ToArray();
        }

        // ---- VS_VERSIONINFO ------------------------------------------------------------------------------------------------
        static void Align4(MemoryStream ms) { while (ms.Length % 4 != 0) ms.WriteByte(0); }

        // One block: wLength, wValueLength, wType, szKey, padding, value, padding, children.
        static byte[] Block(string key, byte[] value, int valueLength, ushort type, List<byte[]> children)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write((ushort)0); w.Write((ushort)valueLength); w.Write(type);
            w.Write(Encoding.Unicode.GetBytes(key)); w.Write((ushort)0);
            Align4(ms);
            if (value != null && value.Length > 0) { w.Write(value); }
            if (children != null)
            {
                Align4(ms);
                for (int i = 0; i < children.Count; i++) { w.Write(children[i]); if (i < children.Count - 1) Align4(ms); }
            }
            w.Flush();
            byte[] r = ms.ToArray();
            BitConverter.GetBytes((ushort)r.Length).CopyTo(r, 0);
            return r;
        }

        static byte[] StringEntry(string key, string value)
        {
            byte[] v = Encoding.Unicode.GetBytes(value + "\0");
            return Block(key, v, value.Length + 1, 1, null);
        }

        static byte[] BuildVersionInfo(int[] ver, Dictionary<string, string> strings)
        {
            MemoryStream fixedInfo = new MemoryStream();
            BinaryWriter f = new BinaryWriter(fixedInfo);
            uint ms = ((uint)ver[0] << 16) | (uint)ver[1];
            uint ls = ((uint)ver[2] << 16) | (uint)ver[3];
            f.Write(0xFEEF04BDu); f.Write(0x00010000u);
            f.Write(ms); f.Write(ls); f.Write(ms); f.Write(ls);
            f.Write(0x3Fu); f.Write(0u); f.Write(0x00040004u); f.Write(1u); f.Write(0u); f.Write(0u); f.Write(0u);
            f.Flush();

            List<byte[]> entries = new List<byte[]>();
            foreach (KeyValuePair<string, string> kv in strings) entries.Add(StringEntry(kv.Key, kv.Value));
            byte[] table = Block("040904B0", null, 0, 1, entries);
            byte[] sfi = Block("StringFileInfo", null, 0, 1, new List<byte[]> { table });

            MemoryStream tr = new MemoryStream();
            BinaryWriter t = new BinaryWriter(tr);
            t.Write((ushort)0x0409); t.Write((ushort)0x04B0); t.Flush();
            byte[] translation = Block("Translation", tr.ToArray(), 4, 0, null);
            byte[] vfi = Block("VarFileInfo", null, 0, 1, new List<byte[]> { translation });

            return Block("VS_VERSION_INFO", fixedInfo.ToArray(), 52, 0, new List<byte[]> { sfi, vfi });
        }

        public static void Apply(string exe, string ico, int[] version, string[] keys, string[] values)
        {
            List<IcoImage> images = ReadIco(ico);
            Dictionary<string, string> strings = new Dictionary<string, string>();
            for (int i = 0; i < keys.Length; i++) strings[keys[i]] = values[i];

            List<ResName> oldIcons, oldGroups, oldVersions;
            IntPtr module = LoadLibraryEx(exe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
            Check(module != IntPtr.Zero, "LoadLibraryEx");
            try
            {
                oldIcons = Enumerate(module, RT_ICON);
                oldGroups = Enumerate(module, RT_GROUP_ICON);
                oldVersions = Enumerate(module, RT_VERSION);
            }
            finally { FreeLibrary(module); }

            ResName group = oldGroups.Count > 0 ? oldGroups[0] : new ResName { Id = 1, Lang = 1033 };
            ResName ver = oldVersions.Count > 0 ? oldVersions[0] : new ResName { Id = 1, Lang = 1033 };
            ushort lang = group.Lang;

            List<IntPtr> allocated = new List<IntPtr>();
            IntPtr h = BeginUpdateResource(exe, false);
            Check(h != IntPtr.Zero, "BeginUpdateResource");
            bool ok = false;
            try
            {
                foreach (ResName n in oldIcons) Check(UpdateResource(h, RT_ICON, ToPtr(n, allocated), n.Lang, null, 0), "delete old icon");
                foreach (ResName n in oldGroups) Check(UpdateResource(h, RT_GROUP_ICON, ToPtr(n, allocated), n.Lang, null, 0), "delete old icon group");
                for (int i = 0; i < images.Count; i++)
                    Check(UpdateResource(h, RT_ICON, new IntPtr(i + 1), lang, images[i].Data, (uint)images[i].Data.Length), "write icon");
                byte[] g = BuildGroup(images);
                Check(UpdateResource(h, RT_GROUP_ICON, ToPtr(group, allocated), lang, g, (uint)g.Length), "write icon group");

                foreach (ResName n in oldVersions) Check(UpdateResource(h, RT_VERSION, ToPtr(n, allocated), n.Lang, null, 0), "delete old version info");
                byte[] vi = BuildVersionInfo(version, strings);
                Check(UpdateResource(h, RT_VERSION, ToPtr(ver, allocated), ver.Lang, vi, (uint)vi.Length), "write version info");
                ok = true;
            }
            finally
            {
                bool ended = EndUpdateResource(h, !ok);
                foreach (IntPtr p in allocated) Marshal.FreeHGlobal(p);
                if (ok) Check(ended, "EndUpdateResource");
            }
        }
    }
}
'@
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path
$Icon = (Resolve-Path -LiteralPath $Icon).Path
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(-[0-9A-Za-z.-]+)?$') { throw "Not a semantic version: $Version" }
$nums = [int[]]@([int]$Matches[1], [int]$Matches[2], [int]$Matches[3], 0)
$name = [IO.Path]::GetFileName($Exe)
if (-not $InternalName) { $InternalName = [IO.Path]::GetFileNameWithoutExtension($Exe) }
if (-not $OriginalFilename) { $OriginalFilename = $name }

$map = [ordered]@{
    CompanyName      = $CompanyName
    FileDescription  = $FileDescription
    FileVersion      = "$($nums[0]).$($nums[1]).$($nums[2]).0"
    InternalName     = $InternalName
    LegalCopyright   = $Copyright
    OriginalFilename = $OriginalFilename
    ProductName      = $ProductName
    ProductVersion   = $Version
}
[Starfoundry.PeResources]::Apply($Exe, $Icon, $nums, [string[]]@($map.Keys), [string[]]@($map.Values))
Write-Host "Stamped icon and version $Version into $name"
