using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace WeaponRestrict.Core;

/// <summary>
/// Отладка: по какому адресу CSS находит CanAcquire и что лежит в начале функции в живой
/// памяти (перехваты utils.so / CSS — переходы E9 / FF 25), с владельцем каждого адреса.
/// </summary>
internal static class Probe
{
    private const string CssSig = "55 48 89 E5 41 57 41 56 49 89 F6 41 55 41 54 49 89 CC 53 48 89 FB 48 83 EC 78";
    private const string TailSig = "? ? ? ? ? 41 57 41 56 49 89 F6 41 55 41 54 49 89 CC 53 48 89 FB 48 83 EC ? 4C 8B 6F 38";

    public static IEnumerable<string> Run()
    {
        var maps = File.ReadAllLines("/proc/self/maps");
        var server = Addresses.ServerPath;

        var css = Find(server, CssSig);
        var tail = Find(server, TailSig);
        yield return $"сигнатура CSS → {Describe(css, maps)}";
        yield return $"сигнатура без первых 5 байт → {Describe(tail, maps)}";

        foreach (var (name, start) in new[] { ("CSS", css), ("хвост", tail) })
        {
            if (start == IntPtr.Zero) continue;
            var a = start;
            for (var hop = 0; hop < 6; hop++)
            {
                var b = Read(a, 16);
                yield return $"  [{name}] {hop}: {Describe(a, maps)}  {BitConverter.ToString(b).Replace('-', ' ')}";
                if (hop > 0)
                    foreach (var l in Imm64Owners(a, maps))
                        yield return $"      {l}";
                IntPtr next;
                if (b[0] == 0xE9)
                    next = a + 5 + BitConverter.ToInt32(b, 1);
                else if (b[0] == 0xFF && b[1] == 0x25)
                    next = Marshal.ReadIntPtr(a + 6 + BitConverter.ToInt32(b, 2));
                else if (b[0] == 0x48 && b[1] == 0xB8 && b[10] == 0xFF && b[11] == 0xE0) // mov rax, imm64; jmp rax
                    next = (IntPtr)BitConverter.ToInt64(b, 2);
                else break;
                a = next;
            }
        }
    }

    /// <summary>Все mov r64, imm64 и call/jmp [rip+x] в первых 768 байтах заглушки — чьи адреса.</summary>
    private static IEnumerable<string> Imm64Owners(IntPtr a, string[] maps, int len = 768)
    {
        var b = Read(a, len);
        var seen = new HashSet<long>();
        for (var i = 0; i + 10 <= b.Length; i++)
        {
            long v = 0; string kind = "";
            if ((b[i] == 0x48 || b[i] == 0x49) && b[i + 1] >= 0xB8 && b[i + 1] <= 0xBF)
            { v = BitConverter.ToInt64(b, i + 2); kind = "mov imm64"; }
            else if (b[i] == 0xFF && (b[i + 1] == 0x15 || b[i + 1] == 0x25) && i + 6 <= b.Length)
            {
                // читать только из отображённой памяти: AccessViolation в .NET не ловится — сервер упадёт
                var slot = a + i + 6 + BitConverter.ToInt32(b, i + 2);
                if (Describe(slot, maps).EndsWith("(?)")) continue;
                v = Marshal.ReadInt64(slot); kind = b[i + 1] == 0x15 ? "call [rip]" : "jmp [rip]";
            }
            else continue;
            if (v < 0x10000 || !seen.Add(v)) continue;
            var d = Describe((IntPtr)v, maps);
            if (d.EndsWith("(?)")) continue;
            yield return $"+0x{i:X} {kind} → {d}";
        }
        yield return "дамп: " + BitConverter.ToString(b, 0, Math.Min(160, b.Length)).Replace('-', ' ');
    }

    /// <summary>Дамп произвольного адреса (только отображённая память) + чьи адреса внутри.</summary>
    public static IEnumerable<string> Dump(IntPtr a, int len)
    {
        var maps = File.ReadAllLines("/proc/self/maps");
        var d = Describe(a, maps);
        if (d.EndsWith("(?)") || d == "не найдено") { yield return $"{d}: не отображено"; yield break; }
        if (Describe(a + len - 1, maps).EndsWith("(?)")) { yield return "конец диапазона не отображён"; yield break; }
        yield return d;
        var b = Read(a, len);
        for (var i = 0; i < len; i += 32)
            yield return $"  +0x{i:X3}: {BitConverter.ToString(b, i, Math.Min(32, len - i)).Replace('-', ' ')}";
        foreach (var l in Imm64Owners(a, maps, len))
            if (!l.StartsWith("дамп")) yield return "  " + l;
    }

    private static IntPtr Find(string module, string sig)
    {
        try { return NativeAPI.FindSignature(module, sig); }
        catch { return IntPtr.Zero; }
    }

    private static byte[] Read(IntPtr a, int n)
    {
        var b = new byte[n];
        Marshal.Copy(a, b, 0, n);
        return b;
    }

    private static string Describe(IntPtr a, string[] maps)
    {
        if (a == IntPtr.Zero) return "не найдено";
        var v = (ulong)a.ToInt64();
        foreach (var l in maps)
        {
            var parts = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var range = parts[0].Split('-');
            var s = Convert.ToUInt64(range[0], 16);
            var e = Convert.ToUInt64(range[1], 16);
            if (v < s || v >= e) continue;
            var file = parts.Length > 5 ? Path.GetFileName(parts[5]) : "[anon]";
            var fileOff = Convert.ToUInt64(parts[2], 16);
            return $"0x{v:X} ({file}+0x{v - s + fileOff:X})";
        }
        return $"0x{v:X} (?)";
    }
}
