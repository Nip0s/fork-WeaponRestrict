# WeaponRestrict
[Оригинал плагина](https://github.com/CS2Plugins/WeaponRestrict) </br>
Автор оригинального плагина: [Whaliin](https://github.com/Whaliin)

# Изменения в моей версии:
- Иммунитет не действует на блок weaponlimits, теперь можно отключать оружия значением 0 и вип игрокам не будут доступны эти оружия
- Добавлен блок с ограничением по количеству игроков в команде

**Если вы нашли какой-то баг или ошибку, напишите мне в дискорде: NiPos**

# Установка
1. Скачать последний [релиз](https://github.com/Nip0s/Restrict-weapon/releases)
2. Закинуть папку `addons` из архива в папку `/game/csgo`
3. Конфиг автоматически генерируется по пути `/game/csgo/addons/counterstrikesharp/configs/plugins/WeaponRestrict/WeaponRestrict.json` 
6. *(Optional) Reload the plugin using `css_plugins reload [plugin index]` to update the loaded config (you can get the index by running `css_plugins list`)*

# Конфиг
`/game/csgo/addons/counterstrikesharp/configs/plugins/WeaponRestrict/WeaponRestrict.json`

Комментарии ниже — только для пояснения, в настоящий файл их переносить не нужно.
```jsonc
{
  "ChatPrefix": "{red} ● WR » {default}", // Префикс сообщений в чате. Цвета: {default} {white} {red} {lightred} {darkred} {green} {lime} {olive} {yellow} {gold} {orange} {blue} {lightblue} {darkblue} {purple} {lightpurple} {magenta} {grey} {silver} {bluegrey}

  "TypePlayers": 1,       // По скольким игрокам выбирается правило из Rules: 1 — все игроки сервера, 2 — только команда покупающего
  "TypeWeapons": 2,       // Как считается «уже на руках»: 1 — оружие у всех игроков сервера, 2 — только в команде покупающего
  "CountSpectators": false, // Считать ли наблюдателей и игроков без команды при подсчёте игроков

  "BypassPermissions": [  // Флаги CSS, которые обходят лимиты (VIP, админы)
    "@vip/restrict",
    "@css/root"
  ],
  "BypassAllowedWhenLimitIsZero": true, // true — флаги выше обходят и лимит 0; false — лимит 0 запрещает оружие всем, включая VIP и админов
  "NoBypassWeapons": [    // Оружие под жёстким запретом: флаги обхода на него не действуют вообще
    "weapon_g3sg1",
    "weapon_scar20"
  ],

  "BlockSound": null,     // Звук игроку при блокировке — путь к звуку (.vsnd / .vsnd_c); null — без звука
  "Debug": false,         // Подробный лог каждой проверки в консоль сервера — для разбора «ограничение не работает»

  "Phrases": {
    "Block": "Оружие ограничено: {gold}{weapon}{default} {grey}(доступно: {limit}).",                     // Сообщение при TypeWeapons = 1
    "BlockTeam": "Оружие ограничено для вашей команды: {gold}{weapon}{default} {grey}(доступно: {limit}).", // Сообщение при TypeWeapons = 2
    "WeaponPretty": {     // Названия оружия для подстановки {weapon}; если оружия нет в списке — выводится имя без weapon_ заглавными
      "weapon_awp": "AWP",
      "weapon_g3sg1": "G3SG1",
      "weapon_scar20": "SCAR-20"
      // ... остальное оружие плагин дописывает сам при генерации конфига
    }
  },

  "DefIndexToClass": {    // Справочник «номер предмета → класс оружия». Генерируется автоматически, на ограничения не влияет — можно не трогать
    "9": "weapon_awp"
    // ...
  },

  "Rules": {              // Сами ограничения
    "all": {              // "all" — для всех карт. Можно добавить блок с именем карты ("de_mirage": { ... }) — тогда на этой карте он полностью заменяет "all"
      "1": {              // Порог: правило действует, когда игроков (см. TypePlayers) не меньше этого числа. Берётся наибольший подходящий порог
        "weapon_awp": 1   // Лимит: сколько единиц можно держать одновременно (см. TypeWeapons). 0 — запрещено, -1 — без ограничений
      },
      "10": {             // От 10 игроков
        "weapon_awp": 2,
        "weapon_g3sg1": 0,
        "weapon_scar20": 0
      }
    }
  }
}
```

# Команды (только из консоли сервера)
| Команда | Что делает |
|---|---|
| `css_restrict_toggle <0\|1>` | Выключить / включить все ограничения (например, на время особого раунда) |
| `css_restrict_pass <слот> <оружие> [секунды]` | Разовый пропуск: следующее получение этого оружия игроком в слоте не проверяется (по умолчанию 3 с). Для плагинов, которые сами выдают оружие |
| `css_knife_toggle <0\|1>` | Забрать / выдать ножи всем |
| `css_knife_damage <0\|1>` | Нож остаётся, но не наносит урон |
| `css_decoy_instant <0\|1>` | Удалять декой сразу после первого звука |
| `css_restrict_probe` | Отладка: на какой адрес встал хук `CanAcquire` |

# Совместимость с utils.so
Если в `addons/configs/signatures.ini` есть строка `"CanAcquire"` (по умолчанию она там есть), Metamod-плагин `utils` перехватывает функцию раньше CounterStrikeSharp, и штатный хук CSS промахивается мимо неё. Начиная с v1.8.4 плагин находит функцию сам и встаёт поверх перехвата `utils` — ничего настраивать не нужно, другие Metamod-плагины продолжают получать вызовы.
