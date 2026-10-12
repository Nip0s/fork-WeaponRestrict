using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using WeaponRestrict.Services;

namespace WeaponRestrict.Core;

internal sealed class Restrictor : IDisposable
{
    private readonly WeaponRestrict _plugin;

    private static readonly ConcurrentDictionary<string, long> Seen = new();

    private const int DebounceWindowMs = 1500;

    // Одноразовые пропуски: слот → (оружие, до какого момента). Ставит css_restrict_pass —
    // другой плагин выдаёт игроку оружие, которое иначе под запретом (РПГ в NiposCustomRounds —
    // подкласс G3SG1, а G3SG1 запрещён всем).
    private static readonly ConcurrentDictionary<int, (string Weapon, long Until)> Passes = new();

    // Хук CanAcquire хоть раз сработал. Если нет — его перебил чужой перехват (utils.so
    // с сигнатурой "CanAcquire" в addons/configs/signatures.ini ставит свой, и до CSS вызов
    // не доходит), и работает подстраховка по событию item_pickup.
    private bool _hookAlive;
    private bool _fallbackAnnounced;

    // Последние покупки: слот → (оружие, когда). Для возврата денег, если подстраховка
    // забрала купленный ствол.
    private readonly Dictionary<int, (string Weapon, long At)> _purchases = new();

    public static void AddPass(int slot, string weapon, float seconds)
    {
        var until = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(seconds * 1000);
        Passes[slot] = (weapon, until);
    }

    private static bool TakePass(int slot, string weapon)
    {
        if (!Passes.TryGetValue(slot, out var pass)) return false;
        if (pass.Until < DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            Passes.TryRemove(slot, out _);
            return false;
        }
        if (!pass.Weapon.Equals(weapon, StringComparison.OrdinalIgnoreCase)) return false;
        Passes.TryRemove(slot, out _);
        return true;
    }

    public Restrictor(WeaponRestrict plugin)
    {
        _plugin = plugin;
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnWeaponCanAcquire, HookMode.Pre);
        Console.WriteLine("[WeaponRestrict] Хук CCSPlayer_ItemServices::CanAcquire установлен");

        plugin.RegisterEventHandler<EventItemPurchase>(OnItemPurchase);
        plugin.RegisterEventHandler<EventItemPickup>(OnItemPickup);
    }

    public void Dispose()
    {
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnWeaponCanAcquire, HookMode.Pre);
    }

    private static bool SeenRecently(ulong steamId, string weapon, int windowMs)
    {
        var key = $"{steamId}:{weapon}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (Seen.TryGetValue(key, out var last) && (now - last) < windowMs)
            return true;

        Seen[key] = now;

        if (Seen.Count > 4096)
        {
            foreach (var kv in Seen)
            {
                if ((now - kv.Value) > (windowMs * 4))
                    Seen.TryRemove(kv.Key, out _);
            }
        }

        return false;
    }

    private void Dbg(string msg)
    {
        if (_plugin.Config.Debug)
            Console.WriteLine($"[WeaponRestrict:debug] {msg}");
    }

    private static string? WeaponName(int defindex)
    {
        var vdata = VirtualFunctions.GetCSWeaponDataFromKeyFunc.Invoke(-1, defindex.ToString());
        return string.IsNullOrWhiteSpace(vdata?.Name) ? null : vdata!.Name;
    }

    /// <summary>
    /// Нарушает ли получение оружия лимиты. <paramref name="alreadyHeld"/> — ствол уже у игрока
    /// (подстраховка после получения): он сам себя не считает. Возвращает лимит или null.
    /// </summary>
    private int? Violation(CCSPlayerController player, string classname, bool alreadyHeld)
    {
        if (!WeaponRestrict.RestrictionsEnabled)
            return null;

        var cfg = _plugin.Config;
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid) return null;

        var team = (int)pawn.TeamNum;
        var playerCount = WorldQuery.CountPlayers(cfg, team);

        if (!RuleBook.TryGetLimit(cfg, classname, playerCount, team, out var limit))
        {
            Dbg($"  выход: правила нет (игроков посчитано {playerCount}, карта {Server.MapName})");
            return null;
        }

        var isInNoBypassList = cfg.NoBypassWeapons?.Any(w =>
            w.Equals(classname, StringComparison.OrdinalIgnoreCase)) ?? false;

        var hardBan = (limit == 0 && !cfg.BypassAllowedWhenLimitIsZero) || isInNoBypassList;
        if (!hardBan && Perms.HasBypass(cfg, player))
        {
            Dbg("  выход: у игрока обход (права)");
            return null;
        }

        var currentCount = WorldQuery.CountWeaponAcrossPlayers(cfg, classname, team);
        if (alreadyHeld) currentCount = Math.Max(0, currentCount - 1);
        var violates = limit == 0 || (limit != -1 && currentCount >= limit);
        Dbg($"  лимит {limit}, уже на руках {currentCount}, игроков {playerCount}, жёсткий запрет {hardBan} → {(violates ? "ЗАПРЕТ" : "можно")}");
        return violates ? limit : null;
    }

    private void NotifyBlocked(CCSPlayerController player, string classname, int limit)
    {
        var cfg = _plugin.Config;
        if (SeenRecently(player.SteamID, classname, DebounceWindowMs))
            return;

        var msgTemplate = cfg.TypeWeapons == 2 ? cfg.Phrases.BlockTeam : cfg.Phrases.Block;
        var msg = msgTemplate
            .Replace("{weapon}", cfg.Phrases.Pretty(classname))
            .Replace("{limit}", limit.ToString());
        Notify.Info(player, cfg.ChatPrefix, msg);

        SoundService.TryEmitSafe(player, cfg.BlockSound);
    }

    private HookResult OnWeaponCanAcquire(DynamicHook hook)
    {
        _hookAlive = true;
        Dbg("CanAcquire вызван");
        var player = hook.GetParam<CCSPlayer_ItemServices>(0)
            ?.Pawn.Value?.Controller.Value?.As<CCSPlayerController>();

        if (player == null || !player.IsValid || !player.PawnIsAlive)
        {
            Dbg($"  выход: игрок не найден или мёртв (null={player == null})");
            return HookResult.Continue;
        }

        var econ = hook.GetParam<CEconItemView>(1);
        if (econ == null) { Dbg("  выход: CEconItemView = null"); return HookResult.Continue; }

        var classname = WeaponName(econ.ItemDefinitionIndex);
        if (classname == null) { Dbg($"  выход: нет имени для defindex {econ.ItemDefinitionIndex}"); return HookResult.Continue; }
        Dbg($"  {player.PlayerName}: {classname} (defindex {econ.ItemDefinitionIndex}), способ {(int)hook.GetParam<AcquireMethod>(2)}, ограничения={WeaponRestrict.RestrictionsEnabled}");

        // Пропуск на одну выдачу (css_restrict_pass) — без проверок
        if (TakePass(player.Slot, classname))
        {
            Dbg("  выход: одноразовый пропуск");
            return HookResult.Continue;
        }

        var method = hook.GetParam<AcquireMethod>(2);

        // Check if this is a knife and knives are disabled
        if (!WeaponRestrict.KnivesEnabled &&
            (classname.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
             classname.Contains("bayonet", StringComparison.OrdinalIgnoreCase)))
        {
            hook.SetReturn(method != AcquireMethod.PickUp ? AcquireResult.AlreadyOwned : AcquireResult.InvalidItem);
            return HookResult.Stop;
        }

        if (Violation(player, classname, alreadyHeld: false) is not { } limit)
            return HookResult.Continue;

        NotifyBlocked(player, classname, limit);
        hook.SetReturn(method != AcquireMethod.PickUp ? AcquireResult.AlreadyOwned : AcquireResult.InvalidItem);
        return HookResult.Stop;
    }

    // ── подстраховка: хук не работает → забрать уже выданный ствол ──

    private HookResult OnItemPurchase(EventItemPurchase ev, GameEventInfo info)
    {
        var player = ev.Userid;
        if (player == null || !player.IsValid || _hookAlive) return HookResult.Continue;

        var weapon = ev.Weapon;
        if (!weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            weapon = "weapon_" + weapon;
        _purchases[player.Slot] = (weapon, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return HookResult.Continue;
    }

    private HookResult OnItemPickup(EventItemPickup ev, GameEventInfo info)
    {
        if (_hookAlive) return HookResult.Continue;

        var player = ev.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive) return HookResult.Continue;

        var classname = WeaponName((int)ev.Defindex);
        if (classname == null) return HookResult.Continue;

        if (!_fallbackAnnounced)
        {
            _fallbackAnnounced = true;
            Console.WriteLine("[WeaponRestrict] Хук CanAcquire не срабатывает (его перехватил другой плагин, " +
                              "например utils.so по строке \"CanAcquire\" в addons/configs/signatures.ini) — " +
                              "ограничения работают по событию получения оружия.");
        }

        Dbg($"item_pickup (подстраховка): {player.PlayerName}: {classname} (defindex {ev.Defindex})");

        if (TakePass(player.Slot, classname))
        {
            Dbg("  выход: одноразовый пропуск");
            return HookResult.Continue;
        }

        if (Violation(player, classname, alreadyHeld: true) is not { } limit)
            return HookResult.Continue;

        // Покупка прилетает отдельным событием — разбираемся кадром позже, когда оба уже пришли.
        var slot = player.Slot;
        Server.NextFrame(() =>
        {
            var p = Utilities.GetPlayerFromSlot(slot);
            if (p == null || !p.IsValid || !p.PawnIsAlive) return;

            var price = TakeAway(p, classname);
            if (price < 0) return;

            if (_purchases.TryGetValue(slot, out var buy) &&
                buy.Weapon.Equals(classname, StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - buy.At < 2000)
            {
                _purchases.Remove(slot);
                Refund(p, price);
                Dbg($"  забрал купленный {classname}, вернул ${price}");
            }
            else
            {
                Dbg($"  забрал {classname}");
            }

            NotifyBlocked(p, classname, limit);
        });
        return HookResult.Continue;
    }

    /// <summary>Убирает у игрока ствол этого класса. Цена из vdata, -1 — ствола не нашлось.</summary>
    private static int TakeAway(CCSPlayerController player, string classname)
    {
        var pawn = player.PlayerPawn?.Value;
        var ws = pawn?.WeaponServices;
        if (pawn == null || ws == null) return -1;

        foreach (var h in ws.MyWeapons)
        {
            var w = h.Value;
            if (w == null || !w.IsValid || !w.DesignerName.Equals(classname, StringComparison.OrdinalIgnoreCase))
                continue;

            var price = w.As<CCSWeaponBase>().VData?.Price ?? 0;
            // Как CCSPlayerController.RemoveItemByDesignerName: сделать активным, выбросить, удалить.
            ws.ActiveWeapon.Raw = h.Raw;
            player.DropActiveWeapon();
            w.AddEntityIOEvent("Kill", w, null, "", 0.1f);
            return price;
        }
        return -1;
    }

    private static void Refund(CCSPlayerController player, int price)
    {
        var money = player.InGameMoneyServices;
        if (money == null || price <= 0) return;
        money.Account += price;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }
}
