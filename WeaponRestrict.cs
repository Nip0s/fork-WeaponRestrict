using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using WeaponRestrict.Config;
using WeaponRestrict.Core;

namespace WeaponRestrict;

public class WeaponRestrict : BasePlugin, IPluginConfig<RestrictedWeaponsConfig>
{
    public override string ModuleName => "WeaponRestrict";
    public override string ModuleAuthor => "TICHOJEBEC & Modified";
    public override string ModuleVersion => "1.8.3";
    public override string ModuleDescription => "Weapon restrictions + knife/decoy control for custom rounds";

    public RestrictedWeaponsConfig Config { get; set; } = new();
    private Restrictor? _restrictor;

    // Global flags for custom rounds
    public static bool RestrictionsEnabled { get; set; } = true;
    public static bool KnivesEnabled { get; set; } = true;
    public static bool DecoyInstantRemove { get; set; } = false;
    public static bool KnifeDamageEnabled { get; set; } = true;

    public override void Load(bool hotReload)
    {
        _restrictor = new Restrictor(this);

        // Hook player spawn for knife control
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

        // Hook decoy started for instant remove
        RegisterEventHandler<EventDecoyStarted>(OnDecoyStarted);

        // Hook player hurt for knife damage control
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);

        base.Load(hotReload);

        Console.WriteLine("[WeaponRestrict] Plugin loaded with knife/decoy control support");
    }

    public override void Unload(bool hotReload)
    {
        _restrictor?.Dispose();
        _restrictor = null;
        base.Unload(hotReload);
    }

    public void OnConfigParsed(RestrictedWeaponsConfig config)
    {
        config.TypePlayers = (config.TypePlayers == 2) ? 2 : 1;
        config.TypeWeapons = (config.TypeWeapons == 2) ? 2 : 1;
        Config = config;
    }

    // Command: css_restrict_toggle <0|1>
    [ConsoleCommand("css_restrict_toggle", "Toggle weapon restrictions on/off")]
    [CommandHelper(minArgs: 1, usage: "<0|1>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRestrictToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int value))
        {
            command.ReplyToCommand("[WeaponRestrict] Usage: css_restrict_toggle <0|1>");
            return;
        }

        RestrictionsEnabled = value != 0;
        command.ReplyToCommand($"[WeaponRestrict] Weapon restrictions: {(RestrictionsEnabled ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[WeaponRestrict] Weapon restrictions: {(RestrictionsEnabled ? "ENABLED" : "DISABLED")}");
    }

    // Command: css_restrict_pass <slot> <weapon> [seconds]
    // Одноразовый пропуск: следующее получение этого оружия игроком в слоте <slot> в течение
    // [seconds] (по умолчанию 3) не проверяется. Для плагинов, выдающих оружие сами.
    [ConsoleCommand("css_restrict_pass", "One-time pass for a player to receive a restricted weapon")]
    [CommandHelper(minArgs: 2, usage: "<slot> <weapon> [seconds]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRestrictPassCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int slot) || slot < 0 || slot > 63)
        {
            command.ReplyToCommand("[WeaponRestrict] Usage: css_restrict_pass <slot> <weapon> [seconds]");
            return;
        }

        var weapon = command.GetArg(2).Trim();
        if (!weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase))
            weapon = "weapon_" + weapon;

        float seconds = 3f;
        if (command.ArgCount > 3 && float.TryParse(command.GetArg(3), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
            seconds = s;

        Restrictor.AddPass(slot, weapon, seconds);
        if (Config.Debug)
            Console.WriteLine($"[WeaponRestrict:debug] пропуск: слот {slot}, {weapon}, {seconds} с");
    }

    // Command: css_knife_toggle <0|1>
    [ConsoleCommand("css_knife_toggle", "Toggle knives on/off")]
    [CommandHelper(minArgs: 1, usage: "<0|1>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnKnifeToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int value))
        {
            command.ReplyToCommand("[WeaponRestrict] Usage: css_knife_toggle <0|1>");
            return;
        }

        bool newState = value != 0;

        if (newState == KnivesEnabled)
        {
            command.ReplyToCommand($"[WeaponRestrict] Knives already {(KnivesEnabled ? "ENABLED" : "DISABLED")}");
            return;
        }

        KnivesEnabled = newState;

        if (!KnivesEnabled)
        {
            // Remove knives from all players
            RemoveAllKnives();
            command.ReplyToCommand("[WeaponRestrict] Knives DISABLED - removed from all players");
        }
        else
        {
            // Give knives to all players
            GiveAllKnives();
            command.ReplyToCommand("[WeaponRestrict] Knives ENABLED - given to all players");
        }

        Console.WriteLine($"[WeaponRestrict] Knives: {(KnivesEnabled ? "ENABLED" : "DISABLED")}");
    }

    // Command: css_decoy_instant <0|1>
    [ConsoleCommand("css_decoy_instant", "Toggle instant decoy removal (removes after first sound)")]
    [CommandHelper(minArgs: 1, usage: "<0|1>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnDecoyInstantCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int value))
        {
            command.ReplyToCommand("[WeaponRestrict] Usage: css_decoy_instant <0|1>");
            return;
        }

        DecoyInstantRemove = value != 0;
        command.ReplyToCommand($"[WeaponRestrict] Decoy instant remove: {(DecoyInstantRemove ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[WeaponRestrict] Decoy instant remove: {(DecoyInstantRemove ? "ENABLED" : "DISABLED")}");
    }

    // Command: css_knife_damage <0|1>
    [ConsoleCommand("css_knife_damage", "Toggle knife damage on/off (knife stays but does no damage)")]
    [CommandHelper(minArgs: 1, usage: "<0|1>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnKnifeDamageCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.GetArg(1), out int value))
        {
            command.ReplyToCommand("[WeaponRestrict] Usage: css_knife_damage <0|1>");
            return;
        }

        KnifeDamageEnabled = value != 0;
        command.ReplyToCommand($"[WeaponRestrict] Knife damage: {(KnifeDamageEnabled ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[WeaponRestrict] Knife damage: {(KnifeDamageEnabled ? "ENABLED" : "DISABLED")}");
    }

    // Event handler for player spawn
    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        if (KnivesEnabled)
            return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid || !player.PawnIsAlive)
            return HookResult.Continue;

        // Remove knife after a short delay (to ensure it's spawned)
        AddTimer(0.5f, () =>
        {
            if (!KnivesEnabled && player.IsValid && player.PawnIsAlive)
            {
                RemovePlayerKnife(player);
            }
        });

        return HookResult.Continue;
    }

    // Event handler for decoy started - remove decoy after first sound
    private HookResult OnDecoyStarted(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!DecoyInstantRemove)
            return HookResult.Continue;

        var entityId = @event.Entityid;

        // Remove decoy after a very short delay (let it make one sound, then remove)
        AddTimer(0.1f, () =>
        {
            if (!DecoyInstantRemove)
                return;

            // Find and remove the decoy entity
            var decoy = Utilities.GetEntityFromIndex<CBaseEntity>(entityId);
            if (decoy != null && decoy.IsValid)
            {
                decoy.Remove();
            }
        });

        return HookResult.Continue;
    }

    // Event handler for player hurt - block knife damage when disabled
    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (KnifeDamageEnabled)
            return HookResult.Continue;

        var weapon = @event.Weapon;
        if (string.IsNullOrEmpty(weapon))
            return HookResult.Continue;

        // Check if damage was from knife
        if (!weapon.Contains("knife", StringComparison.OrdinalIgnoreCase) &&
            !weapon.Contains("bayonet", StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        var victim = @event.Userid;
        if (victim == null || !victim.IsValid || !victim.PawnIsAlive)
            return HookResult.Continue;

        var pawn = victim.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return HookResult.Continue;

        // Restore health and armor that was taken by knife
        int dmgHealth = @event.DmgHealth;
        int dmgArmor = @event.DmgArmor;

        if (dmgHealth > 0)
        {
            pawn.Health += dmgHealth;
            if (pawn.Health > pawn.MaxHealth)
                pawn.Health = pawn.MaxHealth;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        }

        if (dmgArmor > 0)
        {
            pawn.ArmorValue += dmgArmor;
            if (pawn.ArmorValue > 100)
                pawn.ArmorValue = 100;
        }

        return HookResult.Continue;
    }

    // Remove knives from all players
    public static void RemoveAllKnives()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !player.PawnIsAlive)
                continue;

            RemovePlayerKnife(player);
        }
    }

    // Give knives to all players
    public static void GiveAllKnives()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !player.PawnIsAlive)
                continue;

            GivePlayerKnife(player);
        }
    }

    // Remove knife from a specific player
    public static void RemovePlayerKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons == null)
            return;

        foreach (var weaponHandle in weapons)
        {
            var weapon = weaponHandle.Value;
            if (weapon == null || !weapon.IsValid)
                continue;

            var designerName = weapon.DesignerName;
            if (string.IsNullOrEmpty(designerName))
                continue;

            // Check if it's a knife
            if (designerName.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
                designerName.Contains("bayonet", StringComparison.OrdinalIgnoreCase))
            {
                weapon.Remove();
                Console.WriteLine($"[WeaponRestrict] Removed {designerName} from {player.PlayerName}");
                break;
            }
        }
    }

    // Give knife to a specific player
    public static void GivePlayerKnife(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn?.Value;
        if (pawn == null || !pawn.IsValid)
            return;

        // Check if player already has a knife
        var weapons = pawn.WeaponServices?.MyWeapons;
        if (weapons != null)
        {
            foreach (var weaponHandle in weapons)
            {
                var weapon = weaponHandle.Value;
                if (weapon == null || !weapon.IsValid)
                    continue;

                var designerName = weapon.DesignerName;
                if (!string.IsNullOrEmpty(designerName) &&
                    (designerName.Contains("knife", StringComparison.OrdinalIgnoreCase) ||
                     designerName.Contains("bayonet", StringComparison.OrdinalIgnoreCase)))
                {
                    // Already has a knife
                    return;
                }
            }
        }

        // Give knife
        player.GiveNamedItem("weapon_knife");
        Console.WriteLine($"[WeaponRestrict] Gave knife to {player.PlayerName}");
    }
}
