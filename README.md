# WeaponRestrict
[Оригинал плагина](https://github.com/CS2Plugins/WeaponRestrict) </br>
Автор оригинального плагина: [TICHOJEBEC](https://github.com/tichotm)

# Изменения в моей версии:
- Иммунитет не действует на хард-бан (`NoBypassWeapons` и лимит `0` при `BypassAllowedWhenLimitIsZero: false`)
- Лимиты по картам и динамическим порогам числа игроков (`Rules`)
- Управление ножами, decoy и уроном ножа для кастомных раундов (команды ниже)

**Если вы нашли какой-то баг или ошибку, напишите мне в дискорде: NiPos**

# Установка
1. Скачать последний [релиз](https://github.com/Nip0s/Weapon-restrict-fix/releases)
2. Закинуть папку `WeaponRestrict` из архива в папку `/game/csgo/addons/counterstrikesharp/plugins/`
3. Конфиг автоматически генерируется по пути `/game/csgo/addons/counterstrikesharp/configs/plugins/WeaponRestrict/WeaponRestrict.json`

# Команды (только консоль сервера / RCON)
| Команда | Описание |
|---------|----------|
| `css_restrict_toggle <0\|1>` | Вкл/выкл ограничения оружия |
| `css_knife_toggle <0\|1>` | Выдать/забрать ножи у всех игроков |
| `css_decoy_instant <0\|1>` | Мгновенное удаление decoy после первого звука |
| `css_knife_damage <0\|1>` | Вкл/выкл урон от ножа |

# Конфиг
`/game/csgo/addons/counterstrikesharp/configs/plugins/WeaponRestrict/WeaponRestrict.json`
```json
{
  "ChatPrefix": "{green}[{default}WR{green}]",
  "TypePlayers": 1,        // 1 = все игроки, 2 = только тиммейты
  "TypeWeapons": 1,        // 1 = глобально, 2 = per-team
  "CountSpectators": false,
  "BypassPermissions": [
    "@vip/restrict",
    "@css/root"
  ],
  "BypassAllowedWhenLimitIsZero": false, // false = VIP не обходит лимит 0 (хард-бан)
  "NoBypassWeapons": [],                 // Оружия, которые никто не может обойти
  "BlockSound": "",
  "Rules": {
    "all": {              // Работает на всех картах
      "1": {             // Порог: от 1 игрока
        "weapon_awp": 1
      },
      "6": {             // Порог: от 6 игроков
        "weapon_awp": 2
      },
      "10": {
        "weapon_awp": 4
      }
    },
    "de_dust2": {        // Переопределение для конкретной карты
      "1": {
        "weapon_awp": 0
      }
    }
  },
  "ConfigVersion": 1
}
```

### Как работают правила (Rules)
Плагин выбирает **наибольший ключ ≤ текущему числу игроков**.
- `0` = полный бан
- `-1` = без ограничений
- `N` = максимум N штук
